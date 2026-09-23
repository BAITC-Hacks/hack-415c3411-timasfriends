"""Bounded AI calls and an extractive, deterministic offline assistant.

Only a human-facing draft is produced here. This module has no database writes,
publication tools, or ability to select a team.
"""

from __future__ import annotations

import asyncio
import json
import re
from pathlib import Path
from typing import Literal, Protocol

import httpx
from pydantic import BaseModel, ConfigDict, Field, ValidationError, model_validator

from .models import ChatResponse, Draft, FieldSource, Question
from .scoring import is_filled


PROMPTS = Path(__file__).resolve().parent.parent / "prompts"
FIELD_NAMES = tuple(Draft.model_fields)
QUESTION_TEXT = {
    "users": "Кто будет пользоваться решением?",
    "data": "Какие данные или примеры уже есть?",
    "successCriteria": "Как вы поймёте, что задача решена?",
    "need": "Что именно нужно изменить или улучшить?",
    "expectedResult": "Какой конкретный результат должна передать команда?",
    "constraints": "Какие сроки и ограничения нужно учесть?",
    "contact": "Как команда сможет связаться с представителем бизнеса?",
    "interactionFormat": "В каком формате вы готовы общаться с командой?",
    "feedbackProcess": "Кто и как будет проверять промежуточный результат?",
    "context": "Как сейчас решают эту задачу и где возникает затруднение?",
    "title": "Как кратко назвать задачу?",
    "category": "К какой категории относится задача?",
}
FIELD_LABELS = {
    "title": "название", "category": "категория", "context": "контекст",
    "need": "потребность", "users": "пользователи", "data": "данные",
    "constraints": "ограничения", "expectedResult": "ожидаемый результат",
    "successCriteria": "критерии успеха", "contact": "контакт",
    "interactionFormat": "формат взаимодействия", "feedbackProcess": "обратная связь",
}
ALIASES = {
    **{name.casefold(): name for name in FIELD_NAMES},
    **{label: name for name, label in FIELD_LABELS.items()},
    "заголовок": "title", "проблема": "context", "цель": "need",
    "аудитория": "users", "результат": "expectedResult",
    "критерий успеха": "successCriteria", "критерии": "successCriteria",
    "контакты": "contact", "формат связи": "interactionFormat",
    "процесс обратной связи": "feedbackProcess",
}
LABEL_RE = re.compile(r"(?:^|[\n;])\s*([A-Za-zА-Яа-яЁё ]{1,80}):\s*([^\n;]*)")
INSTRUCTION_RE = re.compile(
    r"ignore\s+(?:all\s+)?(?:previous|system)|system\s*prompt|"
    r"игнорир\w*\s+(?:все\s+)?(?:инструкц|правил)|"
    r"системн\w*\s+(?:промпт|инструкц)|<\|(?:system|developer)\|>",
    re.IGNORECASE,
)


class ClarityResult(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)

    clarity: float = Field(ge=0, le=10, allow_inf_nan=False)
    clarityReason: str = Field(min_length=1, max_length=1000)
    aiMode: Literal["live", "fallback"]


class _ChatOutput(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)

    message: str = Field(min_length=1, max_length=6000)
    phase: Literal["clarifying", "draft_ready"]
    questions: list[Question] = Field(max_length=12)
    draft: Draft | None
    sources: list[FieldSource] = Field(max_length=24)

    @model_validator(mode="before")
    @classmethod
    def require_all_draft_fields(cls, value):
        if isinstance(value, dict) and isinstance(value.get("draft"), dict):
            if set(value["draft"]) != set(FIELD_NAMES):
                raise ValueError("All draft fields must be present in AI output")
        return value


class _ClarityOutput(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)

    clarity: float = Field(ge=0, le=10, allow_inf_nan=False)
    clarityReason: str = Field(min_length=1, max_length=1000)


class AIProvider(Protocol):
    async def generate(self, task: str, payload: dict, repair: bool = False) -> str: ...


