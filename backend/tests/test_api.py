"""Integration checks for the persisted, offline QuestBridge API contract."""

from concurrent.futures import ThreadPoolExecutor
from copy import deepcopy

import pytest
from fastapi.testclient import TestClient

from backend.app.ai import AIService
from backend.app.config import Settings
from backend.app.main import create_app


DRAFT_FIELDS = (
    "title", "category", "context", "need", "users", "data", "constraints",
    "expectedResult", "successCriteria", "contact", "interactionFormat",
    "feedbackProcess",
)
WEIGHTS = {
    "context": 10,
    "need": 10,
    "data": 20,
    "expectedResult": 15,
    "successCriteria": 15,
    "constraints": 10,
    "users": 10,
    "contact": 4,
    "interactionFormat": 3,
    "feedbackProcess": 3,
}


@pytest.fixture
def settings(tmp_path):
    return Settings(
        db_path=str(tmp_path / "questbridge.sqlite3"),
        seed_demo=True,
        ai_api_key="",
        ai_model="",
        cors_origins=("http://localhost:8080",),
    )


@pytest.fixture
def client(settings):
    with TestClient(create_app(settings=settings)) as test_client:
        yield test_client


def draft(**changes):
    result = dict.fromkeys(DRAFT_FIELDS, "")
    result.update(title="Учебный тренажёр дробей", category="Образование")
    result.update(changes)
    return result


def publication(task_draft=None, confirmed_fields=()):
    return {
        "businessId": "business-demo",
        "draft": task_draft if task_draft is not None else draft(),
        "confirmedFields": list(confirmed_fields),
        "confirmed": True,
    }


def publish(client, task_draft=None, confirmed_fields=(), headers=None):
    response = client.post(
        "/api/tasks", json=publication(task_draft, confirmed_fields), headers=headers
    )
    assert response.status_code in (200, 201), response.text
    return response.json()


def submit(client, task_id, team_id="team-1", publish_details=False):
    payload = {
        "teamId": team_id,
        "idea": f"Идея команды {team_id}: интерактивный тренажёр дробей",
        "plan": "Собрать экран упражнения и проверить его с преподавателем",
        "timeline": "Показать первую версию через неделю",
        "prototypeUrl": "https://example.org/questbridge-prototype",
        "publishDetails": publish_details,
    }
    response = client.post(f"/api/tasks/{task_id}/proposals", json=payload)
    assert response.status_code in (200, 201), response.text
    return response.json(), payload


def select(client, proposal_id, business_id="business-demo"):
    return client.patch(
        f"/api/proposals/{proposal_id}/decision",
        json={"businessId": business_id, "decision": "selected"},
    )


def milestone(client, proposal_id, team_id="team-1"):
    response = client.post(
        f"/api/proposals/{proposal_id}/milestones",
        json={"teamId": team_id, "description": "Готов первый экран упражнения"},
    )
    assert response.status_code in (200, 201), response.text
    return response.json()


def confirm(client, milestone_id, business_id="business-demo"):
    return client.post(
        f"/api/milestones/{milestone_id}/confirm",
        json={"businessId": business_id},
    )


def team_profile(client, team_id):
    return next(
        item for item in client.get("/api/catalog").json()["teams"]
        if item["id"] == team_id
    )


def assert_error(response, status=None, code=None):
    if status is not None:
        assert response.status_code == status, response.text
    assert 400 <= response.status_code < 600, response.text
    body = response.json()
    assert set(body) == {"error"}
    assert set(body["error"]) == {"code", "message"}
    assert isinstance(body["error"]["code"], str)
    assert body["error"]["message"]
    if code is not None:
        assert body["error"]["code"] == code
    assert "Traceback" not in response.text


