using System;
using System.Linq;
using System.Reflection;
using QuestBridge;
using UnityEngine;

public static class VerifyExperience
{
    static object Field(QuestBridgeApp app,string name)=>typeof(QuestBridgeApp).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app);
    static Snapshot Copy(Snapshot s)=>JsonUtility.FromJson<Snapshot>(JsonUtility.ToJson(s));
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static object Main()
    {
        var app=UnityEngine.Object.FindFirstObjectByType<QuestBridgeApp>();
        Check(app&&Application.isPlaying,"Run QuestBridge in Play mode first.");
        var initial=Copy((Snapshot)Field(app,"snapshot"));
        var content=(RectTransform)Field(app,"content");
        content.anchoredPosition=new Vector2(0,80);
        var buttons=content.GetComponentsInChildren<UnityEngine.UI.Button>().Select(b=>b.GetInstanceID()).ToArray();
        app.Apply(Copy(initial),true);
        Check(buttons.SequenceEqual(content.GetComponentsInChildren<UnityEngine.UI.Button>().Select(b=>b.GetInstanceID())),"Unchanged snapshot rebuilt buttons.");
        Check(Mathf.Abs(content.anchoredPosition.y-80)<.1f,"Polling changed scroll position.");
        var malformed=Copy(initial);malformed.teams[0]=null;
        bool rejected=false;try{app.Apply(malformed,true);}catch(ArgumentException){rejected=true;}
        Check(rejected,"Malformed snapshot was accepted.");
        Check(((Snapshot)Field(app,"snapshot")).teams.All(t=>t!=null),"Malformed response replaced valid state.");
        var changed=Copy(initial);changed.cards[2].readiness=99;app.Apply(changed,true);
        Check(((Snapshot)Field(app,"snapshot")).cards[0].id==changed.cards.First(c=>c.readiness==99).id,"Ranking did not change.");
        app.Apply(initial,false);content.anchoredPosition=Vector2.zero;
        var avatar=content.GetComponentsInChildren<UnityEngine.UI.Button>().First(b=>b.name=="Avatar a");avatar.onClick.Invoke();
        Check((string)Field(app,"selectedTeamId")=="a","Avatar did not select team.");
        Check(Resources.Load<AudioClip>("Audio/Click")&&Resources.Load<AudioClip>("Audio/Leader"),"Downloaded audio missing.");
        return new {sameSnapshotStable=true,scrollPreserved=true,invalidSnapshotRejected=true,rankUpdated=true,avatarWorks=true,audioLoaded=true};
    }
}
