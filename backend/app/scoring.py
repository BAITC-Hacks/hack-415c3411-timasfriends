"""Deterministic completeness scoring; no AI opinions enter this formula."""

import re
import unicodedata
from collections.abc import Iterable

from .models import Draft, FieldName, PreviewResponse, ReadinessLevel, ScoreBreakdownItem


WEIGHTS: dict[FieldName, int] = {
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

_PLACEHOLDERS = {
    "не знаю",
    "пока не знаю",
    "неизвестно",
    "не известно",
    "не указано",
    "не указан",
    "не указана",
    "не указаны",
    "не определено",
    "не определён",
    "не определен",
    "не определена",
    "не определены",
    "не задано",
    "нет",
    "нет данных",
    "данных нет",
    "нет информации",
    "нет ответа",
    "информация отсутствует",
    "отсутствует",
    "уточняется",
    "уточнить",
    "tbd",
    "todo",
    "unknown",
    "not known",
    "not specified",
    "not provided",
    "not available",
    "to be determined",
    "to be defined",
    "to be decided",
    "i don t know",
    "n a",
    "na",
    "none",
    "null",
    "undefined",
}
_PLACEHOLDER_PART = "(?:" + "|".join(
    re.escape(value) for value in sorted(_PLACEHOLDERS, key=len, reverse=True)
) + ")"
_ONLY_PLACEHOLDERS = re.compile(rf"{_PLACEHOLDER_PART}(?:\s+{_PLACEHOLDER_PART})*")


def is_filled(value: str) -> bool:
    """Recognize text presence, excluding explicit unknowns and punctuation.

    This does not establish truth or quality. Meaningful short strings and
    numeric goals still count; a model cannot arbitrarily discount them.
    """
    normalized = unicodedata.normalize("NFKC", value).casefold()
    normalized = "".join(
        char for char in normalized if unicodedata.category(char) != "Cf"
    )
    normalized = re.sub(r"[\W_]+", " ", normalized, flags=re.UNICODE).strip()
    return bool(normalized) and _ONLY_PLACEHOLDERS.fullmatch(normalized) is None


def readiness_level(readiness: int) -> ReadinessLevel:
    if readiness < 40:
        return "draft"
    if readiness < 70:
        return "workable"
    if readiness < 90:
        return "ready"
    return "priority"


def score_draft(draft: Draft, confirmedFields: Iterable[FieldName]) -> PreviewResponse:
    """Award the fixed weight only when a field is present and confirmed."""
    confirmed_fields = set(confirmedFields)
    breakdown: list[ScoreBreakdownItem] = []
    missing: list[FieldName] = []
    for field, weight in WEIGHTS.items():
        filled = is_filled(getattr(draft, field))
        confirmed = field in confirmed_fields
        points = weight if filled and confirmed else 0
        breakdown.append(
            ScoreBreakdownItem(
                field=field,
                maxPoints=weight,
                points=points,
                filled=filled,
                confirmed=confirmed,
            )
        )
        if not points:
            missing.append(field)

    readiness = sum(item.points for item in breakdown)
    return PreviewResponse(
        readiness=readiness,
        scoreBreakdown=breakdown,
        missingFields=missing,
        readinessLevel=readiness_level(readiness),
    )
