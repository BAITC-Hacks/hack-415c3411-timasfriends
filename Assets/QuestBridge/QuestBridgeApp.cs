using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Networking;

namespace QuestBridge
{
    [Serializable] public class Team { public string id, name, initials, stack, description; public int completed; }
    [Serializable] public class Card { public string id, title, description, category; public int readiness; public float clarity; public string[] teamIds; }
    [Serializable] public class Snapshot { public Card[] cards; public Team[] teams; }
    [Serializable] public class ChatRequest { public string message, conversationId; }
    [Serializable] public class ChatReply { public string message, conversationId, aiMode; }

    public sealed class QuestBridgeApp : MonoBehaviour
    {
        [Tooltip("API base URL; empty means offline demo. Never put an AI key here.")]
        public string serverUrl = "";
        [Min(5)] public float pollSeconds = 5;
        static Color Hex(uint c) => new Color32((byte)(c>>16),(byte)(c>>8),(byte)c,255);
        static readonly Color Ink=Hex(0x17212A), Muted=Hex(0x74808B), Paper=Hex(0xF4F6F8), Blue=Hex(0xDDEFFF), Accent=Hex(0x3B91DE), Line=Hex(0xE4E9EE);
        static readonly Color[] TeamColors={Hex(0xDDEBFF),Hex(0xE6DFF9),Hex(0xDCF0E7),Hex(0xFFEAD9),Hex(0xE4E8F0)};
        readonly Dictionary<string,CardView> cards=new();
        readonly List<Texture2D> textures=new();
        readonly List<Sprite> sprites=new();
        readonly List<UnityWebRequest> requests=new();
        readonly Dictionary<string,UnityEngine.UI.Button> filters=new();
        Snapshot snapshot;
        TMP_FontAsset font;
        Sprite rounded, circle;
        RectTransform root, content, right, middle, left, chatContent, detailOverlay;
        UnityEngine.UI.ScrollRect catalogScroll, chatScroll;
        TMP_InputField input;
        TMP_Text connectionText, countText, toastText, activityText, assistantMode;
        UnityEngine.UI.Button sendButton, soundButton, focusButton, demoButton;
        CanvasGroup focusGroup, toastGroup, rightGroup, entrance;
        AudioSource audioSource;
        AudioClip clickClip, riseClip, leaderClip, openClip;
        bool sound=true, focus, sending, onlineSnapshot, demoLive;
        string conversationId="", selectedTeamId, selectedCardId, filter="Все", lastTeamFingerprint="", pendingMessage="";
        float chatHeight, toastUntil, demoTick, introTime;
        int demoVersion;
        int selectionFrame;
        const float RowHeight=206;
        sealed class CardView
        {
            public RectTransform rect, avatars, bar;
            public UnityEngine.UI.Image surface, strip;
            public TMP_Text rank,title,description,score,meta,teamCount;
            public QuestBridgeMotion motion;
            public CanvasGroup group;
            public float targetY, displayedScore, highlight;
            public string teamFingerprint;
            public Card data;
        }
        void Start()
        {
            font=TMP_FontAsset.CreateFontAsset(Resources.Load<Font>("NotoSans-Regular"));font.name="QuestBridge Cyrillic";
            rounded=MakeSprite(false);circle=MakeSprite(true);
            audioSource=gameObject.AddComponent<AudioSource>();audioSource.playOnAwake=false;audioSource.volume=.28f;
            clickClip=Resources.Load<AudioClip>("Audio/Click");riseClip=Resources.Load<AudioClip>("Audio/Rise");leaderClip=Resources.Load<AudioClip>("Audio/Leader");openClip=Resources.Load<AudioClip>("Audio/Open");
            sound=PlayerPrefs.GetInt("QuestBridge.Sound",1)==1;
            Build();Apply(Demo(),false);StartCoroutine(Poll());
        }
        Sprite MakeSprite(bool disc)
        {
            const int n=64;var tex=new Texture2D(n,n,TextureFormat.RGBA32,false);tex.name=disc?"Avatar circle":"Rounded surface";tex.wrapMode=TextureWrapMode.Clamp;tex.filterMode=FilterMode.Bilinear;
            var pixels=new Color[n*n];float radius=disc?31:14;
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                var p=new Vector2(Mathf.Abs(x-31.5f),Mathf.Abs(y-31.5f));var q=new Vector2(Mathf.Max(p.x-(31-radius),0),Mathf.Max(p.y-(31-radius),0));
                pixels[y*n+x]=new Color(1,1,1,Mathf.Clamp01(radius-q.magnitude+.4f));
            }
            tex.SetPixels(pixels);tex.Apply();textures.Add(tex);
            var s=Sprite.Create(tex,new Rect(0,0,n,n),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,disc?Vector4.zero:new Vector4(17,17,17,17));sprites.Add(s);return s;
        }
        RectTransform Rect(Transform p,string name,float x,float y,float w,float h)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(p,false);r.anchorMin=new Vector2(x,y);r.anchorMax=new Vector2(x+w,y+h);r.offsetMin=r.offsetMax=Vector2.zero;return r;
        }
        RectTransform Surface(Transform p,string name,float x,float y,float w,float h,Color color,bool round=true,bool hit=false)
        {
            var r=Rect(p,name,x,y,w,h);var img=r.gameObject.AddComponent<UnityEngine.UI.Image>();img.color=color;img.raycastTarget=hit;
            if(round){img.sprite=rounded;img.type=UnityEngine.UI.Image.Type.Sliced;}return r;
        }
        TMP_Text Text(Transform p,string value,float x,float y,float w,float h,int size=18,bool bold=false,Color? color=null)
        {
            var r=Rect(p,"Text",x,y,w,h);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=size;t.color=color??Ink;t.richText=false;t.raycastTarget=false;t.characterSpacing=-.4f;
            t.enableAutoSizing=false;t.fontStyle=bold?FontStyles.Bold:FontStyles.Normal;t.verticalAlignment=VerticalAlignmentOptions.Middle;t.overflowMode=TextOverflowModes.Ellipsis;return t;
        }
        UnityEngine.UI.Button Button(Transform p,string title,float x,float y,float w,float h,Action action,Color? fill=null,bool dark=false)
        {
            Color normal=fill??(dark?Ink:Blue);var r=Surface(p,title,x,y,w,h,normal,true,true);
            var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=r.GetComponent<UnityEngine.UI.Image>();b.transition=UnityEngine.UI.Selectable.Transition.None;
            Text(r,title,.06f,0,.88f,1,16,true,dark?Color.white:Ink).alignment=TextAlignmentOptions.Center;
            var motion=r.gameObject.AddComponent<QuestBridgeMotion>();motion.surface=r.GetComponent<UnityEngine.UI.Image>();motion.resting=normal;motion.hovered=Color.Lerp(normal,dark?Accent:Color.white,.18f);
            b.onClick.AddListener(()=>{Play(clickClip);motion.Pulse(.025f);action();});return b;
        }
        RectTransform Avatar(Transform p,Team team,float x,float y,float diameter,Action action=null)
        {
            var r=Rect(p,"Avatar "+team.id,x,y,0,0);r.sizeDelta=new Vector2(diameter,diameter);
            var img=r.gameObject.AddComponent<UnityEngine.UI.Image>();img.sprite=circle;img.color=Color.white;img.raycastTarget=action!=null;
            var face=Surface(r,"Face",.065f,.065f,.87f,.87f,TeamColor(team.id),false,action!=null);face.GetComponent<UnityEngine.UI.Image>().sprite=circle;
            Text(face,string.IsNullOrWhiteSpace(team.initials)?"?":team.initials,0,0,1,1,diameter>65?28:12,true).alignment=TextAlignmentOptions.Center;
            if(action!=null){var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=img;b.transition=UnityEngine.UI.Selectable.Transition.None;b.onClick.AddListener(()=>{Play(openClip);action();});r.gameObject.AddComponent<QuestBridgeMotion>().hoverScale=1.12f;}return r;
        }
        static Color TeamColor(string id){int v=0;foreach(var c in id??"")v=(v+c)%TeamColors.Length;return TeamColors[v];}
        void Rule(Transform p,float y)=>Surface(p,"Rule",0,y,1,.0016f,Line,false);
        RectTransform CreateScroll(Transform parent,string name,float x,float y,float w,float h,out UnityEngine.UI.ScrollRect scroll)
        {
            var r=Rect(parent,name,x,y,w,h);scroll=r.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            var vp=Surface(r,"Viewport",0,0,1,1,new Color(1,1,1,.004f),false,true);vp.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var c=Rect(vp,"Content",0,1,1,0);c.pivot=new Vector2(.5f,1);
            scroll.viewport=vp;scroll.content=c;scroll.horizontal=false;scroll.vertical=true;scroll.inertia=true;scroll.decelerationRate=.055f;scroll.scrollSensitivity=42;scroll.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped;return c;
        }
        void Build()
        {
            root=Rect(transform,"QuestBridge Canvas",0,0,1,1);var canvas=root.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.pixelPerfect=true;
            var scaler=root.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=1;
            root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();if(!FindFirstObjectByType<EventSystem>())new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
            Surface(root,"Page",0,0,1,1,Color.white,false);entrance=root.gameObject.AddComponent<CanvasGroup>();entrance.alpha=0;
            var brand=Surface(root,"Brand",.023f,.921f,.026f,.046f,Ink);Text(brand,"q",0,0,1,1,26,true,Color.white).alignment=TextAlignmentOptions.Center;
            Text(root,"questbridge",.059f,.918f,.20f,.052f,29,true);Text(root,"Задачи, которые находят команду",.305f,.928f,.40f,.032f,16,false,Muted);
            soundButton=Button(root,sound?"Звук вкл":"Звук выкл",.838f,.929f,.095f,.037f,()=>{sound=!sound;PlayerPrefs.SetInt("QuestBridge.Sound",sound?1:0);SetButtonText(soundButton,sound?"Звук вкл":"Звук выкл");if(sound)Play(clickClip);},Paper);
            Avatar(root,new Team{id="me",initials="TF"},.96f,.947f,38);Surface(root,"Header line",.023f,.902f,.954f,.0015f,Ink,false);
            left=Surface(root,"Assistant panel",.022f,.062f,.261f,.817f,Paper);middle=Rect(root,"Catalog panel",.305f,.062f,.438f,.817f);right=Rect(root,"Profile panel",.766f,.062f,.212f,.817f);
            Surface(root,"Right divider",.754f,.062f,.0008f,.817f,Line,false);
            var spark=Surface(left,"Assistant mark",.055f,.911f,.10f,.053f,Ink);Text(spark,"+",0,0,1,1,22,true,Color.white).alignment=TextAlignmentOptions.Center;
            Text(left,"Помощник",.19f,.925f,.58f,.055f,23,true);assistantMode=Text(left,string.IsNullOrWhiteSpace(serverUrl)?"Демо-режим":"AI-помощник",.19f,.892f,.58f,.030f,13,false,Muted);
            Surface(left,"Chat divider",.055f,.862f,.89f,.0015f,Line,false);
            chatContent=CreateScroll(left,"Conversation",.055f,.32f,.89f,.52f,out chatScroll);
            AddMessage("Что хотите улучшить в своём бизнесе?",false,false);AddMessage("Опишите задачу своими словами. Я помогу уточнить детали.",false,false);
            var field=Surface(left,"Draft input",.055f,.104f,.89f,.182f,Color.white,true,true);input=field.gameObject.AddComponent<TMP_InputField>();input.lineType=TMP_InputField.LineType.MultiLineNewline;input.characterLimit=3000;
            var textArea=Rect(field,"Text viewport",.055f,.1f,.89f,.8f);textArea.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var typed=Text(textArea,"",0,0,1,1,18);typed.verticalAlignment=VerticalAlignmentOptions.Top;input.textViewport=textArea;input.textComponent=(TextMeshProUGUI)typed;
            var placeholder=Text(textArea,"Опишите вашу задачу…",0,0,1,1,18,false,Muted);placeholder.verticalAlignment=VerticalAlignmentOptions.Top;input.placeholder=placeholder;
            sendButton=Button(left,"Отправить  ↑",.055f,.036f,.89f,.051f,()=>{if(!sending)StartCoroutine(Chat());},null,true);
            focusButton=Button(left,"Фокус",.69f,.302f,.255f,.036f,ToggleFocus,Color.white);
            Text(middle,"Каталог задач",0,.917f,.7f,.075f,35,true);
            var live=Surface(middle,"Live dot",.813f,.95f,.012f,.015f,Accent,false);live.GetComponent<UnityEngine.UI.Image>().sprite=circle;Text(middle,"LIVE",.84f,.935f,.16f,.04f,14,true,Accent);
            countText=Text(middle,"",0,.867f,.90f,.044f,16,false,Muted);
            filters["Все"]=Button(middle,"Все",0,.800f,.145f,.045f,()=>SetFilter("Все"),Ink,true);
            filters["Образование"]=Button(middle,"Образование",.165f,.800f,.275f,.045f,()=>SetFilter("Образование"),Paper);
            filters["Бизнес"]=Button(middle,"Бизнес",.46f,.800f,.18f,.045f,()=>SetFilter("Бизнес"),Paper);
            demoButton=Button(middle,"Демо",.80f,.800f,.20f,.045f,()=>{demoLive=!demoLive;demoTick=Time.unscaledTime+5;SetButtonText(demoButton,demoLive?"Пауза":"Демо");if(demoLive)DemoLeader();else connectionText.text="ДЕМО · события на паузе";},Blue);demoButton.gameObject.SetActive(string.IsNullOrWhiteSpace(serverUrl));
            content=CreateScroll(middle,"Task list",-.006f,.069f,1.012f,.702f,out catalogScroll);activityText=Text(middle,"Готовность задачи определяет её место",0,.008f,1,.043f,14,false,Muted);
            var veil=Surface(middle,"Focus veil",-.008f,0,1.016f,1,new Color(1,1,1,.96f));focusGroup=veil.gameObject.AddComponent<CanvasGroup>();focusGroup.alpha=0;focusGroup.blocksRaycasts=false;focusGroup.interactable=false;
            Text(veil,"Сосредоточьтесь на идее",.1f,.48f,.8f,.07f,26,true).alignment=TextAlignmentOptions.Center;Text(veil,"Каталог подождёт",.1f,.425f,.8f,.045f,17,false,Muted).alignment=TextAlignmentOptions.Center;
            Button(veil,"Вернуться в каталог",.20f,.31f,.60f,.078f,()=>{if(focus)ToggleFocus();},null,true);
            rightGroup=right.gameObject.AddComponent<CanvasGroup>();ShowTeam(null,null,false);
            connectionText=Text(root,string.IsNullOrWhiteSpace(serverUrl)?"ДЕМО · пример данных":"Подключение…",.025f,.017f,.5f,.025f,12,false,Muted);Text(root,"TimasFriends / HackAlem",.79f,.017f,.19f,.025f,12,false,Muted).alignment=TextAlignmentOptions.Right;
            var toast=Surface(root,"Event toast",.36f,.855f,.38f,.056f,Ink);toastGroup=toast.gameObject.AddComponent<CanvasGroup>();toastGroup.alpha=0;toastGroup.blocksRaycasts=false;toastText=Text(toast,"",.045f,0,.91f,1,16,true,Color.white);
        }
        static void SetButtonText(UnityEngine.UI.Button b,string value)=>b.GetComponentInChildren<TMP_Text>().text=value;
        void ToggleFocus(){focus=!focus;SetButtonText(focusButton,focus?"Вернуться":"Фокус");}
        void SetFilter(string value)
        {
            filter=value;
            foreach(var pair in filters)
            {
                bool chosen=pair.Key==value;var motion=pair.Value.GetComponent<QuestBridgeMotion>();
                motion.resting=chosen?Ink:Paper;motion.hovered=chosen?Hex(0x293D50):Color.white;
                pair.Value.GetComponentInChildren<TMP_Text>().color=chosen?Color.white:Ink;
            }
            Arrange(false);Notify(value=="Все"?"Все опубликованные задачи":value);
        }
        void Update()
        {
            if(!focusGroup)return;float dt=Time.unscaledDeltaTime;introTime+=dt;entrance.alpha=Mathf.Clamp01(introTime/.38f);float blend=1-Mathf.Exp(-10*dt);
            focusGroup.alpha=Mathf.Lerp(focusGroup.alpha,focus?1:0,blend);focusGroup.blocksRaycasts=focus;focusGroup.interactable=focus;focusGroup.GetComponent<UnityEngine.UI.Image>().raycastTarget=focus;
            rightGroup.alpha=Mathf.Lerp(rightGroup.alpha,focus?.16f:1,blend);rightGroup.interactable=!focus;rightGroup.blocksRaycasts=!focus;
            toastGroup.alpha=Mathf.Lerp(toastGroup.alpha,!focus&&Time.unscaledTime<toastUntil?1:0,blend);
            foreach(var v in cards.Values)
            {
                v.rect.anchoredPosition=Vector2.Lerp(v.rect.anchoredPosition,new Vector2(0,v.targetY),1-Mathf.Exp(-8*dt));v.displayedScore=Mathf.MoveTowards(v.displayedScore,v.data.readiness,100*dt);v.score.text=Mathf.RoundToInt(v.displayedScore).ToString();v.bar.anchorMax=new Vector2(Mathf.Clamp01(v.displayedScore/100f),1);
                v.highlight=Mathf.Max(0,v.highlight-dt*.55f);v.group.alpha=Mathf.MoveTowards(v.group.alpha,1,dt*3.5f);v.strip.color=v.highlight>0&&!focus?new Color(Accent.r,Accent.g,Accent.b,v.highlight):Color.clear;
            }
            if(demoLive&&Time.unscaledTime>=demoTick&&!focus&&string.IsNullOrWhiteSpace(serverUrl)){demoTick=Time.unscaledTime+5;DemoLeader();}
        }
        void LateUpdate()
        {
            if(!root)return;
            if(UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame==true)
            {
                if(detailOverlay)CloseDetails();else if(focus)ToggleFocus();else if(selectedTeamId!=null)ShowTeam(null,null,false);
                return;
            }
            var pointer=UnityEngine.InputSystem.Pointer.current;
            if(selectedTeamId!=null&&!detailOverlay&&!focus&&Time.frameCount!=selectionFrame&&pointer!=null&&pointer.press.wasPressedThisFrame)
            {
                var canvas=root.GetComponent<Canvas>();var cam=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
                if(!RectTransformUtility.RectangleContainsScreenPoint(right,pointer.position.ReadValue(),cam))ShowTeam(null,null,false);
            }
        }
        CardView CreateCard(Card data,int rank,bool animate)
        {
            var r=Surface(content,"Task "+data.id,.01f,1,.98f,0,Line,true,true);r.pivot=new Vector2(.5f,1);r.sizeDelta=new Vector2(0,RowHeight-16);
            var inner=Surface(r,"Card surface",0,0,1,1,Color.white,true,true);inner.offsetMin=new Vector2(1,1);inner.offsetMax=new Vector2(-1,-1);
            var v=new CardView{rect=r,data=data,surface=inner.GetComponent<UnityEngine.UI.Image>()};v.group=r.gameObject.AddComponent<CanvasGroup>();v.group.alpha=animate?0:1;
            v.motion=r.gameObject.AddComponent<QuestBridgeMotion>();v.motion.scaleOnHover=false;v.motion.surface=v.surface;v.motion.resting=Color.white;v.motion.hovered=Hex(0xF6FAFE);
            var open=r.gameObject.AddComponent<UnityEngine.UI.Button>();open.targetGraphic=v.surface;open.transition=UnityEngine.UI.Selectable.Transition.None;open.onClick.AddListener(()=>{Play(openClip);OpenDetails(v.data);});
            var number=Surface(inner,"Rank badge",.027f,.68f,.080f,.22f,Paper);v.rank=Text(number,"",0,0,1,1,19,true);v.rank.alignment=TextAlignmentOptions.Center;
            v.meta=Text(inner,"",.133f,.79f,.65f,.14f,12,false,Muted);v.title=Text(inner,"",.133f,.51f,.645f,.29f,24,true);v.description=Text(inner,"",.133f,.31f,.65f,.20f,15,false,Muted);
            v.score=Text(inner,"",.81f,.52f,.16f,.38f,39,true);v.score.alignment=TextAlignmentOptions.Right;Text(inner,"готовность",.795f,.41f,.18f,.12f,11,false,Muted).alignment=TextAlignmentOptions.Right;
            var track=Surface(inner,"Progress track",.813f,.31f,.15f,.018f,Line);v.bar=Surface(track,"Progress",0,0,1,1,Accent);
            Surface(inner,"Divider",.133f,.26f,.827f,.005f,Line,false);v.teamCount=Text(inner,"",.133f,.06f,.42f,.17f,13,false,Muted);v.avatars=Rect(inner,"Applicant avatars",.60f,.15f,.34f,0);
            v.strip=Surface(inner,"Rise highlight",.003f,.1f,.005f,.8f,Color.clear).GetComponent<UnityEngine.UI.Image>();v.displayedScore=data.readiness;r.anchoredPosition=new Vector2(0,-rank*RowHeight+(animate?85:0));return v;
        }
        public void Apply(Snapshot next,bool animate)
        {
            Validate(next);var sorted=next.cards.OrderByDescending(c=>c.readiness).ThenByDescending(c=>c.clarity).ThenBy(c=>c.id,StringComparer.Ordinal).ToArray();
            string oldLeader=snapshot?.cards.FirstOrDefault()?.id;var previous=snapshot?.cards.ToDictionary(c=>c.id,c=>c.readiness)??new Dictionary<string,int>();next.cards=sorted;snapshot=next;
            foreach(var id in cards.Keys.Except(sorted.Select(c=>c.id)).ToArray()){cards[id].rect.gameObject.SetActive(false);Destroy(cards[id].rect.gameObject);cards.Remove(id);}
            for(int i=0;i<sorted.Length;i++)
            {
                var card=sorted[i];if(!cards.TryGetValue(card.id,out var v)){v=CreateCard(card,i,animate);cards.Add(card.id,v);}
                v.data=card;v.title.text=card.title;v.description.text=card.description;v.rank.text=(i+1).ToString("00");v.meta.text=(i==0?"ЛИДЕР  /  ":"")+(card.category??"Задача").ToUpperInvariant();
                v.rank.transform.parent.GetComponent<UnityEngine.UI.Image>().color=i==0?Ink:Paper;v.rank.color=i==0?Color.white:Ink;v.motion.resting=i==0?Hex(0xEFF7FF):Color.white;v.motion.hovered=i==0?Hex(0xE5F2FF):Hex(0xF6FAFE);
                var ids=(card.teamIds??Array.Empty<string>()).Distinct().ToArray();string fp=string.Join("|",ids.Select(id=>id+":"+next.teams.First(t=>t.id==id).initials));
                if(v.teamFingerprint!=fp)
                {
                    v.teamFingerprint=fp;Clear(v.avatars);var applicants=ids.Select(id=>next.teams.First(t=>t.id==id)).ToArray();
                    for(int j=0;j<Math.Min(4,applicants.Length);j++){var team=applicants[j];var avatar=Avatar(v.avatars,team,0,0,40,()=>SelectTeam(team.id,card.id));avatar.anchoredPosition=new Vector2(j*30+20,0);}
                    v.teamCount.text=applicants.Length==0?"Откликов пока нет":"Отклики: "+applicants.Length;
                }
                if(animate&&previous.TryGetValue(card.id,out var old)&&old!=card.readiness){v.highlight=1;if(!focus)v.motion.Pulse(.012f);}
            }
            Arrange(!animate);
            if(selectedTeamId!=null)
            {
                var team=next.teams.FirstOrDefault(t=>t.id==selectedTeamId);var card=next.cards.FirstOrDefault(t=>t.id==selectedCardId);
                if(card==null||!(card.teamIds??Array.Empty<string>()).Contains(selectedTeamId)){team=null;card=null;}
                string fp=JsonUtility.ToJson(team)+JsonUtility.ToJson(card);if(fp!=lastTeamFingerprint)ShowTeam(team,card,false);
            }
            if(animate&&oldLeader!=null&&sorted.Length>0&&oldLeader!=sorted[0].id){var leader=cards[sorted[0].id];leader.highlight=1;if(!focus){leader.motion.Pulse(.035f);Play(leaderClip??riseClip);}Notify("↑  Новый лидер — "+sorted[0].title);}
            else if(animate&&sorted.Any(c=>!previous.ContainsKey(c.id))){Notify("Новая задача в каталоге");if(!focus)Play(riseClip);}
        }
        static void Validate(Snapshot s)
        {
            if(s?.cards==null||s.teams==null||s.cards.Length>500||s.teams.Length>1000)throw new ArgumentException("Invalid catalog");var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var t in s.teams)if(t==null||string.IsNullOrWhiteSpace(t.id)||!ids.Add(t.id)||t.completed<0)throw new ArgumentException("Invalid team");
            var cardIds=new HashSet<string>(StringComparer.Ordinal);
            foreach(var c in s.cards)if(c==null||string.IsNullOrWhiteSpace(c.id)||!cardIds.Add(c.id)||string.IsNullOrWhiteSpace(c.title)||c.readiness<0||c.readiness>100||float.IsNaN(c.clarity)||float.IsInfinity(c.clarity)||c.clarity<0||c.clarity>10||(c.teamIds??Array.Empty<string>()).Any(id=>id==null||!ids.Contains(id)))throw new ArgumentException("Invalid task");
        }
        void Arrange(bool immediate)
        {
            int index=0;foreach(var card in snapshot.cards){var v=cards[card.id];bool visible=filter=="Все"||card.category==filter;v.rect.gameObject.SetActive(visible);if(!visible)continue;v.targetY=-index++*RowHeight;if(immediate)v.rect.anchoredPosition=new Vector2(0,v.targetY);}
            content.sizeDelta=new Vector2(0,index*RowHeight);var pos=content.anchoredPosition;pos.y=Mathf.Clamp(pos.y,0,Mathf.Max(0,content.rect.height-catalogScroll.viewport.rect.height));content.anchoredPosition=pos;countText.text=index+" задач  ·  Сначала самые готовые";
        }
        void SelectTeam(string teamId,string cardId){var team=snapshot.teams.FirstOrDefault(t=>t.id==teamId);var card=snapshot.cards.FirstOrDefault(c=>c.id==cardId);if(team!=null)ShowTeam(team,card,true);}
        static void Clear(Transform p){foreach(Transform child in p){child.gameObject.SetActive(false);Destroy(child.gameObject);}}
        void ShowTeam(Team team,Card card,bool animate)
        {
            Clear(right);selectedTeamId=team?.id;selectedCardId=card?.id;selectionFrame=Time.frameCount;lastTeamFingerprint=JsonUtility.ToJson(team)+JsonUtility.ToJson(card);Text(right,"Команда",.015f,.929f,.75f,.054f,23,true);
            if(team==null)
            {
                var art=Surface(right,"Team preview",.02f,.57f,.96f,.28f,Paper);Avatar(art,new Team{id="a",initials="TF"},.35f,.57f,74).anchoredPosition=new Vector2(-24,10);Avatar(art,new Team{id="b",initials="DP"},.56f,.39f,65).anchoredPosition=new Vector2(12,-7);Avatar(art,new Team{id="c",initials="NS"},.26f,.25f,57);
                Text(right,"Кто возьмётся\nза вашу задачу?",.02f,.37f,.96f,.15f,27,true);Text(right,"Выберите аватар у карточки",.02f,.29f,.96f,.055f,16,false,Muted);Rule(right,.23f);Text(right,"Навыки, опыт и предложения —\nвсё о команде в одном месте.",.02f,.10f,.96f,.10f,15,false,Muted);return;
            }
            Button(right,"×",.83f,.937f,.15f,.042f,()=>ShowTeam(null,null,true),Paper);var banner=Surface(right,"Team identity",.015f,.61f,.97f,.26f,TeamColor(team.id));Avatar(banner,team,.5f,.63f,82);Text(banner,team.name,.06f,.10f,.88f,.22f,25,true).alignment=TextAlignmentOptions.Center;
            Text(right,team.description,.015f,.46f,.97f,.12f,17,false,Muted);Text(right,"НАВЫКИ",.015f,.39f,.97f,.04f,12,true,Muted);Text(right,team.stack,.015f,.32f,.97f,.065f,18,true);Rule(right,.28f);
            Text(right,team.completed.ToString("00"),.015f,.16f,.27f,.10f,42,true);Text(right,"подтверждённых\nэтапов",.31f,.17f,.66f,.08f,15,false,Muted);
            if(card!=null){Text(right,"ОТКЛИК НА ЗАДАЧУ",.015f,.10f,.97f,.035f,11,true,Muted);Button(right,card.title,.015f,.012f,.97f,.075f,()=>OpenDetails(card),Paper);}
            if(animate){right.localScale=Vector3.one*.975f;StartCoroutine(RevealProfile());}
        }
        IEnumerator RevealProfile(){for(float t=0;t<.25f;t+=Time.unscaledDeltaTime){right.localScale=Vector3.Lerp(Vector3.one*.975f,Vector3.one,Mathf.SmoothStep(0,1,t/.25f));yield return null;}right.localScale=Vector3.one;}
        void OpenDetails(Card card)
        {
            CloseDetails();detailOverlay=Rect(root,"Task detail",0,0,1,1);
            var backdrop=Surface(detailOverlay,"Dismiss backdrop",0,0,1,1,new Color(.06f,.10f,.15f,.32f),false,true);
            var dismiss=backdrop.gameObject.AddComponent<UnityEngine.UI.Button>();dismiss.targetGraphic=backdrop.GetComponent<UnityEngine.UI.Image>();dismiss.transition=UnityEngine.UI.Selectable.Transition.None;dismiss.onClick.AddListener(CloseDetails);
            var pane=Surface(detailOverlay,"Detail pane",.24f,.14f,.52f,.72f,Color.white,true,true);
            Text(pane,(card.category??"Задача").ToUpperInvariant(),.07f,.85f,.73f,.05f,13,true,Muted);Text(pane,card.title,.07f,.65f,.76f,.18f,35,true);Button(pane,"×",.88f,.85f,.07f,.065f,CloseDetails,Paper);
            var body=CreateScroll(pane,"Description",.07f,.31f,.86f,.31f,out var bodyScroll);var description=Text(body,card.description,0,0,1,1,22);description.verticalAlignment=VerticalAlignmentOptions.Top;description.overflowMode=TextOverflowModes.Overflow;
            Canvas.ForceUpdateCanvases();body.sizeDelta=new Vector2(0,Mathf.Max(150,description.GetPreferredValues(card.description,bodyScroll.viewport.rect.width,0).y+24));Surface(pane,"Rule",.07f,.27f,.86f,.002f,Line,false);
            Text(pane,card.readiness+" / 100",.07f,.14f,.42f,.10f,34,true);Text(pane,"Готовность",.07f,.09f,.42f,.045f,14,false,Muted);Text(pane,card.clarity.ToString("0.0")+" / 10",.55f,.14f,.38f,.10f,34,true);Text(pane,"Ясность по оценке ИИ",.55f,.09f,.38f,.045f,14,false,Muted);
            StartCoroutine(FadeBubble(pane.gameObject.AddComponent<CanvasGroup>()));
        }
        void CloseDetails(){if(!detailOverlay)return;detailOverlay.gameObject.SetActive(false);Destroy(detailOverlay.gameObject);detailOverlay=null;}
        void Notify(string message){activityText.text=message;toastText.text=message;toastUntil=Time.unscaledTime+3.7f;}
        void AddMessage(string message,bool user,bool animate=true)
        {
            Canvas.ForceUpdateCanvases();float width=Mathf.Max(210,chatContent.rect.width);var bubble=Surface(chatContent,user?"Your message":"Assistant message",0,1,1,0,user?Blue:Color.white);bubble.pivot=new Vector2(.5f,1);
            var label=Text(bubble,message,.055f,0,.89f,1,18);label.overflowMode=TextOverflowModes.Overflow;float height=Mathf.Max(54,label.GetPreferredValues(message,width*.89f,0).y+30);bubble.sizeDelta=new Vector2(0,height);bubble.anchoredPosition=new Vector2(0,-chatHeight);chatHeight+=height+14;chatContent.sizeDelta=new Vector2(0,chatHeight);
            Canvas.ForceUpdateCanvases();chatScroll.verticalNormalizedPosition=0;if(animate)StartCoroutine(FadeBubble(bubble.gameObject.AddComponent<CanvasGroup>()));
        }
        IEnumerator FadeBubble(CanvasGroup g){g.alpha=0;for(float t=0;t<.22f;t+=Time.unscaledDeltaTime){if(!g)yield break;g.alpha=t/.22f;yield return null;}if(g)g.alpha=1;}
        bool ValidBaseUrl()=>Uri.TryCreate(serverUrl,UriKind.Absolute,out var u)&&(u.Scheme=="http"||u.Scheme=="https");
        IEnumerator Poll()
        {
            while(true)
            {
                float nextPoll=Time.unscaledTime+Mathf.Max(5,pollSeconds);
                if(!string.IsNullOrWhiteSpace(serverUrl))
                {
                    demoLive=false;demoButton.gameObject.SetActive(false);if(!ValidBaseUrl()){connectionText.text="Проверьте адрес сервера";yield return new WaitForSecondsRealtime(5);continue;}
                    using(var req=UnityWebRequest.Get(serverUrl.TrimEnd('/')+"/api/catalog"))
                    {
                        requests.Add(req);req.timeout=8;yield return req.SendWebRequest();requests.Remove(req);
                        if(req.result==UnityWebRequest.Result.Success)
                        {
                            try{Apply(JsonUtility.FromJson<Snapshot>(req.downloadHandler.text),onlineSnapshot);onlineSnapshot=true;connectionText.text="Подключено · обновление каждые "+Mathf.Max(5,pollSeconds).ToString("0")+" с";}
                            catch(ArgumentException){connectionText.text=onlineSnapshot?"Ошибка данных · сохранена последняя версия":"Ошибка данных · показано демо";}
                        }
                        else connectionText.text=onlineSnapshot?"Нет связи · сохранена последняя версия":"Нет связи · показано демо";
                    }
                }
                yield return new WaitForSecondsRealtime(Mathf.Max(.1f,nextPoll-Time.unscaledTime));
            }
        }
        IEnumerator Chat()
        {
            string message=input.text.Trim();if(message.Length==0)yield break;if(!string.IsNullOrWhiteSpace(serverUrl)&&!ValidBaseUrl()){AddMessage("Проверьте адрес сервера. Ваш текст сохранён.",false);yield break;}
            sending=true;sendButton.interactable=false;SetButtonText(sendButton,"Думаю…");input.interactable=false;
            if(pendingMessage!=message)AddMessage(message,true);pendingMessage=message;bool success=false;
            if(string.IsNullOrWhiteSpace(serverUrl))
            {
                yield return new WaitForSecondsRealtime(.55f);AddMessage("Демо-подсказка:\n\n1. Кто будет пользоваться решением?\n2. Какие данные у вас уже есть?\n3. Как поймёте, что задача решена?",false);assistantMode.text="Демо · без подключения к ИИ";success=true;
            }
            else
            {
                using(var req=new UnityWebRequest(serverUrl.TrimEnd('/')+"/api/chat","POST"))
                {
                    req.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new ChatRequest{message=message,conversationId=conversationId})));req.downloadHandler=new DownloadHandlerBuffer();req.SetRequestHeader("Content-Type","application/json");req.timeout=25;requests.Add(req);yield return req.SendWebRequest();requests.Remove(req);
                    if(req.result==UnityWebRequest.Result.Success)
                    {
                        ChatReply reply=null;try{reply=JsonUtility.FromJson<ChatReply>(req.downloadHandler.text);if(reply==null||string.IsNullOrWhiteSpace(reply.message)||reply.message.Length>12000||string.IsNullOrWhiteSpace(reply.conversationId))throw new ArgumentException();}catch(ArgumentException){reply=null;}
                        if(reply!=null){conversationId=reply.conversationId;AddMessage(reply.message,false);assistantMode.text=reply.aiMode=="fallback"?"Резервные подсказки":"AI-помощник";success=true;}
                    }
                    if(!success)AddMessage("Не удалось получить ответ. Текст сохранён — попробуйте ещё раз.\n\nПока уточните пользователей, доступные данные и критерии успеха.",false);
                }
            }
            if(success){input.text="";pendingMessage="";}input.interactable=true;sending=false;sendButton.interactable=true;SetButtonText(sendButton,"Отправить  ↑");if(success)Play(riseClip);
        }
        void DemoLeader(){if(!string.IsNullOrWhiteSpace(serverUrl))return;var next=Demo();demoVersion++;next.cards[demoVersion%next.cards.Length].readiness=99;Apply(next,true);connectionText.text="ДЕМО · события каждые 5 с";}
        void Play(AudioClip clip){if(sound&&!focus&&clip)audioSource.PlayOneShot(clip);}
        void OnDestroy()
        {
            foreach(var req in requests){req.Abort();req.Dispose();}requests.Clear();
            if(font){if(font.material)Destroy(font.material);foreach(var texture in font.atlasTextures)if(texture)Destroy(texture);Destroy(font);}foreach(var s in sprites)Destroy(s);foreach(var t in textures)Destroy(t);
        }
        static Snapshot Demo()
        {
            var teams=new[]{new Team{id="a",name="TimasFriends",initials="TF",stack="Unity · Python · AI",completed=3,description="Делаем сложное понятным. Интерактивные продукты для образования."},new Team{id="b",name="Data People",initials="DP",stack="Python · SQL · ML",completed=7,description="Превращаем данные бизнеса в решения, которые можно измерить."},new Team{id="c",name="Next Step",initials="NS",stack="React · UX · Node.js",completed=2,description="Проектируем удобные сервисы и быстро проверяем идеи."},new Team{id="d",name="Orbit",initials="OR",stack="Python · FastAPI",completed=5,description="Автоматизируем повседневные процессы небольших команд."},new Team{id="e",name="Form",initials="FM",stack="Design · Web",completed=1,description="Продуманные интерфейсы для полезных продуктов."}};
            string[] titles={"Проверка SAT без рутины","Меньше списаний в кофейне","Доставка по умному маршруту","Запись на занятия за минуту","Отзывы, которые помогают","Помощник для библиотеки"};
            string[] desc={"Помочь преподавателям быстрее проверять работы и находить пробелы в знаниях.","Планировать закупки на основе продаж и сократить остатки продуктов.","Распределять заказы между курьерами с учётом маршрута и загрузки.","Упростить запись на пробные занятия в учебном центре.","Находить повторяющиеся проблемы в обратной связи клиентов.","Подбирать учебные материалы по теме и уровню подготовки."};
            return new Snapshot{teams=teams,cards=titles.Select((t,i)=>new Card{id="demo-"+i,title=t,description=desc[i],category=i==0||i==3||i==5?"Образование":"Бизнес",readiness=92-i*9,clarity=8-i*.5f,teamIds=i%2==0?new[]{"a","b","c"}:new[]{"d","e"}}).ToArray()};
        }
    }
}
