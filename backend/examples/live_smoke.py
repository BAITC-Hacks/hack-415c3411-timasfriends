"""Exercise real AI and the API with synthetic data in an isolated SQLite database.

Run from the repository root: python -m backend.examples.live_smoke
Requires backend/requirements-dev.txt and a configured backend/.env.
"""

from dataclasses import replace
from datetime import datetime, timezone
import json
import re
import sys
from time import perf_counter
from uuid import uuid4

from dotenv import load_dotenv
from fastapi.testclient import TestClient

from backend.app.config import BACKEND_DIR, Settings
from backend.app.main import create_app
from backend.app.scoring import is_filled


FIRST_MESSAGE = (
    "В вымышленном учебном центре «Орбита» преподаватели вручную проверяют "
    "пробные SAT и тратят много времени."
)
SECOND_MESSAGE = (
    "Назовём задачу «ДЕМО: Помощник проверки SAT». Категория — Образование. "
    "Пользоваться будут преподаватели учебного центра «Орбита». "
    "Нам нужно сократить время ручной проверки. Для работы доступны "
    "30 синтетических работ с ответами и эталонами. Команда должна передать "
    "браузерный прототип загрузки работ и таблица оценок с возможностью "
    "исправления преподавателем. Критерий успеха: не менее 27 из 30 оценок "
    "совпадают с эталоном. Ограничения: только синтетические данные; "
    "окончательную оценку подтверждает преподаватель. Контакт пока неизвестен. "
    "Формат взаимодействия неизвестен. Процесс обратной связи пока неизвестен."
)
CONFIRMED_FIELDS = [
    "context", "need", "users", "data", "constraints", "expectedResult", "successCriteria",
]
UNKNOWN_FIELDS = ["contact", "interactionFormat", "feedbackProcess"]


