"""Validated, camelCase JSON contracts shared by the QuestBridge endpoints."""

from datetime import datetime, timedelta
from typing import Annotated, Literal
from urllib.parse import urlsplit

from pydantic import (
    AfterValidator,
    BaseModel,
    ConfigDict,
    Field,
    HttpUrl,
    StrictBool,
    StringConstraints,
    TypeAdapter,
)


FieldName = Literal[
    "title",
    "category",
    "context",
    "need",
    "users",
    "data",
    "constraints",
    "expectedResult",
    "successCriteria",
    "contact",
    "interactionFormat",
    "feedbackProcess",
]
AIMode = Literal["live", "fallback"]
ReadinessLevel = Literal["draft", "workable", "ready", "priority"]
Identifier = Annotated[
    str, StringConstraints(strip_whitespace=True, pattern=r"^[A-Za-z0-9_-]{1,80}$")
]
ConversationIdentifier = Annotated[
    str, StringConstraints(strip_whitespace=True, pattern=r"^[A-Za-z0-9_-]{0,80}$")
]
ShortText = Annotated[str, StringConstraints(max_length=2000)]
RequiredText = Annotated[
    str, StringConstraints(strip_whitespace=True, min_length=1, max_length=2000)
]
NonNegativeInt = Annotated[int, Field(strict=True, ge=0)]
PositiveInt = Annotated[int, Field(strict=True, ge=1)]
Readiness = Annotated[int, Field(strict=True, ge=0, le=100)]
Clarity = Annotated[float, Field(strict=True, ge=0, le=10, allow_inf_nan=False)]
FieldNames = Annotated[list[FieldName], Field(max_length=12)]


def _validate_utc(value: str) -> str:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise ValueError("Date must be an ISO 8601 UTC timestamp") from exc
    if "T" not in value or parsed.tzinfo is None or parsed.utcoffset() != timedelta(0):
        raise ValueError("Date must be an ISO 8601 UTC timestamp")
    return value


UTCDateTime = Annotated[
    str, StringConstraints(min_length=20, max_length=40), AfterValidator(_validate_utc)
]
_http_url_adapter = TypeAdapter(HttpUrl)


def _validate_prototype_url(value: str) -> str:
    if "\\" in value or any(char.isspace() or ord(char) < 32 for char in value):
        raise ValueError("prototypeUrl must be an HTTP or HTTPS URL without whitespace")
    parsed = urlsplit(value)
    if parsed.scheme.lower() not in {"http", "https"} or not parsed.netloc or not parsed.hostname:
        raise ValueError("prototypeUrl must be an absolute HTTP or HTTPS URL")
    _http_url_adapter.validate_python(value)
    return value


PrototypeURL = Annotated[
    str,
    StringConstraints(strip_whitespace=True, min_length=1, max_length=2000),
    AfterValidator(_validate_prototype_url),
]


class APIModel(BaseModel):
    model_config = ConfigDict(
        extra="forbid", str_strip_whitespace=True, validate_default=True
    )


class ErrorBody(APIModel):
    code: Annotated[str, StringConstraints(min_length=1, max_length=80)]
    message: RequiredText


class ErrorResponse(APIModel):
    error: ErrorBody


class HealthResponse(APIModel):
    status: Literal["ok"] = "ok"
    service: Literal["questbridge"] = "questbridge"
    aiMode: AIMode
    demo: StrictBool = True


class Draft(APIModel):
    title: Annotated[str, StringConstraints(max_length=160)] = ""
    category: Annotated[str, StringConstraints(max_length=100)] = ""
    context: ShortText = ""
    need: ShortText = ""
    users: ShortText = ""
    data: ShortText = ""
    constraints: ShortText = ""
    expectedResult: ShortText = ""
    successCriteria: ShortText = ""
    contact: ShortText = ""
    interactionFormat: ShortText = ""
    feedbackProcess: ShortText = ""


class ScoreBreakdownItem(APIModel):
    field: FieldName
    maxPoints: Annotated[int, Field(strict=True, ge=0, le=100)]
    points: Annotated[int, Field(strict=True, ge=0, le=100)]
    filled: StrictBool
    confirmed: StrictBool


class PreviewRequest(APIModel):
    draft: Draft
    confirmedFields: FieldNames = Field(default_factory=list)


class PreviewResponse(APIModel):
    readiness: Readiness
    scoreBreakdown: Annotated[list[ScoreBreakdownItem], Field(min_length=10, max_length=10)]
    missingFields: FieldNames
    readinessLevel: ReadinessLevel