def test_health_and_exact_catalog_contract(client):
    assert client.get("/health").status_code == 200
    response = client.get("/api/catalog")
    assert response.status_code == 200
    catalog = response.json()
    assert set(catalog) == {"version", "cards", "teams"}
    assert type(catalog["version"]) is int
    assert len(catalog["cards"]) >= 5
    assert len(catalog["teams"]) >= 5
    assert len({card["readiness"] for card in catalog["cards"]}) >= 3
    assert catalog["cards"] == sorted(
        catalog["cards"],
        key=lambda card: (-card["readiness"], -card["clarity"], card["id"]),
    )
    team_ids = {team["id"] for team in catalog["teams"]}
    proposals_count = 0
    for card in catalog["cards"]:
        assert set(card) == {
            "id", "title", "description", "category", "readiness", "clarity", "teamIds"
        }
        assert 0 <= card["readiness"] <= 100
        assert 0 <= card["clarity"] <= 10
        proposals = client.get(f"/api/tasks/{card['id']}/proposals").json()["proposals"]
        proposals_count += len(proposals)
        assert set(card["teamIds"]) == {item["teamId"] for item in proposals}
        assert len(card["teamIds"]) == len(set(card["teamIds"]))
        assert set(card["teamIds"]) <= team_ids
    assert proposals_count >= 5
    for team in catalog["teams"]:
        assert set(team) == {
            "id", "name", "initials", "stack", "description", "completed", "experience"
        }
        assert type(team["completed"]) is int
        assert team["completed"] >= 0
        assert type(team["experience"]) is int
        assert team["experience"] >= 0


def test_readiness_is_deterministic_and_counts_only_confirmed_content(client):
    complete = draft(**{field: f"Содержательное значение поля {field}" for field in WEIGHTS})
    payload = {"draft": complete, "confirmedFields": list(WEIGHTS)}
    first = client.post("/api/tasks/preview", json=payload)
    assert first.status_code == 200, first.text
    result = first.json()
    assert result == client.post("/api/tasks/preview", json=payload).json()
    assert result["readiness"] == 100
    assert result["readinessLevel"] == "priority"
    assert result["missingFields"] == []
    assert len(result["scoreBreakdown"]) == len(WEIGHTS)
    assert {row["field"]: row["maxPoints"] for row in result["scoreBreakdown"]} == WEIGHTS
    assert {row["field"]: row["points"] for row in result["scoreBreakdown"]} == WEIGHTS
    assert all(row["filled"] and row["confirmed"] for row in result["scoreBreakdown"])
    for field, weight in WEIGHTS.items():
        single = client.post(
            "/api/tasks/preview", json={"draft": complete, "confirmedFields": [field]}
        ).json()
        assert single["readiness"] == weight
    unconfirmed = client.post(
        "/api/tasks/preview", json={"draft": complete, "confirmedFields": []}
    ).json()
    assert unconfirmed["readiness"] == 0
    assert unconfirmed["readinessLevel"] == "draft"
    assert set(unconfirmed["missingFields"]) == set(WEIGHTS)


@pytest.mark.parametrize("placeholder", ["", "   ", "не знаю", "Не указано", "TBD", "-"])
def test_placeholders_never_earn_readiness_points(client, placeholder):
    response = client.post(
        "/api/tasks/preview",
        json={
            "draft": draft(**dict.fromkeys(WEIGHTS, placeholder)),
            "confirmedFields": list(WEIGHTS),
        },
    )
    assert response.status_code == 200, response.text
    assert response.json()["readiness"] == 0


@pytest.mark.parametrize(
    ("fields", "score", "level"),
    [
        (("data", "context"), 30, "draft"),
        (("data", "context", "need"), 40, "workable"),
        (("data", "context", "need", "expectedResult", "successCriteria"), 70, "ready"),
        (tuple(field for field in WEIGHTS if field != "users"), 90, "priority"),
    ],
)
def test_readiness_level_boundaries(client, fields, score, level):
    response = client.post(
        "/api/tasks/preview",
        json={
            "draft": draft(**{field: "Конкретное описание" for field in fields}),
            "confirmedFields": list(fields),
        },
    )
    assert response.json()["readiness"] == score
    assert response.json()["readinessLevel"] == level


