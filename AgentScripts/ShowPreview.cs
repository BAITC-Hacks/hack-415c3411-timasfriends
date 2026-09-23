using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class ShowPreview
{
    public static void Main()
    {
        var canvas=UnityEngine.Object.FindFirstObjectByType<Canvas>();
        if(canvas)canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        var app=UnityEngine.Object.FindFirstObjectByType<QuestBridge.QuestBridgeApp>();
        var field=typeof(QuestBridge.QuestBridgeApp).GetField("activityText",BindingFlags.Instance|BindingFlags.NonPublic);
        if(app&&field.GetValue(app) is TMPro.TMP_Text text)text.text="Готовность задачи определяет её место";
        var gameType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("UnityEditor.GameView")).FirstOrDefault(t=>t!=null);
        if(gameType!=null){var view=UnityEditor.EditorWindow.GetWindow(gameType);view.Show();view.Focus();}
    }
}
