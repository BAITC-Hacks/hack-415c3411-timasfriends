"""Short SQLite transactions. Never hold a write transaction during an AI call."""

from contextlib import contextmanager
from datetime import datetime, timezone
import json
from pathlib import Path
import sqlite3
from uuid import uuid4


SCHEMA = """
CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
INSERT OR IGNORE INTO metadata(key, value) VALUES ('catalog_version', 0);
CREATE TABLE IF NOT EXISTS businesses (id TEXT PRIMARY KEY, name TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS teams (
    id TEXT PRIMARY KEY, profile TEXT NOT NULL,
    completed INTEGER NOT NULL DEFAULT 0 CHECK(completed >= 0),
    experience INTEGER NOT NULL DEFAULT 0 CHECK(experience >= 0)
);
CREATE TABLE IF NOT EXISTS conversations (
    id TEXT PRIMARY KEY, history TEXT NOT NULL, draft TEXT NOT NULL,
    sources TEXT NOT NULL, revision INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS tasks (
    id TEXT PRIMARY KEY, business_id TEXT NOT NULL REFERENCES businesses(id), body TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS proposals (
    id TEXT PRIMARY KEY, task_id TEXT NOT NULL REFERENCES tasks(id),
    team_id TEXT NOT NULL REFERENCES teams(id),
    idea TEXT NOT NULL, plan TEXT NOT NULL, timeline TEXT NOT NULL, prototype_url TEXT NOT NULL,
    publish_details INTEGER NOT NULL CHECK(publish_details IN (0,1)),
    decision TEXT NOT NULL DEFAULT 'pending' CHECK(decision IN ('pending','selected','rejected')),
    created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
    UNIQUE(task_id, team_id)
);
CREATE TABLE IF NOT EXISTS milestones (
    id TEXT PRIMARY KEY, proposal_id TEXT NOT NULL REFERENCES proposals(id),
    description TEXT NOT NULL, confirmed INTEGER NOT NULL DEFAULT 0 CHECK(confirmed IN (0,1)),
    experience_awarded INTEGER NOT NULL DEFAULT 0 CHECK(experience_awarded IN (0,10)),
    created_at TEXT NOT NULL, confirmed_at TEXT
);
CREATE TABLE IF NOT EXISTS idempotency (
    business_id TEXT NOT NULL REFERENCES businesses(id), key TEXT NOT NULL,
    fingerprint TEXT NOT NULL, response TEXT NOT NULL,
    PRIMARY KEY(business_id, key)
);
CREATE TABLE IF NOT EXISTS events (
    id INTEGER PRIMARY KEY AUTOINCREMENT, type TEXT NOT NULL,
    task_id TEXT NOT NULL REFERENCES tasks(id), message TEXT NOT NULL, created_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS proposals_task ON proposals(task_id);
CREATE INDEX IF NOT EXISTS milestones_proposal ON milestones(proposal_id);
"""


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def new_id(prefix: str) -> str:
    return f"{prefix}-{uuid4().hex}"


def dumps(value) -> str:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), sort_keys=True)


class Database:
    def __init__(self, path: str):
        self.path = path

    def connect(self) -> sqlite3.Connection:
        conn = sqlite3.connect(self.path, timeout=10, isolation_level=None)
        conn.row_factory = sqlite3.Row
        conn.execute("PRAGMA foreign_keys = ON")
        conn.execute("PRAGMA busy_timeout = 10000")
        return conn

    def initialize(self) -> None:
        Path(self.path).parent.mkdir(parents=True, exist_ok=True)
        conn = self.connect()
        try:
            conn.execute("PRAGMA journal_mode = WAL")
            conn.executescript(SCHEMA)
        finally:
            conn.close()

    @contextmanager
    def read(self):
        conn = self.connect()
        try:
            conn.execute("BEGIN")
            yield conn
            if conn.in_transaction:
                conn.commit()
        except BaseException:
            if conn.in_transaction:
                conn.rollback()
            raise
        finally:
            conn.close()

    @contextmanager
    def write(self):
        conn = self.connect()
        try:
            conn.execute("BEGIN IMMEDIATE")
            yield conn
            conn.commit()
        except BaseException:
            conn.rollback()
            raise
        finally:
            conn.close()


def add_event(conn, event_type: str, task_id: str, message: str, created_at: str | None = None):
    conn.execute(
        "INSERT INTO events(type,task_id,message,created_at) VALUES (?,?,?,?)",
        (event_type, task_id, message, created_at or utc_now()),
    )
    conn.execute("UPDATE metadata SET value=value+1 WHERE key='catalog_version'")