def test_publication_requires_confirmation_and_recalculates_readiness(client):
    before = len(client.get("/api/catalog").json()["cards"])
    payload = publication()
    payload["confirmed"] = False
    assert_error(client.post("/api/tasks", json=payload))
    payload.pop("confirmed")
    assert_error(client.post("/api/tasks", json=payload), 422)
    assert len(client.get("/api/catalog").json()["cards"]) == before
    injected = publication()
    injected["readiness"] = 100
    response = client.post("/api/tasks", json=injected)
    if response.status_code in (200, 201):
        assert response.json()["readiness"] == 0
    else:
        assert_error(response, 422)
    task = publish(client)
    assert task["readiness"] == 0
    assert task["clarity"] == 0
    assert task["aiMode"] == "fallback"
    assert task["version"] == 1
    assert task["teamIds"] == []
    assert task["id"] in {card["id"] for card in client.get("/api/catalog").json()["cards"]}
    for required in ("title", "category"):
        invalid = publication(draft(**{required: "   "}))
        assert_error(client.post("/api/tasks", json=invalid))


def test_fallback_chat_has_three_questions_then_editable_unpublished_draft(client):
    before = client.get("/api/catalog").json()
    response = client.post(
        "/api/chat",
        json={"conversationId": "", "message": "Учителя долго проверяют пробные SAT"},
    )
    assert response.status_code == 200, response.text
    first = response.json()
    assert set(first) == {"conversationId", "message", "phase", "aiMode", "questions", "draft", "sources", "missingFields"}
    assert first["conversationId"]
    assert first["phase"] == "clarifying"
    assert first["aiMode"] == "fallback"
    assert len(first["questions"]) >= 3
    assert len({question["id"] for question in first["questions"]}) == len(first["questions"])
    assert all(question["field"] in DRAFT_FIELDS for question in first["questions"])
    assert first["draft"] is None
    next_response = client.post(
        "/api/chat",
        json={"conversationId": first["conversationId"], "message": "Не знаю ответы на эти вопросы"},
    )
    assert next_response.status_code == 200, next_response.text
    ready = next_response.json()
    assert ready["conversationId"] == first["conversationId"]
    assert ready["phase"] == "draft_ready"
    assert ready["aiMode"] == "fallback"
    assert ready["questions"] == []
    assert set(ready["draft"]) == set(DRAFT_FIELDS)
    for field in ("data", "constraints", "contact", "successCriteria"):
        assert ready["draft"][field] == ""
    assert client.get("/api/catalog").json() == before
    assert_error(
        client.post("/api/chat", json={"conversationId": "missing-session", "message": "Ответ"}),
        404,
    )


def test_low_readiness_accepts_proposals_and_multiple_manual_selections(client):
    task = publish(client)
    before = {team_id: team_profile(client, team_id)["completed"] for team_id in ("team-1", "team-2")}
    first, _ = submit(client, task["id"], "team-1")
    second, _ = submit(client, task["id"], "team-2")
    assert first["decision"] == second["decision"] == "pending"
    for proposal in (first, second):
        response = select(client, proposal["id"])
        assert response.status_code == 200, response.text
        assert response.json()["decision"] == "selected"
    proposals = client.get(f"/api/tasks/{task['id']}/proposals").json()["proposals"]
    assert sum(item["decision"] == "selected" for item in proposals) == 2
    task_detail = client.get(f"/api/tasks/{task['id']}").json()
    assert set(task_detail["teamIds"]) == {"team-1", "team-2"}
    assert task_detail["readiness"] == 0
    for team_id, completed in before.items():
        assert team_profile(client, team_id)["completed"] == completed


def test_private_proposal_details_are_only_in_the_demo_business_scope(client):
    task = publish(client)
    private, private_payload = submit(client, task["id"])
    public, public_payload = submit(client, task["id"], "team-2", publish_details=True)
    response = client.get(f"/api/tasks/{task['id']}/proposals")
    rows = {item["id"]: item for item in response.json()["proposals"]}
    assert rows[private["id"]]["details"] is None
    assert private_payload["idea"] not in response.text
    assert rows[public["id"]]["details"]["idea"] == public_payload["idea"]
    public_with_business_id = client.get(
        f"/api/tasks/{task['id']}/proposals", params={"businessId": "business-demo"}
    )
    assert private_payload["idea"] not in public_with_business_id.text
    demo = client.get(
        f"/api/tasks/{task['id']}/proposals",
        params={"scope": "demo-business", "businessId": "business-demo"},
    )
    assert demo.status_code == 200, demo.text
    rows = {item["id"]: item for item in demo.json()["proposals"]}
    assert rows[private["id"]]["details"]["idea"] == private_payload["idea"]
    assert_error(client.get(
        f"/api/tasks/{task['id']}/proposals",
        params={"scope": "demo-business", "businessId": "business-school"},
    ), 403)


