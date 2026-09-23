using System;

namespace QuestBridge
{
    [Serializable] public class DraftData
    {
        public string title="", category="", context="", need="", users="", data="", constraints="", expectedResult="", successCriteria="", contact="", interactionFormat="", feedbackProcess="";
    }
    [Serializable] public class ScoreRow { public string field; public int maxPoints, points; public bool filled, confirmed; }
    [Serializable] public class ScorePreview { public int readiness; public string readinessLevel; public ScoreRow[] scoreBreakdown; public string[] missingFields; }
    [Serializable] public class TaskRecord
    {
        public string id,businessId,clarityReason,aiMode; public DraftData draft; public int readiness,version,proposalCount; public float clarity;
        public string[] confirmedFields, missingFields, teamIds; public ScoreRow[] scoreBreakdown; public bool demo;
    }
    [Serializable] public class PreviewBody { public DraftData draft; public string[] confirmedFields; }
    [Serializable] public class PublishBody { public string businessId,conversationId; public DraftData draft; public string[] confirmedFields; public bool confirmed; }
    [Serializable] public class EditTaskBody : PublishBody { public int version; public string[] reconfirmedFields; }
    [Serializable] public class APIErrorEnvelope { public APIErrorData error; }
    [Serializable] public class APIErrorData { public string code,message; }
}
