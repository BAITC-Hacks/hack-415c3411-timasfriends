"""Formula and input contract regressions independent of any AI provider."""

from itertools import combinations

import pytest
from pydantic import ValidationError

from backend.app.models import Draft, PreviewRequest, ProposalRequest, PublishTaskRequest, UpdateTaskRequest
from backend.app.scoring import WEIGHTS, is_filled, readiness_level, score_draft


@pytest.fixture
def filled_draft():
    return Draft(**{field: "Подтверждённый факт" for field in WEIGHTS})


def test_weights_total_one_hundred_and_title_category_do_not_score(filled_draft):
    assert sum(WEIGHTS.values()) == 100
    assert len(WEIGHTS) == 10
    assert score_draft(filled_draft, list(WEIGHTS)).readiness == 100
    result = score_draft(Draft(title="Название", category="Образование"), ["title", "category"])
    assert result.readiness == 0
    assert result.missingFields == list(WEIGHTS)


def test_unconfirmed_text_and_confirmed_empty_text_receive_no_points(filled_draft):
    assert score_draft(filled_draft, []).readiness == 0
    result = score_draft(Draft(context="Есть контекст"), ["context", "data"])
    rows = {item.field: item for item in result.scoreBreakdown}
    assert result.readiness == 10
    assert rows["context"].filled and rows["context"].confirmed
    assert rows["data"].confirmed and not rows["data"].filled
    assert rows["data"].points == 0
    assert "context" not in result.missingFields
    assert "data" in result.missingFields


def test_duplicate_confirmations_cannot_increase_score(filled_draft):
    result = score_draft(filled_draft, ["context", "context", "need"])
    assert result.readiness == 20
    assert result == score_draft(filled_draft, ["need", "context"])


@pytest.mark.parametrize(
    "value",
    [
        "", "   ", "\n\t", "-", "—", "...", "!?", "___", "🤔",
        "не знаю", " НЕ УКАЗАНО!!! ", "[TBD]", "T\u200bBD", "N/A", "null",
        "не знаю / TBD", "нет данных", "to be determined",
    ],
)
def test_placeholders_do_not_count(value):
    assert not is_filled(value)
    result = score_draft(Draft(data=value), ["data"])
    assert result.readiness == 0


@pytest.mark.parametrize(
    "value",
    [
        "0 ошибок", "CSV", "Учителя", "Нет ограничений", "Нужен бот",
        "Не знаю срок, но доступны 20 работ", "Точность: 90%", "0",
    ],
)
def test_substantive_values_are_not_arbitrarily_discounted(value):
    assert is_filled(value)
    assert score_draft(Draft(data=value), ["data"]).readiness == 20


@pytest.mark.parametrize(
    ("target", "level"),
    [(0, "draft"), (39, "draft"), (40, "workable"), (69, "workable"),
     (70, "ready"), (89, "ready"), (90, "priority"), (100, "priority")],
)
def test_readiness_level_boundaries(target, level):
    assert readiness_level(target) == level


@pytest.mark.parametrize("target", [0, 39, 40, 69, 70, 87, 90, 100])
def test_score_sum_and_missing_fields(filled_draft, target):
    fields = list(WEIGHTS)
    selected = next(
        subset
        for size in range(len(fields) + 1)
        for subset in combinations(fields, size)
        if sum(WEIGHTS[field] for field in subset) == target
    )
    result = score_draft(filled_draft, selected)
    assert result.readiness == target
    assert result.readinessLevel == readiness_level(target)
    assert len(result.scoreBreakdown) == 10
    assert result.readiness == sum(row.points for row in result.scoreBreakdown)
    assert result.missingFields == [field for field in fields if field not in selected]


def test_api_rejects_unknown_field_names_and_client_scores():
    with pytest.raises(ValidationError):
        PreviewRequest(draft={}, confirmedFields=["budget"])
    with pytest.raises(ValidationError):
        PublishTaskRequest(businessId="business-demo", draft={}, confirmed=True, readiness=100)


@pytest.mark.parametrize("confirmed", ["true", "false", 0, 1, None])
def test_confirmation_requires_an_actual_boolean(confirmed):
    with pytest.raises(ValidationError):
        PublishTaskRequest(businessId="business-demo", draft={}, confirmed=confirmed)


@pytest.mark.parametrize("version", [True, "1", 1.0, 0, -1])
def test_update_version_requires_a_positive_integer(version):
    with pytest.raises(ValidationError):
        UpdateTaskRequest(businessId="business-demo", draft={}, confirmed=True, version=version)


def test_draft_trims_text_and_limits_lengths():
    assert Draft(context="  Контекст  ").context == "Контекст"
    assert all(value == "" for value in Draft().model_dump().values())
    with pytest.raises(ValidationError):
        Draft(title="x" * 161)
    with pytest.raises(ValidationError):
        Draft(data="x" * 2001)


@pytest.mark.parametrize("url", ["javascript:alert(1)", "file:///tmp/a", "ftp://example.org", "https://", "https:example.org", "https://example.org/a b"])
def test_proposals_reject_non_http_and_invalid_urls(url):
    with pytest.raises(ValidationError):
        ProposalRequest(teamId="team-1", idea="Идея", plan="План", timeline="Неделя", prototypeUrl=url)


def test_proposals_preserve_valid_http_url_and_require_nonempty_details():
    proposal = ProposalRequest(teamId="team-1", idea="Идея", plan="План", timeline="Неделя", prototypeUrl="https://example.org/prototype")
    assert proposal.prototypeUrl == "https://example.org/prototype"
    assert proposal.publishDetails is False
    with pytest.raises(ValidationError):
        ProposalRequest(teamId="team-1", idea="  ", plan="План", timeline="Неделя", prototypeUrl="https://example.org")