def test_ownership_checks_and_milestone_selection_requirement(client):
    task = publish(client)
    proposal, _ = submit(client, task["id"])
    patch = publication()
    patch.update(businessId="business-school", version=task["version"])
    assert_error(client.patch(f"/api/tasks/{task['id']}", json=patch), 403)
    assert_error(select(client, proposal["id"], "business-school"), 403)
    assert_error(client.post(
        f"/api/proposals/{proposal['id']}/milestones",
        json={"teamId": "team-1", "description": "Первый рабочий экран"},
    ), 409)
    assert select(client, proposal["id"]).status_code == 200
    assert_error(client.post(
        f"/api/proposals/{proposal['id']}/milestones",
        json={"teamId": "team-2", "description": "Чужой этап"},
    ), 403)
    step = milestone(client, proposal["id"])
    assert_error(confirm(client, step["id"], "business-school"), 403)


def test_task_edits_require_fresh_field_confirmation_and_current_version(client):
    original = draft(context="Преподаватели проверяют задания вручную", need="Сократить время проверки")
    task = publish(client, original, ("context", "need"))
    changed = deepcopy(original)
    changed["context"] = "Преподаватели проверяют домашние задания в таблицах"
    payload = publication(changed, ("context", "need"))
    payload["version"] = task["version"]
    assert_error(
        client.patch(f"/api/tasks/{task['id']}", json=payload),
        409, "reconfirmation_required",
    )
    unchanged = client.get(f"/api/tasks/{task['id']}").json()
    assert unchanged["draft"] == original
    assert unchanged["version"] == task["version"]
    payload["reconfirmedFields"] = ["context"]
    response = client.patch(f"/api/tasks/{task['id']}", json=payload)
    assert response.status_code == 200, response.text
    revised = response.json()
    assert revised["version"] == task["version"] + 1
    assert revised["draft"] == changed
    assert revised["readiness"] == 20
    assert_error(client.patch(f"/api/tasks/{task['id']}", json=payload), 409)


def test_idempotency_returns_original_response_and_rejects_different_payload(client):
    headers = {"Idempotency-Key": "publish-training-card"}
    payload = publication()
    first = client.post("/api/tasks", json=payload, headers=headers)
    repeated = client.post("/api/tasks", json=payload, headers=headers)
    assert first.status_code in (200, 201), first.text
    assert repeated.status_code in (200, 201), repeated.text
    assert repeated.json() == first.json()
    update = publication(draft(title="Карточка изменена после первой публикации"))
    update["version"] = first.json()["version"]
    assert client.patch(f"/api/tasks/{first.json()['id']}", json=update).status_code == 200
    original_replay = client.post("/api/tasks", json=payload, headers=headers)
    assert original_replay.json() == first.json()
    payload["draft"]["title"] = "Другое содержание при прежнем ключе"
    assert_error(client.post("/api/tasks", json=payload, headers=headers), 409)
    cards = client.get("/api/catalog").json()["cards"]
    assert sum(card["id"] == first.json()["id"] for card in cards) == 1
    events = client.get("/api/events", params={"after": 0, "limit": 200}).json()["events"]
    assert sum(event["taskId"] == first.json()["id"] and event["type"] == "task_published" for event in events) == 1


def test_concurrent_publication_with_one_idempotency_key_creates_one_task(client):
    def create_once(_):
        return client.post(
            "/api/tasks", json=publication(), headers={"Idempotency-Key": "concurrent-publication"}
        )

    with ThreadPoolExecutor(max_workers=4) as pool:
        responses = list(pool.map(create_once, range(4)))
    assert all(response.status_code in (200, 201) for response in responses), [r.text for r in responses]
    assert len({response.json()["id"] for response in responses}) == 1
    assert all(response.json() == responses[0].json() for response in responses)


