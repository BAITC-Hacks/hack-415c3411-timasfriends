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
NUMBERED_LINE_RE = re.compile(r"(?:^|[\n;])\s*([1-3])[.):]\s*([^\n;]*)")
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
    questions: list[Question] = Field(max_length=1)
    draft: Draft
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
                 transport: httpx.AsyncBaseTransport | None = None, *, reasoning_effort: str = ""):
        self._api_key = api_key
        self._model = model
        self._timeout = timeout
        self._transport = transport
        self._reasoning_effort = reasoning_effort.strip()

    async def generate(self, task: str, payload: dict, repair: bool = False) -> str:
        instructions = (PROMPTS / f"{task}_system.txt").read_text(encoding="utf-8")
        schema = json.loads((PROMPTS / f"{task}_schema.json").read_text(encoding="utf-8"))
        if repair:
            instructions += (
                "\nПредыдущий ответ не прошёл проверку. Создай ответ заново строго по JSON schema. "
                "Проверь типы, диапазоны, цитаты USER, пустые неизвестные поля и правила фазы. "
                "Это единственная попытка исправления."
            )
        request_body = {
            "model": self._model,
            "instructions": instructions,
            "input": [{"role": "user", "content": json.dumps(payload, ensure_ascii=False)}],
            "text": {"format": {"type": "json_schema", "name": f"questbridge_{task}",
                                 "schema": schema, "strict": True}},
            "store": False,
            "max_output_tokens": 4000,
        }
        if self._reasoning_effort:
            request_body["reasoning"] = {"effort": self._reasoning_effort}
        async with httpx.AsyncClient(timeout=self._timeout, transport=self._transport) as client:
            response = await client.post(
                "https://api.openai.com/v1/responses",
                headers={"Authorization": f"Bearer {self._api_key}"},
                json=request_body,
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


def _initial_question_fields(history: list[dict]) -> dict[int, str]:
    """Map answer numbers only when the stored assistant asked known questions."""
    first_user_seen = False
    for message in history:
        if message.get("role") == "user":
            if first_user_seen:
                break
            first_user_seen = True
        elif first_user_seen and message.get("role") == "assistant":
            question_fields = {text: field for field, text in QUESTION_TEXT.items()}
            return {
                int(match.group(1)): question_fields[match.group(2).strip()]
                for match in NUMBERED_LINE_RE.finditer(message["content"])
                if match.group(2).strip() in question_fields
            }
    return {}


def _stored_questions(message: dict) -> list[dict]:
    questions = message.get("questions", [])
    if not isinstance(questions, list):
        return []
    return [question for question in questions if isinstance(question, dict)
            and question.get("field") in QUESTION_TEXT]


def _question_flow(history: list[dict], draft: Draft) -> tuple[Question | None, int]:
    """The server owns the three question turns, including across restarts."""
    assistants = [message for message in history if message.get("role") == "assistant"]
    recorded = [message for message in assistants if "questions" in message or "phase" in message]
    asked_fields = []
    if recorded:
        if any(message.get("phase") == "draft_ready" for message in recorded):
            return None, 0
        # Preserve steps from before this conversation started storing metadata.
        legacy_history = history[:history.index(recorded[0])]
        completed = min(max(len(_user_messages(legacy_history)) - 1, 0), 3)
        legacy_fields = list(_initial_question_fields(legacy_history).values()) or list(QUESTION_TEXT)
        asked_fields = legacy_fields[:completed]
        for message in recorded:
            for question in _stored_questions(message):
                if question["field"] not in asked_fields:
                    asked_fields.append(question["field"])
                # The ordinal also preserves progress when an old conversation
                # starts recording question metadata midway through its flow.
                ordinal = re.fullmatch(r"q-([1-3])-[A-Za-z]+", str(question.get("id", "")))
                if ordinal:
                    completed = max(completed, int(ordinal.group(1)))
        completed = max(completed, len(asked_fields))
    else:
        # Old rows contain only role/content. Do not restart their questionnaire.
        completed = min(max(len(_user_messages(history)) - 1, 0), 3)
        legacy_fields = list(_initial_question_fields(history).values())
        inferred = legacy_fields or list(QUESTION_TEXT)
        asked_fields = inferred[:completed]
    if completed >= 3:
        return None, 0
    available = [field for field in QUESTION_TEXT if field not in asked_fields]
    field = next((field for field in available if not is_filled(getattr(draft, field))), available[0])
    number = completed + 1
    text = QUESTION_TEXT[field]
    if is_filled(getattr(draft, field)):
        text = f"Подтверждаете значение поля «{FIELD_LABELS[field]}» в черновике?"
    return Question(id=f"q-{number}-{field}", field=field, text=text), number


def _answer_fields(history: list[dict]) -> dict[str, str]:
    """A plain answer belongs only to the immediately preceding single question."""
    fields = {}
    pending_field = None
    for message in history:
        if message.get("role") == "assistant":
            questions = _stored_questions(message)
            pending_field = (questions[0]["field"]
                             if message.get("phase") == "clarifying" and len(questions) == 1 else None)
        elif message.get("role") == "user":
            if pending_field is not None:
                fields[message["id"]] = pending_field
            pending_field = None
    return fields


def _chat_message(question: Question | None, number: int, missing: list[str]) -> str:
    if question is not None:
        return f"Вопрос {number} из 3. {question.text}"
    message = "Ответ сохранён. Черновик готов, задача ещё не опубликована."
    if missing:
        message += " Неизвестные поля оставлены пустыми. Дополните сведения и проверьте карточку перед публикацией."
    else:
        message += " Проверьте данные перед публикацией."
    return message


def _extract(history: list[dict], draft: Draft, sources: list[FieldSource]) -> tuple[Draft, list[FieldSource]]:
    """Only copy literal user text; an unknown reply never erases known facts."""
    values = draft.model_dump()
    user_messages = _user_messages(history)
    initial_questions = _initial_question_fields(history)
    answer_fields = _answer_fields(history)
    users = {message["id"]: message["content"] for message in user_messages}
    positions = {message["id"]: index for index, message in enumerate(user_messages)}
    provenance = {source.field: source for source in sources
                  if source.field in values and is_filled(values[source.field])
                  and _source_valid(source, users, values[source.field])}

    def assign(field: str, value: str, message: dict, *, implicit: bool = False) -> None:
        value = _bounded(field, value)
        previous = provenance.get(field)
        if previous and positions[previous.messageId] > positions[message["id"]]:
            return
        if implicit and previous and previous.messageId == message["id"]:
            # Replaying a plain answer must not replace an already sourced,
            # more precise live extraction from that very same answer.
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
        if index == 1 and not INSTRUCTION_RE.search(content):
            # A numbered answer refers to the initial questions, not a guessed
            # field order. Later corrections still use explicit field labels.
            for match in NUMBERED_LINE_RE.finditer(content):
                field = initial_questions.get(int(match.group(1)))
                if field:
                    assign(field, match.group(2), message)
        answer_field = answer_fields.get(message["id"])
        has_labels = any(match.group(1).strip().casefold() in ALIASES for match in labelled)
        if answer_field and not has_labels and not INSTRUCTION_RE.search(content):
            numbered = list(NUMBERED_LINE_RE.finditer(content))
            answer = numbered[0].group(2) if len(numbered) == 1 else content
            confirmation = answer.strip().casefold().rstrip(".! ") in {
                "да", "верно", "всё верно", "все верно", "да верно", "подтверждаю", "без изменений",
            }
            if len(numbered) <= 1 and not (confirmation and is_filled(values[answer_field])):
                assign(answer_field, answer, message, implicit=True)
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
        question, number = _question_flow(history, draft)
        return ChatResponse(conversationId=conversation_id, message=_chat_message(question, number, missing),
                            phase="clarifying" if question is not None else "draft_ready", aiMode="fallback",
                            questions=[question] if question is not None else [],
                            draft=draft, sources=sources, missingFields=missing)

    def clarity(self) -> ClarityResult:
        return ClarityResult(clarity=0, clarityReason="Оценка AI сейчас недоступна. 0 не является оценкой ясности задачи.",
                             aiMode="fallback")


class AIService:
    def __init__(self, api_key: str = "", model: str = "", timeout: float = 10.0,
                 transport: httpx.AsyncBaseTransport | None = None, *, reasoning_effort: str = ""):
        self._timeout = max(0.1, min(float(timeout), 10.0))
        self._provider: AIProvider | None = (
            OpenAIProvider(api_key, model, self._timeout, transport, reasoning_effort=reasoning_effort)
            if api_key and model else None
        )
        self._fallback = FallbackProvider()

    async def chat(self, conversation_id: str, history: list[dict], draft: Draft,
                   sources: list[FieldSource]) -> ChatResponse:
        base, base_sources = _extract(history, draft, sources)
        question, number = _question_flow(history, base)
        if self._provider is not None:
            payload = {"history": history, "knownDraft": base.model_dump(),
                       "knownSources": [source.model_dump() for source in base_sources],
                       "expectedPhase": "clarifying" if question is not None else "draft_ready",
                       "nextQuestion": question.model_dump() if question is not None else None,
                       "questionNumber": number}
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
        question, number = _question_flow(history, base)
        if question is not None:
            if (candidate.phase != "clarifying" or len(candidate.questions) != 1
                    or candidate.questions[0].field != question.field):
                raise ValueError("Return only the server-selected question for this turn")
        elif candidate.phase != "draft_ready" or candidate.questions:
            raise ValueError("The third answer must produce a draft without another question")
        return ChatResponse(conversationId=conversation_id, message=_chat_message(question, number, missing),
                            phase=candidate.phase, aiMode="live", questions=[question] if question is not None else [],
                            draft=merged,
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
