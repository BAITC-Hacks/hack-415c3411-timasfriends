"""Idempotent, explicitly synthetic demo records; never calls a live provider."""

import json

from .config import BACKEND_DIR
from .db import Database, add_event, dumps, utc_now
from .models import Draft, FieldSource, TaskResponse, Team
from .scoring import WEIGHTS, is_filled, score_draft


PROFILES = [
    ("team-1", "TimasFriends (демо)", "TF", "Unity · Python · AI", "Интерактивные образовательные продукты"),
    ("team-2", "Fraction Lab (демо)", "FL", "Unity · C#", "Учебные игры и тренажёры"),
    ("team-3", "Study Map (демо)", "SM", "Python · FastAPI", "Каталоги учебных материалов"),
    ("team-4", "Science Cards (демо)", "SC", "Web · Design", "Обучение через визуальные карточки"),
    ("team-5", "Lab Notes (демо)", "LN", "Python · SQLite", "Прототипы для лабораторных занятий"),
]


def seed(db: Database, include_tasks: bool = True):
    with db.write() as conn:
        for business_id, name in [("business-demo", "Демонстрационный учебный центр"), ("business-school", "Демонстрационная школа")]:
            conn.execute("INSERT OR IGNORE INTO businesses(id,name) VALUES (?,?)", (business_id, name))
        for team_id, name, initials, stack, description in PROFILES:
            profile = Team(id=team_id, name=name, initials=initials, stack=stack, description=f"ДЕМО. {description}")
            conn.execute("INSERT OR IGNORE INTO teams(id,profile) VALUES (?,?)", (team_id, dumps(profile.model_dump(exclude={"completed", "experience"}))))
        if not include_tasks or conn.execute("SELECT value FROM metadata WHERE key='demo_seed_v1'").fetchone():
            return
        samples = json.loads((BACKEND_DIR / "examples" / "demo_drafts.json").read_text(encoding="utf-8"))
        for index, sample in enumerate(samples["drafts"], start=1):
            draft = Draft.model_validate(sample)
            task_id, team_id, conv_id = f"task-{index}", f"team-{index}", f"conv-demo-{index}"
            timestamp, message_id = utc_now(), f"msg-demo-{index}"
            sources = [FieldSource(field=field, messageId=message_id, quote=value) for field, value in draft.model_dump().items() if value]
            history = [{"id": message_id, "role": "user", "content": "ДЕМОНСТРАЦИОННЫЙ ЧЕРНОВИК\n" + "\n".join(f"{field}: {value}" for field, value in draft.model_dump().items() if value)}]
            conn.execute("INSERT INTO conversations(id,history,draft,sources) VALUES (?,?,?,?)",
                         (conv_id, dumps(history), draft.model_dump_json(), dumps([s.model_dump() for s in sources])))
            confirmed = [field for field in WEIGHTS if is_filled(getattr(draft, field))]
            task = TaskResponse(
                id=task_id, businessId="business-demo", draft=draft, confirmed=True, confirmedFields=confirmed,
                **score_draft(draft, confirmed).model_dump(), clarity=0.0,
                clarityReason="Демонстрационная карточка: AI не вызывался, clarity=0 (fallback).",
                aiMode="fallback", version=1, createdAt=timestamp, updatedAt=timestamp,
                sources=sources, teamIds=[], proposalCount=0, demo=True,
            )
            conn.execute("INSERT INTO tasks(id,business_id,body) VALUES (?,?,?)", (task_id, "business-demo", task.model_dump_json()))
            add_event(conn, "task_published", task_id, "ДЕМО: опубликована синтетическая задача", timestamp)
            conn.execute(
                "INSERT INTO proposals(id,task_id,team_id,idea,plan,timeline,prototype_url,publish_details,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?)",
                (f"proposal-{index}", task_id, team_id, f"ДЕМО: прототип для задачи «{draft.title}».",
                 "ДЕМО: обсудить исходные данные, собрать прототип, показать результат наставнику.",
                 "ДЕМО: первая демонстрация через неделю после согласования.", f"https://example.invalid/prototypes/{index}",
                 int(index % 2 == 0), timestamp, timestamp),
            )
            add_event(conn, "proposal_submitted", task_id, "ДЕМО: команда подала синтетическое предложение", timestamp)
        conn.execute("INSERT INTO metadata(key,value) VALUES ('demo_seed_v1',1)")