def main() -> int:
    load_dotenv(BACKEND_DIR / ".env")
    settings = Settings.from_env()
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid4().hex[:8]
    output_dir = BACKEND_DIR / "data" / f"live-smoke-{run_id}"
    output_dir.mkdir(parents=True, exist_ok=False)
    settings = replace(settings, db_path=str(output_dir / "questbridge.sqlite3"), seed_demo=False)
    report_path = output_dir / "report.json"
    report = {
        "runId": run_id, "synthetic": True, "status": "running", "model": settings.ai_model,
        "configuredTimeoutSeconds": settings.ai_timeout, "database": settings.db_path,
        "reasoningEffort": settings.ai_reasoning_effort or "provider default",
        "transport": "FastAPI TestClient; real external AI HTTP requests",
        "inputs": [FIRST_MESSAGE, SECOND_MESSAGE], "aiModeByStage": {}, "steps": [], "checks": [],
    }

    def safe(value):
        if isinstance(value, str):
            if settings.ai_api_key:
                value = value.replace(settings.ai_api_key, "[REDACTED]")
            return re.sub(r"sk-[A-Za-z0-9_-]+", "[REDACTED]", value)
        if isinstance(value, dict):
            return {safe(key): safe(item) for key, item in value.items()}
        if isinstance(value, list):
            return [safe(item) for item in value]
        return value

    def save():
        report_path.write_text(json.dumps(safe(report), ensure_ascii=False, indent=2), encoding="utf-8")

    def check(name, passed):
        report["checks"].append({"name": name, "passed": bool(passed)})
        save()
        if not passed:
            raise AssertionError(name)

    def call(client, name, method, path, body=None, status=200, headers=None):
        started = perf_counter()
        step = {"name": name, "method": method, "path": path, "request": body}
        report["steps"].append(step)
        save()
        try:
            response = client.request(method, path, json=body, headers=headers)
            step.update(seconds=round(perf_counter() - started, 3), statusCode=response.status_code)
            data = response.json()
            step["response"] = data
        except Exception as exc:
            step.update(seconds=round(perf_counter() - started, 3), errorType=type(exc).__name__)
            save()
            raise
        check(f"{name}: HTTP {status}", response.status_code == status)
        print(safe(f"{name}: HTTP {response.status_code}, {step['seconds']} s"), flush=True)
        return data

    def live(name, response):
        report["aiModeByStage"][name] = response.get("aiMode")
        check(f"{name}: real AI", response.get("aiMode") == "live")

    started = perf_counter()
    save()
    try:
        check("AI key and model configured", bool(settings.ai_api_key and settings.ai_model))
        app = create_app(settings)
        with TestClient(app) as client:
            initial = call(client, "initial catalog", "GET", "/api/catalog")
            first = call(client, "first chat", "POST", "/api/chat", {"message": FIRST_MESSAGE})
            live("firstChat", first)
            check("first chat asks at least three distinct questions", first["phase"] == "clarifying"
                  and len({q["field"] for q in first["questions"]}) >= 3)
            ready = call(client, "second chat", "POST", "/api/chat", {
                "conversationId": first["conversationId"], "message": SECOND_MESSAGE,
            })
            live("secondChat", ready)
            check("follow-up preserves conversation identity", ready["conversationId"] == first["conversationId"])
            check("follow-up produces editable draft", ready["phase"] == "draft_ready"
                  and ready["questions"] == [] and isinstance(ready["draft"], dict))
            draft = ready["draft"]
            check("AI does not assign readiness", "readiness" not in first and "readiness" not in ready)
            check("seven supplied fields filled", all(is_filled(draft[field]) for field in CONFIRMED_FIELDS))
            check("unknown contact fields stay empty", all(draft[field] == "" for field in UNKNOWN_FIELDS))
            check("explicit title and category extracted", "Помощник проверки SAT" in draft["title"]
                  and draft["category"] == "Образование")
            anchors = {"context": ["SAT"], "need": ["время", "проверки"], "users": ["преподаватели", "Орбита"],
                       "data": ["30", "синтетических", "эталонами"], "constraints": ["синтетические", "преподаватель"],
                       "expectedResult": ["прототип", "оценок", "исправления"], "successCriteria": ["27", "30", "эталоном"]}
            check("supplied facts retain their meaning", all(
                all(word in draft[field] for word in words) for field, words in anchors.items()))
            with app.state.db.read() as conn:
                row = conn.execute("SELECT history FROM conversations WHERE id=?", (ready["conversationId"],)).fetchone()
            users = {m["id"]: m["content"] for m in json.loads(row["history"]) if m["role"] == "user"}
            sources = {source["field"]: source for source in ready["sources"]}
            check("each source matches actual user message", all(
                source["messageId"] in users and source["quote"] in users[source["messageId"]]
                and draft[source["field"]] in source["quote"] for source in ready["sources"]))
            check("every filled field has a source", all(field in sources for field, value in draft.items() if is_filled(value)))
            empty = call(client, "unconfirmed preview", "POST", "/api/tasks/preview", {"draft": draft})
            preview = call(client, "confirmed preview", "POST", "/api/tasks/preview", {
                "draft": draft, "confirmedFields": CONFIRMED_FIELDS,
            })
            check("readiness grows from zero to 90", empty["readiness"] == 0 and preview["readiness"] == 90)
            body = {"businessId": "business-demo", "draft": draft, "confirmedFields": CONFIRMED_FIELDS,
                    "conversationId": ready["conversationId"], "confirmed": True}
            denied = call(client, "unconfirmed publication", "POST", "/api/tasks", {**body, "confirmed": False}, status=409)
            check("human confirmation required", denied["error"]["code"] == "confirmation_required")
            headers = {"Idempotency-Key": "live-smoke-" + run_id}
            task = call(client, "publication", "POST", "/api/tasks", body, status=201, headers=headers)
            live("publicationClarity", task)
            check("published draft and provenance preserved", task["draft"] == draft and task["sources"] == ready["sources"])
            check("clarity and readiness valid", 0 <= task["clarity"] <= 10 and bool(task["clarityReason"].strip())
                  and task["readiness"] == 90)
            events = call(client, "publication events", "GET", "/api/events")
            check("one publication event", len(events["events"]) == 1
                  and events["events"][0]["type"] == "task_published")
            replay = call(client, "publication retry", "POST", "/api/tasks", body, status=201, headers=headers)
            check("publication retry identical", replay == task)
            check("publication retry emits no event", call(client, "retry events", "GET", "/api/events") == events)
            catalog = call(client, "published catalog", "GET", "/api/catalog")
            check("published task visible", any(card["id"] == task["id"] for card in catalog["cards"]))
            check("catalog sorted", catalog["cards"] == sorted(catalog["cards"], key=lambda c: (-c["readiness"], -c["clarity"], c["id"])))
            proposals = []
            for team_id in ("team-1", "team-2"):
                proposals.append(call(client, f"proposal {team_id}", "POST", f"/api/tasks/{task['id']}/proposals", {
                    "teamId": team_id, "idea": "ДЕМО: прототип проверки синтетических SAT",
                    "plan": "ДЕМО: загрузка, сравнение с эталоном, ручная правка оценки",
                    "timeline": "ДЕМО: первая демонстрация через неделю", "prototypeUrl": "https://example.invalid/synthetic-sat",
                    "publishDetails": False,
                }, status=201))
            public = call(client, "public proposals", "GET", f"/api/tasks/{task['id']}/proposals")
            business = call(client, "business proposals", "GET", f"/api/tasks/{task['id']}/proposals?scope=demo-business&businessId=business-demo")
            proposal_ids = {proposal["id"] for proposal in proposals}
            check("private details visible only in business view", len(public["proposals"]) == 2
                  and len(business["proposals"]) == 2
                  and {p["id"] for p in public["proposals"]} == proposal_ids
                  and {p["id"] for p in business["proposals"]} == proposal_ids
                  and all(p["details"] is None for p in public["proposals"])
                  and all(p["details"] is not None for p in business["proposals"]))
            for proposal in proposals:
                call(client, f"select {proposal['teamId']}", "PATCH", f"/api/proposals/{proposal['id']}/decision",
                     {"businessId": "business-demo", "decision": "selected"})
            selected = call(client, "selected proposals", "GET", f"/api/tasks/{task['id']}/proposals")
            check("both teams remain selected", len(selected["proposals"]) == 2
                  and {p["id"] for p in selected["proposals"]} == proposal_ids
                  and {p["teamId"] for p in selected["proposals"]} == {"team-1", "team-2"}
                  and all(p["decision"] == "selected" for p in selected["proposals"]))
            before_milestone = call(client, "catalog before milestone", "GET", "/api/catalog")
            check("no experience for proposals or selection", before_milestone["teams"] == initial["teams"])
            milestone = call(client, "submit milestone", "POST", f"/api/proposals/{proposals[0]['id']}/milestones", {
                "teamId": "team-1", "description": "ДЕМО: подготовлена загрузка синтетической работы и ручная правка оценки",
            }, status=201)
            confirm_path = f"/api/milestones/{milestone['id']}/confirm"
            confirmation = {"businessId": "business-demo"}
            confirmed = call(client, "confirm milestone", "POST", confirm_path, confirmation)
            repeated = call(client, "confirm milestone retry", "POST", confirm_path, confirmation)
            baseline = next(team for team in initial["teams"] if team["id"] == "team-1")
            check("exactly ten experience awarded once", confirmed == repeated and confirmed["experienceAwarded"] == 10
                  and confirmed["team"]["experience"] == baseline["experience"] + 10
                  and confirmed["team"]["completed"] == baseline["completed"] + 1)
            final_events = call(client, "final events", "GET", "/api/events")
            final_catalog = call(client, "final catalog", "GET", "/api/catalog")
            snapshot = call(client, "task snapshot", "GET", f"/api/tasks/{task['id']}")
            final_card = next(card for card in final_catalog["cards"] if card["id"] == task["id"])
            final_team = next(team for team in final_catalog["teams"] if team["id"] == "team-1")
            check("catalog and task show both proposing teams", len(final_card["teamIds"]) == 2
                  and set(final_card["teamIds"]) == {"team-1", "team-2"}
                  and snapshot["proposalCount"] == 2 and len(snapshot["teamIds"]) == 2
                  and set(snapshot["teamIds"]) == {"team-1", "team-2"})
            check("catalog shows confirmed experience and completed milestone",
                  final_team["experience"] == baseline["experience"] + 10
                  and final_team["completed"] == baseline["completed"] + 1)
            check("poll preserves catalog", call(client, "catalog poll", "GET", "/api/catalog") == final_catalog)
            check("poll creates no events", call(client, "poll events", "GET", "/api/events") == final_events)
        with TestClient(create_app(settings)) as restarted:
            check("task survives restart", call(restarted, "restarted task", "GET", f"/api/tasks/{task['id']}") == snapshot)
            check("catalog survives restart", call(restarted, "restarted catalog", "GET", "/api/catalog") == final_catalog)
            check("restart confirmation retry adds no experience", call(restarted, "restarted confirm retry", "POST", confirm_path, confirmation) == confirmed)
            check("events survive restart without duplication", call(restarted, "restarted events", "GET", "/api/events") == final_events)
        report["status"] = "passed"
    except Exception as exc:
        report.update(status="failed", errorType=type(exc).__name__)
    finally:
        report["totalSeconds"] = round(perf_counter() - started, 3)
        save()
        print(safe(json.dumps({"status": report["status"], "report": str(report_path),
                               "aiModeByStage": report["aiModeByStage"], "checks": len(report["checks"]),
                               "errorType": report.get("errorType")}, ensure_ascii=False, indent=2)), flush=True)
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    try:
        exit_code = main()
    except Exception as exc:
        print(json.dumps({"status": "failed", "errorType": type(exc).__name__}))
        exit_code = 1
    raise SystemExit(exit_code)
