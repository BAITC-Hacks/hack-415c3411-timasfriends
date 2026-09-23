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
        var gameType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("UnityEditor.GameView")).FirstOrDefault(t=>t!=null);
        if(gameType!=null)
        {
            var view=UnityEditor.EditorWindow.GetWindow(gameType);view.Show();view.maximized=true;view.Focus();
            var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            gameType.GetProperty("lowResolutionForAspectRatios",flags)?.SetValue(view,false);
            var zoom=gameType.GetField("m_ZoomArea",flags)?.GetValue(view);
            if(zoom!=null)
            {
                var scale=gameType.GetProperty("minScale",flags)?.GetValue(view);
                float nativeScale=scale is float f?f:1;
                zoom.GetType().GetMethod("SetScaleFocused",flags,null,new[]{typeof(Vector2),typeof(Vector2)},null)?.Invoke(zoom,new object[]{Vector2.zero,Vector2.one*nativeScale});
            }
            view.Repaint();
        }
    }
}