def test_milestone_confirmation_awards_once_even_with_concurrent_requests(client):
    task = publish(client)
    proposal, _ = submit(client, task["id"])
    assert select(client, proposal["id"]).status_code == 200
    before = team_profile(client, "team-1")
    step = milestone(client, proposal["id"])
    with ThreadPoolExecutor(max_workers=4) as pool:
        responses = list(pool.map(lambda _: confirm(client, step["id"]), range(4)))
    assert all(response.status_code == 200 for response in responses), [r.text for r in responses]
    assert all(response.json()["experienceAwarded"] == 10 for response in responses)
    assert all(response.json()["confirmed"] for response in responses)
    assert team_profile(client, "team-1")["completed"] == before["completed"] + 1
    assert team_profile(client, "team-1")["experience"] == before["experience"] + 10
    assert confirm(client, step["id"]).status_code == 200
    assert team_profile(client, "team-1")["completed"] == before["completed"] + 1
    assert team_profile(client, "team-1")["experience"] == before["experience"] + 10
    events = client.get("/api/events", params={"after": 0, "limit": 200}).json()["events"]
    assert sum(event["taskId"] == task["id"] and event["type"] == "milestone_confirmed" for event in events) == 1


def test_sqlite_restart_preserves_chat_tasks_proposals_milestones_and_experience(settings):
    with TestClient(create_app(settings=settings)) as first_client:
        initial = first_client.post(
            "/api/chat", json={"conversationId": "", "message": "Нужны учебные упражнения по дробям"}
        ).json()
        task = publish(first_client, draft(context="Учащиеся путают дроби"), ("context",))
        proposal, payload = submit(first_client, task["id"])
        assert select(first_client, proposal["id"]).status_code == 200
        step = milestone(first_client, proposal["id"])
        confirmed = confirm(first_client, step["id"])
        assert confirmed.status_code == 200
        persisted_team = team_profile(first_client, "team-1")
        before = first_client.get("/api/catalog").json()
    with TestClient(create_app(settings=settings)) as restarted:
        assert restarted.get("/api/catalog").json() == before
        assert restarted.get(f"/api/tasks/{task['id']}").json()["draft"] == task["draft"]
        rows = restarted.get(
            f"/api/tasks/{task['id']}/proposals",
            params={"scope": "demo-business", "businessId": "business-demo"},
        ).json()["proposals"]
        assert len(rows) == 1
        assert rows[0]["id"] == proposal["id"]
        assert rows[0]["decision"] == "selected"
        assert rows[0]["details"]["idea"] == payload["idea"]
        assert confirm(restarted, step["id"]).status_code == 200
        assert team_profile(restarted, "team-1") == persisted_team
        reply = restarted.post(
            "/api/chat", json={"conversationId": initial["conversationId"], "message": "Не знаю"}
        )
        assert reply.status_code == 200, reply.text
        assert reply.json()["conversationId"] == initial["conversationId"]
        assert reply.json()["phase"] == "draft_ready"


def test_catalog_polling_and_other_reads_never_call_ai(settings):
    class CountingAI(AIService):
        def __init__(self):
            super().__init__(api_key="", model="")
            self.chat_calls = 0
            self.clarity_calls = 0

        async def chat(self, conversation_id, history, draft, sources):
            self.chat_calls += 1
            return await super().chat(conversation_id, history, draft, sources)

        async def clarity(self, draft):
            self.clarity_calls += 1
            return await super().clarity(draft)

    ai = CountingAI()
    with TestClient(create_app(settings=settings, ai_service=ai)) as polling_client:
        task = publish(polling_client)
        polling_client.post("/api/chat", json={"conversationId": "", "message": "Нужен тренажёр дробей"})
        calls = (ai.chat_calls, ai.clarity_calls)
        assert calls[0] >= 1
        assert calls[1] >= 1
        snapshot = polling_client.get("/api/catalog").json()
        for _ in range(5):
            assert polling_client.get("/api/catalog").json() == snapshot
            assert polling_client.get(f"/api/tasks/{task['id']}").status_code == 200
            assert polling_client.get(f"/api/tasks/{task['id']}/proposals").status_code == 200
            assert polling_client.get("/api/events").status_code == 200
            assert polling_client.post(
                "/api/tasks/preview", json={"draft": task["draft"], "confirmedFields": []}
            ).status_code == 200
        assert (ai.chat_calls, ai.clarity_calls) == calls
        update = publication(task["draft"])
        update["version"] = task["version"]
        response = polling_client.patch(f"/api/tasks/{task['id']}", json=update)
        assert response.status_code == 200, response.text
        assert ai.clarity_calls == calls[1]