class OpenAIProvider:
    """REST Responses API; retries are owned by AIService, never nested."""

    def __init__(self, api_key: str, model: str, timeout: float,
                 transport: httpx.AsyncBaseTransport | None = None):
        self._api_key = api_key
        self._model = model
        self._timeout = timeout
        self._transport = transport

    async def generate(self, task: str, payload: dict, repair: bool = False) -> str:
        instructions = (PROMPTS / f"{task}_system.txt").read_text(encoding="utf-8")
        schema = json.loads((PROMPTS / f"{task}_schema.json").read_text(encoding="utf-8"))
        if repair:
            instructions += (
                "\nПредыдущий ответ не прошёл проверку. Создай ответ заново строго по JSON schema. "
                "Проверь типы, диапазоны, цитаты USER, пустые неизвестные поля и правила фазы. "
                "Это единственная попытка исправления."
            )
        async with httpx.AsyncClient(timeout=self._timeout, transport=self._transport) as client:
            response = await client.post(
                "https://api.openai.com/v1/responses",
                headers={"Authorization": f"Bearer {self._api_key}"},
                json={
                    "model": self._model,
                    "instructions": instructions,
                    "input": [{"role": "user", "content": json.dumps(payload, ensure_ascii=False)}],
                    "text": {"format": {"type": "json_schema", "name": f"questbridge_{task}",
                                         "schema": schema, "strict": True}},
                    "store": False,
                    "max_output_tokens": 4000,
                },
            )
            response.raise_for_status()
            body = response.json()
        if not isinstance(body, dict) or body.get("status") in {"failed", "incomplete", "cancelled"}:
            raise ValueError("Incomplete provider response")
        chunks = []
        for item in body.get("output", []):
            if not isinstance(item, dict):
                raise ValueError("Invalid provider output")
            for part in item.get("content", []):
                if not isinstance(part, dict):
                    raise ValueError("Invalid provider content")
                if part.get("type") == "refusal":
                    raise ValueError("Provider refusal")
                if part.get("type") == "output_text" and isinstance(part.get("text"), str):
                    chunks.append(part["text"])
        text = "".join(chunks).strip()
        if not text or len(text) > 60000:
            raise ValueError("Empty or oversized provider answer")
        return text


def _bounded(field: str, value: str) -> str:
    # Match the public model's own limit without duplicating its validation rules.
    max_length = next((m.max_length for m in Draft.model_fields[field].metadata
                       if getattr(m, "max_length", None) is not None), 3000)
    return value.strip()[:max_length].rstrip()


def _user_messages(history: list[dict]) -> list[dict]:
    return [message for message in history if message.get("role") == "user"]


def _source_valid(source: FieldSource, users: dict[str, str], value: str) -> bool:
    return bool(source.quote.strip() and source.messageId in users
                and source.quote in users[source.messageId]
                and value in source.quote
                and not INSTRUCTION_RE.search(source.quote))


