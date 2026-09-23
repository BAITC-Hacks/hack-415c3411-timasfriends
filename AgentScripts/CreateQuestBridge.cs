public static class CreateQuestBridge
{
    public static void Main()
    {
        var existing = UnityEngine.Object.FindFirstObjectByType<QuestBridge.QuestBridgeApp>();
        if (!existing) new UnityEngine.GameObject("QuestBridge").AddComponent<QuestBridge.QuestBridgeApp>();
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/QuestBridge/QuestBridge.unity");
    }
}