def test_events_are_monotonic_paginated_and_reads_do_not_make_activity(client):
    task = publish(client)
    submit(client, task["id"])
    all_events = client.get("/api/events", params={"after": 0, "limit": 200}).json()
    assert all_events["events"]
    expected = all_events["events"]
    collected = []
    cursor = 0
    while True:
        response = client.get("/api/events", params={"after": cursor, "limit": 2})
        assert response.status_code == 200, response.text
        batch = response.json()
        if not batch["events"]:
            assert batch["cursor"] == cursor
            break
        assert len(batch["events"]) <= 2
        assert all(event["id"] > cursor for event in batch["events"])
        assert batch["cursor"] == batch["events"][-1]["id"]
        collected.extend(batch["events"])
        cursor = batch["cursor"]
    assert collected == expected
    ids = [event["id"] for event in collected]
    assert ids == sorted(set(ids))
    for event in collected:
        assert set(event) == {"id", "type", "taskId", "message", "createdAt"}
        assert event["type"] in {
            "task_published", "task_updated", "proposal_submitted", "team_selected", "milestone_confirmed"
        }
        assert event["createdAt"].endswith(("Z", "+00:00"))
    before = client.get("/api/catalog").json()
    for _ in range(3):
        assert client.get("/api/catalog").json() == before
        assert client.get("/api/events", params={"after": cursor}).json() == {"cursor": cursor, "events": []}


def test_cors_preflight_allows_configured_unity_origin(client):
    response = client.options(
        "/api/chat",
        headers={
            "Origin": "http://localhost:8080",
            "Access-Control-Request-Method": "POST",
            "Access-Control-Request-Headers": "content-type,idempotency-key",
        },
    )
    assert response.status_code == 200, response.text
    assert response.headers["access-control-allow-origin"] == "http://localhost:8080"
    rejected = client.options(
        "/api/chat",
        headers={"Origin": "https://unconfigured.example", "Access-Control-Request-Method": "POST"},
    )
    assert "access-control-allow-origin" not in rejected.headers


@pytest.mark.parametrize(
    ("method", "path", "payload"),
    [
        ("POST", "/api/chat", {}),
        ("POST", "/api/chat", {"message": " "}),
        ("POST", "/api/chat", {"message": "x" * 7000}),
        ("POST", "/api/tasks/preview", {"draft": {}, "confirmedFields": ["readiness"]}),
        ("POST", "/api/tasks/task-1/proposals", {
            "teamId": "team-1", "idea": "Идея", "plan": "План", "timeline": "Неделя",
            "prototypeUrl": "javascript:alert(1)", "publishDetails": False,
        }),
        ("GET", "/api/events?after=-1", None),
        ("GET", "/api/events?limit=201", None),
        ("GET", "/api/events?limit=0", None),
    ],
)
def test_invalid_inputs_use_the_error_envelope(client, method, path, payload):
    response = client.request(method, path, json=payload) if payload is not None else client.request(method, path)
    assert_error(response, 422)


def test_malformed_json_oversized_body_and_non_boolean_confirmation(client):
    assert_error(client.post(
        "/api/chat", content=b'{"message": ', headers={"Content-Type": "application/json"}
    ), 422)
    assert_error(client.post("/api/chat", json={"message": "x" * 70000}), 413)
    payload = publication()
    payload["confirmed"] = "true"
    assert_error(client.post("/api/tasks", json=payload), 422)


def test_unknown_resources_return_404_envelope(client):
    assert_error(client.get("/api/tasks/does-not-exist"), 404)
    assert_error(client.get("/api/tasks/does-not-exist/proposals"), 404)
    assert_error(select(client, "does-not-exist"), 404)
    assert_error(confirm(client, "does-not-exist"), 404)
