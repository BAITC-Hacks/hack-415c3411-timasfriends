"""QuestBridge API. All profile switches are explicitly a local demo convention."""

from contextlib import asynccontextmanager
import hashlib
import json
import sqlite3
from typing import Annotated, Literal

from fastapi import FastAPI, Header, Path, Query, Request
from fastapi.exceptions import RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException

from .ai import AIService
from .config import Settings
from .db import Database, add_event, dumps, new_id, utc_now
from .models import (
    CatalogCard, CatalogResponse, ChatRequest, ChatResponse, ConfirmMilestoneRequest,
    DecisionRequest, Draft, ErrorBody, ErrorResponse, Event, EventsResponse, FieldSource,
    HealthResponse, MilestoneRequest, MilestoneResponse, PreviewRequest, PreviewResponse,
    ProposalDetails, ProposalListResponse, ProposalRequest, ProposalResponse,
    PublishTaskRequest, TaskResponse, Team, UpdateTaskRequest,
)
from .scoring import is_filled, score_draft


Identifier = Annotated[str, Path(min_length=1, max_length=80, pattern=r"^[A-Za-z0-9_-]+$")]


class APIError(Exception):
    def __init__(self, status: int, code: str, message: str):
        self.status, self.code, self.message = status, code, message


def error_response(status: int, code: str, message: str) -> JSONResponse:
    body = ErrorResponse(error=ErrorBody(code=code, message=message))
    return JSONResponse(status_code=status, content=body.model_dump(mode="json"))


class BodyLimitMiddleware:
    """Bound even chunked requests before JSON parsing or provider invocation."""

    def __init__(self, app, max_bytes: int):
        self.app, self.max_bytes = app, max_bytes

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http" or scope["method"] not in {"POST", "PATCH", "PUT"}:
            return await self.app(scope, receive, send)
        chunks, size = [], 0
        while True:
            message = await receive()
            if message["type"] == "http.disconnect":
                return
            chunk = message.get("body", b"")
            size += len(chunk)
            if size > self.max_bytes:
                response = error_response(413, "body_too_large", "Размер запроса превышает 64 KiB.")
                return await response(scope, receive, send)
            chunks.append(chunk)
            if not message.get("more_body", False):
                break
        delivered = False

        async def bounded_receive():
            nonlocal delivered
            if not delivered:
                delivered = True
                return {"type": "http.request", "body": b"".join(chunks), "more_body": False}
            return await receive()

        return await self.app(scope, bounded_receive, send)


def require_row(conn, table: str, row_id: str):
    # Table names are internal constants; user values are always bound parameters.
    if table not in {"tasks", "teams", "businesses", "conversations", "proposals", "milestones"}:
        raise ValueError("Unsupported table")
    row = conn.execute(f"SELECT * FROM {table} WHERE id=?", (row_id,)).fetchone()
    if row is None:
        raise APIError(404, "not_found", "Запрошенная запись не найдена.")
    return row


def check_owner(conn, task_row, business_id: str):
    require_row(conn, "businesses", business_id)
    if task_row["business_id"] != business_id:
        raise APIError(403, "business_mismatch", "Задача принадлежит другому демонстрационному бизнесу.")


def team_response(conn, team_id: str) -> Team:
    row = require_row(conn, "teams", team_id)
    return Team(**json.loads(row["profile"]), completed=row["completed"], experience=row["experience"])


def task_response(conn, task_id: str) -> TaskResponse:
    row = require_row(conn, "tasks", task_id)
    body = json.loads(row["body"])
    teams = conn.execute("SELECT team_id FROM proposals WHERE task_id=? ORDER BY team_id", (task_id,)).fetchall()
    body.update(teamIds=[team["team_id"] for team in teams], proposalCount=len(teams))
    return TaskResponse.model_validate(body)


def proposal_response(conn, proposal_id: str, business_view: bool = False) -> ProposalResponse:
    row = require_row(conn, "proposals", proposal_id)
    details = None
    if row["publish_details"] or business_view:
        details = ProposalDetails(idea=row["idea"], plan=row["plan"], timeline=row["timeline"], prototypeUrl=row["prototype_url"])
    return ProposalResponse(
        id=row["id"], taskId=row["task_id"], teamId=row["team_id"], team=team_response(conn, row["team_id"]),
        decision=row["decision"], publishDetails=bool(row["publish_details"]), details=details,
        createdAt=row["created_at"], updatedAt=row["updated_at"],
    )


