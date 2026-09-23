using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
public static class InspectPreview
{
    public static object Main()
    {
        var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("UnityEditor.GameView")).First(t=>t!=null);
        var view=EditorWindow.GetWindow(type);
        var zoom=type.GetField("m_ZoomArea",flags)?.GetValue(view);
        return new {
            properties=type.GetProperties(flags).Where(p=>p.Name.IndexOf("resolution",StringComparison.OrdinalIgnoreCase)>=0||p.Name.IndexOf("scale",StringComparison.OrdinalIgnoreCase)>=0||p.Name.IndexOf("size",StringComparison.OrdinalIgnoreCase)>=0).Select(p=>new {p.Name,p.CanWrite,value=p.GetIndexParameters().Length==0?p.GetValue(view)?.ToString():"indexed"}).ToArray(),
            zoomProperties=zoom?.GetType().GetProperties(flags).Where(p=>p.Name.IndexOf("scale",StringComparison.OrdinalIgnoreCase)>=0).Select(p=>new{p.Name,p.CanWrite,value=p.GetValue(zoom)?.ToString()}).ToArray(),
            zoomMethods=zoom?.GetType().GetMethods(flags).Where(m=>m.Name.Contains("Scale")||m.Name.Contains("Transform")).Select(m=>m.ToString()).ToArray()
        };
    }
}
