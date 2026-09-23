"""Run an explicitly synthetic end-to-end workflow against a local API."""

import argparse
import json
from pathlib import Path
import sys
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
from uuid import uuid4


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://127.0.0.1:8000")
    args = parser.parse_args()

    def call(method, path, body=None, headers=None):
        request_headers = {"Content-Type": "application/json; charset=utf-8", **(headers or {})}
        request = Request(args.base_url.rstrip("/") + path,
                          data=None if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8"),
                          headers=request_headers, method=method)
        with urlopen(request, timeout=25) as response:
            return json.load(response)

    health = call("GET", "/health")
    catalog_before = call("GET", "/api/catalog")
    chat = call("POST", "/api/chat", {"conversationId": "", "message": "Учителя долго проверяют пробные SAT"})
    ready = chat
    for _ in range(3):
        ready = call("POST", "/api/chat", {"conversationId": chat["conversationId"], "message": "Не знаю"})
    sample = json.loads(Path(__file__).with_name("demo_drafts.json").read_text(encoding="utf-8"))["drafts"][4]
    sample["title"] = "ДЕМО: проверка полного сценария API"
    body = {"businessId": "business-demo", "draft": sample, "confirmedFields": ["context"], "confirmed": True}
    preview = call("POST", "/api/tasks/preview", {"draft": sample, "confirmedFields": ["context"]})
    headers = {"Idempotency-Key": "demo-" + uuid4().hex}
    task = call("POST", "/api/tasks", body, headers)
    replay = call("POST", "/api/tasks", body, headers)
    assert replay == task, "Publication retry must return the original task"
    proposal = call("POST", f"/api/tasks/{task['id']}/proposals", {
        "teamId": "team-1", "idea": "ДЕМО: браузерный дневник занятий",
        "plan": "ДЕМО: собрать экран заметок и показать преподавателю",
        "timeline": "ДЕМО: одна неделя", "prototypeUrl": "https://example.invalid/demo-prototype", "publishDetails": False,
    })
    public = call("GET", f"/api/tasks/{task['id']}/proposals")
    assert public["proposals"][0]["details"] is None
    call("PATCH", f"/api/proposals/{proposal['id']}/decision", {"businessId": "business-demo", "decision": "selected"})
    step = call("POST", f"/api/proposals/{proposal['id']}/milestones", {
        "teamId": "team-1", "description": "ДЕМО: собран экран создания заметки",
    })
    confirmed = call("POST", f"/api/milestones/{step['id']}/confirm", {"businessId": "business-demo"})
    repeated = call("POST", f"/api/milestones/{step['id']}/confirm", {"businessId": "business-demo"})
    assert repeated == confirmed, "A confirmation retry must not award experience again"
    before_team = next(team for team in catalog_before["teams"] if team["id"] == "team-1")
    assert confirmed["team"]["experience"] == before_team["experience"] + 10
    assert confirmed["team"]["completed"] == before_team["completed"] + 1
    print(json.dumps({
        "demo": True, "health": health, "chatPhase": ready["phase"], "aiMode": ready["aiMode"],
        "taskId": task["id"], "readiness": preview["readiness"], "proposalId": proposal["id"],
        "milestoneId": step["id"], "experienceAwarded": confirmed["experienceAwarded"],
        "idempotencyVerified": replay == task, "confirmationRetryVerified": repeated == confirmed,
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    try:
        main()
    except HTTPError as error:
        print(f"API returned HTTP {error.code}: {error.read().decode('utf-8', errors='replace')}", file=sys.stderr)
        sys.exit(1)
    except URLError:
        print("API недоступен. Сначала запустите сервер по backend/README.md.", file=sys.stderr)
        sys.exit(1)
