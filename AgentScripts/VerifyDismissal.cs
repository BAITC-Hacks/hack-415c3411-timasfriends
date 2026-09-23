using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using QuestBridge;
using UnityEngine;
using UnityEngine.EventSystems;

public static class VerifyDismissal
{
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Field(QuestBridgeApp app,string name)=>typeof(QuestBridgeApp).GetField(name,Flags).GetValue(app);
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static object Main()
    {
        var app=UnityEngine.Object.FindFirstObjectByType<QuestBridgeApp>();
        var buttons=app.GetComponentsInChildren<UnityEngine.UI.Button>(true);
        buttons.First(b=>b.name=="Фокус").onClick.Invoke();
        Check((bool)Field(app,"focus"),"Focus did not activate.");
        var back=buttons.First(b=>b.name=="Вернуться в каталог");
        Check(back.GetComponent<RectTransform>().anchorMax.x-back.GetComponent<RectTransform>().anchorMin.x>.5f,"Return control is not large.");
        back.onClick.Invoke();Check(!(bool)Field(app,"focus"),"Return control did not exit focus.");
        var snapshot=(Snapshot)Field(app,"snapshot");
        typeof(QuestBridgeApp).GetMethod("OpenDetails",Flags).Invoke(app,new object[]{snapshot.cards[0]});
        return new {focusReturnWorks=true,modalOpened=true,next="Run VerifyDismissal.CheckModal after the opening animation"};
    }
    public static object CheckModal()
    {
        var app=UnityEngine.Object.FindFirstObjectByType<QuestBridgeApp>();
        Canvas.ForceUpdateCanvases();
        var overlay=(RectTransform)Field(app,"detailOverlay");var pane=overlay.Find("Detail pane").GetComponent<RectTransform>();
        var inside=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,pane.TransformPoint(new Vector3(0,pane.rect.height*.42f,0)))};
        var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(inside,hits);
        if(hits.Count==0)throw new Exception("Raycast diagnostic: depth="+pane.GetComponent<UnityEngine.UI.Image>().depth+" cull="+pane.GetComponent<CanvasRenderer>().cull+" alpha="+pane.GetComponent<CanvasGroup>().alpha+" imageRay="+pane.GetComponent<UnityEngine.UI.Image>().Raycast(inside.position,null)+" canvasDepth="+app.GetComponentInChildren<Canvas>().GetComponent<CanvasGroup>().alpha+" relative="+Display.RelativeMouseAt(inside.position));
        Check(hits.Count>0&&hits[0].gameObject.name!="Dismiss backdrop","Panel does not block backdrop clicks: "+string.Join(",",hits.Select(h=>h.gameObject.name))+" pointer="+inside.position+" pane="+pane.rect+" pos="+pane.position+" image="+pane.GetComponent<UnityEngine.UI.Image>().raycastTarget+" mode="+app.GetComponentInChildren<Canvas>().renderMode);
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,inside,ExecuteEvents.pointerClickHandler);
        Check((RectTransform)Field(app,"detailOverlay")!=null,"Inside click closed modal.");
        var outside=new PointerEventData(EventSystem.current){position=new Vector2(4,4)};hits.Clear();EventSystem.current.RaycastAll(outside,hits);
        Check(hits.Count>0&&hits[0].gameObject.name=="Dismiss backdrop","Outside raycast missed backdrop.");
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,outside,ExecuteEvents.pointerClickHandler);
        Check((RectTransform)Field(app,"detailOverlay")==null,"Backdrop did not close modal.");
        return new {focusReturnWorks=true,insideClickPreserved=true,outsideClickDismissed=true,target=UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString()};
    }
}
