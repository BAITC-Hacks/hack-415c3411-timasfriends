"""Configuration stays on the server; loading a module never prints secrets."""

from dataclasses import dataclass
import os
from pathlib import Path


BACKEND_DIR = Path(__file__).resolve().parents[1]


@dataclass(frozen=True)
class Settings:
    db_path: str = str(BACKEND_DIR / "data" / "questbridge.sqlite3")
    seed_demo: bool = True
    cors_origins: tuple[str, ...] = ("http://localhost:8080", "http://127.0.0.1:8080")
    ai_api_key: str = ""
    ai_model: str = ""
    ai_timeout: float = 10.0
    max_body_bytes: int = 65536
    ai_reasoning_effort: str = ""

    @classmethod
    def from_env(cls) -> "Settings":
        return cls(
            db_path=os.getenv("QUESTBRIDGE_DB", cls.db_path),
            seed_demo=os.getenv("SEED_DEMO", "true").lower() in {"1", "true", "yes"},
            cors_origins=tuple(
                origin.strip().rstrip("/")
                for origin in os.getenv(
                    "CORS_ORIGINS", "http://localhost:8080,http://127.0.0.1:8080"
                ).split(",")
                if origin.strip()
            ),
            ai_api_key=os.getenv("OPENAI_API_KEY", "").strip(),
            ai_model=os.getenv("AI_MODEL", "").strip(),
            ai_timeout=max(0.1, min(float(os.getenv("AI_TIMEOUT_SECONDS", "10")), 10.0)),
            ai_reasoning_effort=os.getenv("AI_REASONING_EFFORT", "").strip(),
        )