def _extract(history: list[dict], draft: Draft, sources: list[FieldSource]) -> tuple[Draft, list[FieldSource]]:
    """Only copy literal user text; an unknown reply never erases known facts."""
    values = draft.model_dump()
    user_messages = _user_messages(history)
    users = {message["id"]: message["content"] for message in user_messages}
    positions = {message["id"]: index for index, message in enumerate(user_messages)}
    provenance = {source.field: source for source in sources
                  if source.field in values and is_filled(values[source.field])
                  and _source_valid(source, users, values[source.field])}

    def assign(field: str, value: str, message: dict) -> None:
        value = _bounded(field, value)
        previous = provenance.get(field)
        if previous and positions[previous.messageId] > positions[message["id"]]:
            return
        if is_filled(value) and not INSTRUCTION_RE.search(value):
            values[field] = value
            provenance[field] = FieldSource(field=field, messageId=message["id"], quote=value)

    for index, message in enumerate(user_messages):
        content = message["content"]
        labelled = list(LABEL_RE.finditer(content))
        if index == 0 and not INSTRUCTION_RE.search(content):
            # For prose, keep the exact first paragraph as context and provisional title.
            # Labelled forms already identify which statement belongs to which field.
            first_line = next((line.strip() for line in content.splitlines() if line.strip()), "")
            first_label = LABEL_RE.match(first_line)
            if not first_label or first_label.group(1).strip().casefold() not in ALIASES:
                if not is_filled(values["context"]):
                    assign("context", first_line, message)
                if not is_filled(values["title"]):
                    assign("title", first_line[:120], message)
        for match in labelled:
            field = ALIASES.get(match.group(1).strip().casefold())
            if field and not INSTRUCTION_RE.search(content):
                assign(field, match.group(2), message)
    # Recover provenance for unchanged known values if it can be proven from history.
    for field, value in values.items():
        if is_filled(value) and field not in provenance:
            for message in reversed(user_messages):
                if value in message["content"] and not INSTRUCTION_RE.search(value):
                    provenance[field] = FieldSource(field=field, messageId=message["id"], quote=value)
                    break
    return Draft.model_validate(values), list(provenance.values())


class FallbackProvider:
    def chat(self, conversation_id: str, history: list[dict], draft: Draft,
             sources: list[FieldSource]) -> ChatResponse:
        draft, sources = _extract(history, draft, sources)
        missing = [field for field in FIELD_NAMES if not is_filled(getattr(draft, field))]
        first_turn = len(_user_messages(history)) <= 1
        if first_turn and missing:
            fields = [field for field in QUESTION_TEXT if field in missing][:3]
            questions = [Question(id=f"q-{field}", field=field, text=QUESTION_TEXT[field]) for field in fields]
            context = draft.context.strip()
            intro = (f"Уточним задачу «{context}»." if context and len(context) <= 72
                     else "Уточним несколько деталей задачи.")
            numbered = "\n".join(f"{index}. {question.text}"
                                 for index, question in enumerate(questions, 1))
            answer_format = "\n".join(f"{FIELD_LABELS[field].capitalize()}: …" for field in fields)
            message = f"{intro}\n\n{numbered}\n\nОтветьте в таком формате:\n{answer_format}"
            return ChatResponse(conversationId=conversation_id, message=message,
                                phase="clarifying", aiMode="fallback", questions=questions,
                                draft=None, sources=sources, missingFields=missing)
        message = "Описание сохранено в черновике. Задача ещё не опубликована."
        if missing:
            next_field = next(field for field in QUESTION_TEXT if field in missing)
            message += (f"\n\n{QUESTION_TEXT[next_field]}"
                        f"\nМожно ответить: «{FIELD_LABELS[next_field].capitalize()}: …».")
        else:
            message += " Проверьте данные перед публикацией."
        return ChatResponse(conversationId=conversation_id, message=message,
                            phase="draft_ready", aiMode="fallback", questions=[],
                            draft=draft, sources=sources, missingFields=missing)

    def clarity(self) -> ClarityResult:
        return ClarityResult(clarity=0, clarityReason="Оценка AI сейчас недоступна. 0 не является оценкой ясности задачи.",
                             aiMode="fallback")


