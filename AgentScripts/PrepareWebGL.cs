using System.Linq;
using UnityEditor;
public static class PrepareWebGL
{
    public static object Main()
    {
        const string scene="Assets/QuestBridge/QuestBridge.unity";
        var others=EditorBuildSettings.scenes.Where(s=>s.path!=scene&&s.path!="Assets/Scenes/SampleScene.unity");
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(scene,true)}.Concat(others).ToArray();
        PlayerSettings.defaultWebScreenWidth=1600;
        PlayerSettings.defaultWebScreenHeight=900;
        AssetDatabase.SaveAssets();
        return new {target=EditorUserBuildSettings.activeBuildTarget.ToString(),firstScene=EditorBuildSettings.scenes[0].path};
    }
}