def milestone_response(conn, milestone_id: str) -> MilestoneResponse:
    row = require_row(conn, "milestones", milestone_id)
    proposal = require_row(conn, "proposals", row["proposal_id"])
    return MilestoneResponse(
        id=row["id"], proposalId=row["proposal_id"], taskId=proposal["task_id"], teamId=proposal["team_id"],
        description=row["description"], confirmed=bool(row["confirmed"]), experienceAwarded=row["experience_awarded"],
        createdAt=row["created_at"], confirmedAt=row["confirmed_at"], team=team_response(conn, proposal["team_id"]),
    )


def require_publishable(body: PublishTaskRequest):
    if body.confirmed is not True:
        raise APIError(409, "confirmation_required", "Проверьте карточку и явно подтвердите публикацию.")
    if not is_filled(body.draft.title) or not is_filled(body.draft.category):
        raise APIError(422, "title_category_required", "Для публикации нужны название и категория.")


def sources_for_task(conn, body: PublishTaskRequest, previous: TaskResponse | None = None) -> list[FieldSource]:
    if body.conversationId:
        conv = require_row(conn, "conversations", body.conversationId)
        known = Draft.model_validate_json(conv["draft"])
        sources = [FieldSource.model_validate(s) for s in json.loads(conv["sources"])]
    elif previous is not None:
        known, sources = previous.draft, previous.sources
    else:
        return []
    return [source for source in sources if getattr(body.draft, source.field) and getattr(body.draft, source.field) == getattr(known, source.field)]


def replay_publication(conn, business_id: str, key: str | None, fingerprint: str) -> TaskResponse | None:
    if key is None:
        return None
    row = conn.execute("SELECT * FROM idempotency WHERE business_id=? AND key=?", (business_id, key)).fetchone()
    if row is None:
        return None
    if row["fingerprint"] != fingerprint:
        raise APIError(409, "idempotency_conflict", "Этот Idempotency-Key уже использован с другим запросом.")
    return TaskResponse.model_validate_json(row["response"])