class AIService:
    def __init__(self, api_key: str = "", model: str = "", timeout: float = 4.0,
                 transport: httpx.AsyncBaseTransport | None = None):
        self._timeout = max(0.1, min(float(timeout), 4.0))
        self._provider: AIProvider | None = (
            OpenAIProvider(api_key, model, self._timeout, transport) if api_key and model else None
        )
        self._fallback = FallbackProvider()

    async def chat(self, conversation_id: str, history: list[dict], draft: Draft,
                   sources: list[FieldSource]) -> ChatResponse:
        base, base_sources = _extract(history, draft, sources)
        if self._provider is not None:
            payload = {"history": history, "knownDraft": base.model_dump(),
                       "knownSources": [source.model_dump() for source in base_sources],
                       "firstUserTurn": len(_user_messages(history)) <= 1}
            for attempt in range(2):
                try:
                    text = await asyncio.wait_for(self._provider.generate("chat", payload, repair=attempt > 0),
                                                  timeout=self._timeout)
                    candidate = _ChatOutput.model_validate_json(text, strict=True)
                    return self._validate_chat(candidate, conversation_id, history, base, base_sources)
                except httpx.HTTPStatusError as exc:
                    if exc.response.status_code < 500 and exc.response.status_code != 429:
                        break
                except (httpx.RequestError, TimeoutError, ValidationError, ValueError, TypeError, KeyError):
                    # No provider errors, request bodies, secrets, or generated content enter logs.
                    pass
        return self._fallback.chat(conversation_id, history, base, base_sources)

    @staticmethod
    def _validate_chat(candidate: _ChatOutput, conversation_id: str, history: list[dict],
                       base: Draft, base_sources: list[FieldSource]) -> ChatResponse:
        if not candidate.message.strip():
            raise ValueError("Blank message")
        user_messages = _user_messages(history)
        users = {message["id"]: message["content"] for message in user_messages}
        positions = {message["id"]: index for index, message in enumerate(user_messages)}
        values = base.model_dump()
        provenance = {source.field: source for source in base_sources}
        proposed = candidate.draft.model_dump() if candidate.draft is not None else {}
        candidate_sources = {}
        for source in candidate.sources:
            value = proposed.get(source.field, values.get(source.field, ""))
            if not is_filled(value) or not _source_valid(source, users, value):
                raise ValueError("Unproven source")
            if source.field in candidate_sources:
                raise ValueError("Duplicate source")
            candidate_sources[source.field] = source
        for field, value in proposed.items():
            if not is_filled(value) or value == values[field]:
                continue
            source = candidate_sources.get(field)
            if source is None:
                raise ValueError("Unsourced new fact")
            previous = provenance.get(field)
            if previous and positions[source.messageId] < positions[previous.messageId]:
                raise ValueError("Stale fact")
            values[field] = value
            provenance[field] = source
        merged = Draft.model_validate(values)
        missing = [field for field in FIELD_NAMES if not is_filled(values[field])]
        first_turn = len(user_messages) <= 1
        if first_turn and missing:
            if candidate.phase != "clarifying":
                raise ValueError("Missing first clarification")
            fields = [question.field for question in candidate.questions]
            if (len(fields) < min(3, len(missing)) or len(fields) != len(set(fields))
                    or any(field not in missing for field in fields)
                    or len({q.text.strip().casefold() for q in candidate.questions}) != len(fields)
                    or any(not q.text.strip() for q in candidate.questions)):
                raise ValueError("Insufficient distinct questions about missing fields")
        elif candidate.phase != "draft_ready" or candidate.draft is None or candidate.questions:
            raise ValueError("A follow-up must return an editable draft")
        if candidate.phase == "draft_ready" and candidate.draft is None:
            raise ValueError("Missing editable draft")
        return ChatResponse(conversationId=conversation_id, message=candidate.message,
                            phase=candidate.phase, aiMode="live", questions=candidate.questions,
                            draft=merged if candidate.draft is not None else None,
                            sources=list(provenance.values()), missingFields=missing)

    async def clarity(self, draft: Draft) -> ClarityResult:
        if self._provider is not None:
            for attempt in range(2):
                try:
                    text = await asyncio.wait_for(self._provider.generate(
                        "clarity", {"draft": draft.model_dump()}, repair=attempt > 0), timeout=self._timeout)
                    result = _ClarityOutput.model_validate_json(text, strict=True)
                    if not result.clarityReason.strip():
                        raise ValueError("Blank clarity reason")
                    return ClarityResult(**result.model_dump(), aiMode="live")
                except httpx.HTTPStatusError as exc:
                    if exc.response.status_code < 500 and exc.response.status_code != 429:
                        break
                except (httpx.RequestError, TimeoutError, ValidationError, ValueError, TypeError, KeyError):
                    pass
        return self._fallback.clarity()