class Question(APIModel):
    id: Identifier
    field: FieldName
    text: RequiredText


class FieldSource(APIModel):
    field: FieldName
    messageId: Identifier
    quote: Annotated[str, StringConstraints(min_length=1, max_length=6000)]


class ChatRequest(APIModel):
    conversationId: ConversationIdentifier = ""
    message: Annotated[
        str, StringConstraints(strip_whitespace=True, min_length=1, max_length=6000)
    ]


class ChatResponse(APIModel):
    conversationId: Identifier
    message: Annotated[str, StringConstraints(min_length=1, max_length=10000)]
    phase: Literal["clarifying", "draft_ready"]
    aiMode: AIMode
    questions: Annotated[list[Question], Field(max_length=12)]
    draft: Draft | None
    sources: Annotated[list[FieldSource], Field(max_length=120)] = Field(default_factory=list)
    missingFields: FieldNames = Field(default_factory=list)


class PublishTaskRequest(APIModel):
    businessId: Identifier
    draft: Draft
    confirmedFields: FieldNames = Field(default_factory=list)
    confirmed: StrictBool
    conversationId: ConversationIdentifier = ""


class UpdateTaskRequest(PublishTaskRequest):
    version: PositiveInt
    reconfirmedFields: FieldNames = Field(default_factory=list)


class TaskResponse(PreviewResponse):
    id: Identifier
    businessId: Identifier
    draft: Draft
    confirmedFields: FieldNames
    confirmed: StrictBool = True
    clarity: Clarity
    clarityReason: RequiredText
    aiMode: AIMode
    version: PositiveInt
    createdAt: UTCDateTime
    updatedAt: UTCDateTime
    teamIds: list[Identifier] = Field(default_factory=list)
    proposalCount: NonNegativeInt = 0
    sources: list[FieldSource] = Field(default_factory=list)
    demo: StrictBool = False


class Team(APIModel):
    id: Identifier
    name: Annotated[str, StringConstraints(min_length=1, max_length=160)]
    initials: Annotated[str, StringConstraints(min_length=1, max_length=10)]
    stack: Annotated[str, StringConstraints(max_length=500)]
    description: ShortText
    completed: NonNegativeInt = 0
    experience: NonNegativeInt = 0


class CatalogCard(APIModel):
    id: Identifier
    title: Annotated[str, StringConstraints(min_length=1, max_length=160)]
    description: ShortText
    category: Annotated[str, StringConstraints(min_length=1, max_length=100)]
    readiness: Readiness
    clarity: Clarity
    teamIds: list[Identifier] = Field(default_factory=list)
    demo: StrictBool = False


class CatalogResponse(APIModel):
    version: NonNegativeInt
    cards: list[CatalogCard]
    teams: list[Team]


class ProposalDetails(APIModel):
    idea: RequiredText
    plan: RequiredText
    timeline: RequiredText
    prototypeUrl: PrototypeURL


class ProposalRequest(ProposalDetails):
    teamId: Identifier
    publishDetails: StrictBool = False


class ProposalResponse(APIModel):
    id: Identifier
    taskId: Identifier
    teamId: Identifier
    team: Team
    decision: Literal["pending", "selected", "rejected"]
    publishDetails: StrictBool
    details: ProposalDetails | None
    createdAt: UTCDateTime
    updatedAt: UTCDateTime


class ProposalListResponse(APIModel):
    proposals: list[ProposalResponse]


class DecisionRequest(APIModel):
    businessId: Identifier
    decision: Literal["selected", "rejected"]


class MilestoneRequest(APIModel):
    teamId: Identifier
    description: RequiredText


class ConfirmMilestoneRequest(APIModel):
    businessId: Identifier


class MilestoneResponse(APIModel):
    id: Identifier
    proposalId: Identifier
    taskId: Identifier
    teamId: Identifier
    description: RequiredText
    confirmed: StrictBool
    experienceAwarded: NonNegativeInt
    createdAt: UTCDateTime
    confirmedAt: UTCDateTime | None
    team: Team


class Event(APIModel):
    id: PositiveInt
    type: Literal[
        "task_published",
        "task_updated",
        "proposal_submitted",
        "team_selected",
        "milestone_confirmed",
    ]
    taskId: Identifier
    message: RequiredText
    createdAt: UTCDateTime


class EventsResponse(APIModel):
    cursor: NonNegativeInt
    events: list[Event]