def create_app(settings: Settings | None = None, ai_service: AIService | None = None) -> FastAPI:
    settings = settings or Settings.from_env()
    db = Database(settings.db_path)
    ai = ai_service or AIService(api_key=settings.ai_api_key, model=settings.ai_model, timeout=settings.ai_timeout)

    @asynccontextmanager
    async def lifespan(app):
        db.initialize()
        # Profiles are always available, even when synthetic tasks are disabled.
        from .seed import seed
        seed(db, include_tasks=settings.seed_demo)
        yield

    app = FastAPI(
        title="QuestBridge demo API", version="1.0.0", lifespan=lifespan,
        responses={status: {"model": ErrorResponse} for status in (400, 403, 404, 409, 413, 422, 500, 503)},
    )
    app.state.db, app.state.ai, app.state.settings = db, ai, settings
    app.add_middleware(BodyLimitMiddleware, max_bytes=settings.max_body_bytes)
    app.add_middleware(
        CORSMiddleware, allow_origins=list(settings.cors_origins), allow_credentials=False,
        allow_methods=["GET", "POST", "PATCH", "OPTIONS"], allow_headers=["Content-Type", "Idempotency-Key"],
    )

    @app.exception_handler(APIError)
    async def domain_error(request: Request, exc: APIError):
        return error_response(exc.status, exc.code, exc.message)

    @app.exception_handler(RequestValidationError)
    async def invalid_request(request: Request, exc: RequestValidationError):
        return error_response(422, "validation_error", "Проверьте обязательные поля, типы, длины и допустимые значения запроса.")

    @app.exception_handler(HTTPException)
    async def http_error(request: Request, exc: HTTPException):
        return error_response(exc.status_code, "not_found" if exc.status_code == 404 else "http_error", "Запрошенный адрес или метод недоступен.")

    @app.exception_handler(sqlite3.OperationalError)
    async def database_error(request: Request, exc: sqlite3.OperationalError):
        return error_response(503, "storage_unavailable", "Хранилище временно недоступно. Повторите запрос.")

    @app.exception_handler(Exception)
    async def unexpected_error(request: Request, exc: Exception):
        return error_response(500, "internal_error", "Не удалось обработать запрос.")

    @app.get("/health", response_model=HealthResponse)
    def health():
        with db.read() as conn:
            conn.execute("SELECT 1").fetchone()
        return HealthResponse(aiMode="live" if settings.ai_api_key and settings.ai_model else "fallback")

    @app.get("/api/catalog", response_model=CatalogResponse)
    def catalog(category: Annotated[str | None, Query(max_length=100)] = None, readinessLevel: Literal["draft", "workable", "ready", "priority"] | None = None):
        with db.read() as conn:
            version = conn.execute("SELECT value FROM metadata WHERE key='catalog_version'").fetchone()[0]
            tasks = [task_response(conn, row["id"]) for row in conn.execute("SELECT id FROM tasks ORDER BY id").fetchall()]
            if category is not None:
                tasks = [task for task in tasks if task.draft.category == category]
            if readinessLevel is not None:
                tasks = [task for task in tasks if task.readinessLevel == readinessLevel]
            tasks.sort(key=lambda task: (-task.readiness, -task.clarity, task.id))
            teams = [team_response(conn, row["id"]) for row in conn.execute("SELECT id FROM teams ORDER BY id").fetchall()]
            cards = [CatalogCard(id=t.id, title=t.draft.title, description=t.draft.need or t.draft.context,
                                 category=t.draft.category, readiness=t.readiness, clarity=t.clarity, teamIds=t.teamIds) for t in tasks]
            return CatalogResponse(version=version, cards=cards, teams=teams)

    @app.post("/api/chat", response_model=ChatResponse)
    async def chat(body: ChatRequest):
        conversation_id = body.conversationId or new_id("conv")
        with db.read() as conn:
            if body.conversationId:
                row = require_row(conn, "conversations", conversation_id)
                history, draft = json.loads(row["history"]), Draft.model_validate_json(row["draft"])
                sources = [FieldSource.model_validate(s) for s in json.loads(row["sources"])]
                revision = row["revision"]
            else:
                history, draft, sources, revision = [], Draft(), [], 0
        if sum(item["role"] == "user" for item in history) >= 50:
            raise APIError(409, "conversation_limit", "Сессия достигла 50 сообщений. Создайте новую сессию; сохранённый черновик остаётся в базе.")
        history.append({"id": new_id("msg"), "role": "user", "content": body.message})
        response = await ai.chat(conversation_id=conversation_id, history=history, draft=draft, sources=sources)
        response = ChatResponse.model_validate(response)
        history.append({"id": new_id("msg"), "role": "assistant", "content": response.message})
        # A first clarifying answer may intentionally keep draft:null. The provider
        # can reconstruct its known fields from the complete server-side history.
        stored_draft = response.draft if response.draft is not None else draft
        stored_sources = response.sources if response.draft is not None else sources
        with db.write() as conn:
            if revision:
                result = conn.execute(
                    "UPDATE conversations SET history=?,draft=?,sources=?,revision=revision+1 WHERE id=? AND revision=?",
                    (dumps(history), stored_draft.model_dump_json(), dumps([s.model_dump() for s in stored_sources]), conversation_id, revision),
                )
                if result.rowcount != 1:
                    raise APIError(409, "conversation_conflict", "Сессия уже обновлена другим запросом. Повторите сообщение.")
            else:
                conn.execute("INSERT INTO conversations(id,history,draft,sources) VALUES (?,?,?,?)",
                             (conversation_id, dumps(history), stored_draft.model_dump_json(), dumps([s.model_dump() for s in stored_sources])))
        return response

    @app.post("/api/tasks/preview", response_model=PreviewResponse)
    def preview(body: PreviewRequest):
        return score_draft(body.draft, body.confirmedFields)

    @app.post("/api/tasks", response_model=TaskResponse, status_code=201)
    async def publish(body: PublishTaskRequest, idempotency_key: Annotated[str | None, Header(alias="Idempotency-Key", min_length=1, max_length=128, pattern=r"^[A-Za-z0-9_.:-]+$")] = None):
        require_publishable(body)
        fingerprint = hashlib.sha256(dumps(body.model_dump()).encode("utf-8")).hexdigest()
        with db.read() as conn:
            require_row(conn, "businesses", body.businessId)
            replay = replay_publication(conn, body.businessId, idempotency_key, fingerprint)
            if replay is not None:
                return replay
            sources_for_task(conn, body)
        clarity = await ai.clarity(body.draft)
        timestamp, task_id = utc_now(), new_id("task")
        with db.write() as conn:
            replay = replay_publication(conn, body.businessId, idempotency_key, fingerprint)
            if replay is not None:
                return replay
            task = TaskResponse(
                id=task_id, businessId=body.businessId, draft=body.draft, confirmed=True,
                confirmedFields=body.confirmedFields, **score_draft(body.draft, body.confirmedFields).model_dump(),
                **clarity.model_dump(), version=1, createdAt=timestamp, updatedAt=timestamp,
                teamIds=[], proposalCount=0, sources=sources_for_task(conn, body), demo=False,
            )
            conn.execute("INSERT INTO tasks(id,business_id,body) VALUES (?,?,?)", (task_id, body.businessId, task.model_dump_json()))
            add_event(conn, "task_published", task_id, "Опубликована новая задача", timestamp)
            if idempotency_key is not None:
                conn.execute("INSERT INTO idempotency(business_id,key,fingerprint,response) VALUES (?,?,?,?)",
                             (body.businessId, idempotency_key, fingerprint, task.model_dump_json()))
        return task

    @app.get("/api/tasks/{task_id}", response_model=TaskResponse)
    def get_task(task_id: Identifier):
        with db.read() as conn:
            return task_response(conn, task_id)

    @app.patch("/api/tasks/{task_id}", response_model=TaskResponse)
    async def update_task(task_id: Identifier, body: UpdateTaskRequest):
        require_publishable(body)
        with db.read() as conn:
            check_owner(conn, require_row(conn, "tasks", task_id), body.businessId)
            previous = task_response(conn, task_id)
            if body.version != previous.version:
                raise APIError(409, "version_conflict", "Карточка уже изменена. Загрузите её актуальную версию.")
            changed = {field for field in Draft.model_fields if getattr(body.draft, field) != getattr(previous.draft, field)}
            if (changed & set(body.confirmedFields)) - set(body.reconfirmedFields):
                raise APIError(409, "reconfirmation_required", "Изменённые поля требуют повторного подтверждения в reconfirmedFields или удаления из confirmedFields.")
            sources_for_task(conn, body, previous)
        clarity = await ai.clarity(body.draft) if changed else None
        with db.write() as conn:
            current = task_response(conn, task_id)
            if current.version != body.version:
                raise APIError(409, "version_conflict", "Карточка уже изменена. Загрузите её актуальную версию.")
            updated = current.model_dump()
            updated.update(draft=body.draft.model_dump(), confirmedFields=body.confirmedFields,
                           sources=[s.model_dump() for s in sources_for_task(conn, body, current)],
                           version=current.version + 1, updatedAt=utc_now())
            updated.update(score_draft(body.draft, body.confirmedFields).model_dump())
            if clarity is not None:
                updated.update(clarity.model_dump())
            task = TaskResponse.model_validate(updated)
            conn.execute("UPDATE tasks SET body=? WHERE id=?", (task.model_dump_json(), task_id))
            add_event(conn, "task_updated", task_id, "Карточка задачи обновлена", task.updatedAt)
        return task

    @app.post("/api/tasks/{task_id}/proposals", response_model=ProposalResponse, status_code=201)
    def submit_proposal(task_id: Identifier, body: ProposalRequest):
        with db.write() as conn:
            require_row(conn, "tasks", task_id)
            require_row(conn, "teams", body.teamId)
            if conn.execute("SELECT id FROM proposals WHERE task_id=? AND team_id=?", (task_id, body.teamId)).fetchone():
                raise APIError(409, "proposal_exists", "Эта команда уже подала предложение на задачу.")
            proposal_id, timestamp = new_id("proposal"), utc_now()
            conn.execute(
                "INSERT INTO proposals(id,task_id,team_id,idea,plan,timeline,prototype_url,publish_details,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?)",
                (proposal_id, task_id, body.teamId, body.idea, body.plan, body.timeline, str(body.prototypeUrl), int(body.publishDetails), timestamp, timestamp),
            )
            add_event(conn, "proposal_submitted", task_id, "Команда подала предложение", timestamp)
            # The submitting client receives its own details. Public reads redact them.
            return proposal_response(conn, proposal_id, business_view=True)

    @app.get("/api/tasks/{task_id}/proposals", response_model=ProposalListResponse)
    def list_proposals(task_id: Identifier, scope: Literal["public", "demo-business"] = "public", businessId: Annotated[str | None, Query(max_length=80)] = None):
        with db.read() as conn:
            task_row = require_row(conn, "tasks", task_id)
            if scope == "demo-business":
                if not businessId:
                    raise APIError(422, "business_required", "Демонстрационное бизнес-представление требует businessId.")
                check_owner(conn, task_row, businessId)
            rows = conn.execute("SELECT id FROM proposals WHERE task_id=? ORDER BY created_at,id", (task_id,)).fetchall()
            return ProposalListResponse(proposals=[proposal_response(conn, row["id"], scope == "demo-business") for row in rows])

    @app.patch("/api/proposals/{proposal_id}/decision", response_model=ProposalResponse)
    def decide(proposal_id: Identifier, body: DecisionRequest):
        with db.write() as conn:
            row = require_row(conn, "proposals", proposal_id)
            check_owner(conn, require_row(conn, "tasks", row["task_id"]), body.businessId)
            if row["decision"] != body.decision:
                timestamp = utc_now()
                conn.execute("UPDATE proposals SET decision=?,updated_at=? WHERE id=?", (body.decision, timestamp, proposal_id))
                if body.decision == "selected":
                    add_event(conn, "team_selected", row["task_id"], "Бизнес выбрал команду", timestamp)
                else:
                    conn.execute("UPDATE metadata SET value=value+1 WHERE key='catalog_version'")
            return proposal_response(conn, proposal_id, business_view=True)

    @app.post("/api/proposals/{proposal_id}/milestones", response_model=MilestoneResponse, status_code=201)
    def submit_milestone(proposal_id: Identifier, body: MilestoneRequest):
        with db.write() as conn:
            row = require_row(conn, "proposals", proposal_id)
            require_row(conn, "teams", body.teamId)
            if row["team_id"] != body.teamId:
                raise APIError(403, "team_mismatch", "Этап может подать только команда этого предложения.")
            if row["decision"] != "selected":
                raise APIError(409, "team_not_selected", "Сначала бизнес должен выбрать команду.")
            milestone_id = new_id("milestone")
            conn.execute("INSERT INTO milestones(id,proposal_id,description,created_at) VALUES (?,?,?,?)",
                         (milestone_id, proposal_id, body.description, utc_now()))
            return milestone_response(conn, milestone_id)

    @app.post("/api/milestones/{milestone_id}/confirm", response_model=MilestoneResponse)
    def confirm_milestone(milestone_id: Identifier, body: ConfirmMilestoneRequest):
        with db.write() as conn:
            milestone = require_row(conn, "milestones", milestone_id)
            proposal = require_row(conn, "proposals", milestone["proposal_id"])
            check_owner(conn, require_row(conn, "tasks", proposal["task_id"]), body.businessId)
            if milestone["confirmed"]:
                return milestone_response(conn, milestone_id)
            if proposal["decision"] != "selected":
                raise APIError(409, "team_not_selected", "Подтвердить можно только этап выбранной команды.")
            timestamp = utc_now()
            conn.execute("UPDATE milestones SET confirmed=1,experience_awarded=10,confirmed_at=? WHERE id=? AND confirmed=0", (timestamp, milestone_id))
            conn.execute("UPDATE teams SET completed=completed+1,experience=experience+10 WHERE id=?", (proposal["team_id"],))
            add_event(conn, "milestone_confirmed", proposal["task_id"], "Бизнес подтвердил этап; команде начислено 10 опыта", timestamp)
            return milestone_response(conn, milestone_id)

    @app.get("/api/events", response_model=EventsResponse)
    def events(after: Annotated[int, Query(ge=0, le=9223372036854775807)] = 0, limit: Annotated[int, Query(ge=1, le=200)] = 100):
        with db.read() as conn:
            rows = conn.execute("SELECT * FROM events WHERE id>? ORDER BY id LIMIT ?", (after, limit)).fetchall()
            result = [Event(id=row["id"], type=row["type"], taskId=row["task_id"], message=row["message"], createdAt=row["created_at"]) for row in rows]
            return EventsResponse(cursor=result[-1].id if result else after, events=result)

    return app


app = create_app()
