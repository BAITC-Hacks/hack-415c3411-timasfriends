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
    [Serializable] public class Team { public string id, name, initials, stack, description; public int completed, experience; }
    [Serializable] public class Card { public string id, title, description, category, businessId; public int readiness; public float clarity; public string[] teamIds; public bool demo; }
    [Serializable] public class Snapshot { public Card[] cards; public Team[] teams; public int version; }
    [Serializable] public class ChatRequest { public string message, conversationId; }
    [Serializable] public class ChatReply { public string message, conversationId, aiMode, phase; public DraftData draft; public string[] missingFields; public bool inputAccepted=true; public ContentReview validation; }

    public sealed partial class QuestBridgeApp : MonoBehaviour
    {
        [Tooltip("API base URL; empty means offline demo. Never put an AI key here.")]
        public string serverUrl = "";
        [Min(5)] public float pollSeconds = 5;
        static Color Hex(uint c) => new Color32((byte)(c>>16),(byte)(c>>8),(byte)c,255);
        static readonly Color Ink=Hex(0x17212A), Muted=Hex(0x536170), Paper=Hex(0xF5F7FA), Blue=Hex(0xE4F1FF), Accent=Hex(0x2378CC), Line=Hex(0xE3E8EF);
        static readonly Color[] TeamColors={Hex(0xDDEBFF),Hex(0xE6DFF9),Hex(0xDCF0E7),Hex(0xFFEAD9),Hex(0xE4E8F0)};
        readonly Dictionary<string,CardView> cards=new();
        readonly List<Texture2D> textures=new();
        readonly List<Sprite> sprites=new();
        readonly List<UnityWebRequest> requests=new();
        readonly Dictionary<string,UnityEngine.UI.Button> filters=new();
        readonly List<ChatBubble> chatBubbles=new();
        Snapshot snapshot, lastServerSnapshot, snapshotBeforeDemo;
        TMP_FontAsset font;
        Sprite rounded, circle;
        RectTransform root, content, right, middle, left, chatContent, detailOverlay;
        UnityEngine.UI.ScrollRect catalogScroll, chatScroll;
        TMP_InputField input;
        TMP_Text connectionText, countText, toastText, activityText, assistantMode, draftStatus;
        UnityEngine.UI.Button sendButton, soundButton, focusButton, demoButton, newChatButton;
        CanvasGroup focusGroup, toastGroup, rightGroup, entrance;
        AudioSource audioSource;
        AudioClip clickClip, riseClip, leaderClip, openClip;
        bool sound=true, focus, sending, onlineSnapshot, demoLive;
        string conversationId="", selectedTeamId, selectedCardId, filter="Все", lastTeamFingerprint="", pendingMessage="";
        float chatHeight, toastUntil, demoTick, introTime;
        int demoVersion;
        int selectionFrame;
        int offlineMessages;
        static readonly string[] OfflineQuestionFields={"users","data","successCriteria"};
        static readonly string[] OfflineQuestions={"Кто будет пользоваться решением?","Какие данные, примеры или материалы у вас уже есть?","По какому проверяемому признаку вы поймёте, что задача решена?"};
        UnityEngine.InputSystem.Pointer dismissPointer;
        Vector2 dismissPressPosition;
        float dismissMaxDistance;
        bool dismissStartedOutside;
        string filterBeforeDemo="Все";
        float lastChatWidth;
        const float RowHeight=224;
        sealed class ChatBubble { public RectTransform rect; public TMP_Text label; public bool user; public float top,height; }
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
            Application.runInBackground=true;Application.targetFrameRate=60;
            font=TMP_FontAsset.CreateFontAsset(Resources.Load<Font>("NotoSans-Regular"));font.name="QuestBridge Cyrillic";
            rounded=MakeSprite(false);circle=MakeSprite(true);
            audioSource=gameObject.AddComponent<AudioSource>();audioSource.playOnAwake=false;audioSource.volume=.28f;
            clickClip=Resources.Load<AudioClip>("Audio/Click");riseClip=Resources.Load<AudioClip>("Audio/Rise");leaderClip=Resources.Load<AudioClip>("Audio/Leader");openClip=Resources.Load<AudioClip>("Audio/Open");
            sound=PlayerPrefs.GetInt("QuestBridge.Sound",1)==1;
            gameObject.name="QuestBridge";ConfigureWebAddress();Build();Apply(Demo(),false);InitializeWorkflow();StartCoroutine(Poll());
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
        RectTransform Panel(Transform p,string name,float x,float y,float w,float h)
        {
            var border=Surface(p,name+" border",x,y,w,h,Line);
            var panel=Surface(border,name,0,0,1,1,Color.white);panel.offsetMin=Vector2.one;panel.offsetMax=-Vector2.one;return panel;
        }
        TMP_Text Text(Transform p,string value,float x,float y,float w,float h,int size=18,bool bold=false,Color? color=null)
        {
            var r=Rect(p,"Text",x,y,w,h);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=size;t.color=color??Ink;t.richText=false;t.raycastTarget=false;t.characterSpacing=0;
            t.enableAutoSizing=false;t.fontStyle=bold?FontStyles.Bold:FontStyles.Normal;t.verticalAlignment=VerticalAlignmentOptions.Middle;t.overflowMode=TextOverflowModes.Ellipsis;return t;
        }
        UnityEngine.UI.Button Button(Transform p,string title,float x,float y,float w,float h,Action action,Color? fill=null,bool dark=false)
        {
            Color normal=fill??(dark?Ink:Blue);var r=Surface(p,title,x,y,w,h,normal,true,true);
            var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=r.GetComponent<UnityEngine.UI.Image>();b.transition=UnityEngine.UI.Selectable.Transition.None;b.targetGraphic.CrossFadeColor(Color.white,0,true,true);
            Text(r,title,.06f,0,.88f,1,17,true,dark?Color.white:Ink).alignment=TextAlignmentOptions.Center;
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
            Surface(root,"Page",0,0,1,1,Paper,false);entrance=root.gameObject.AddComponent<CanvasGroup>();entrance.alpha=0;
            Surface(root,"Header",0,.902f,1,.098f,Color.white,false);Surface(root,"Header rule",0,.902f,1,.0012f,Line,false);
            // A bridge made from three aligned shapes, at a fixed logical size.
            var mark=Rect(root,"Bridge mark",.027f,.951f,0,0);mark.sizeDelta=new Vector2(34,34);
            Surface(mark,"Left pillar",0,.10f,.24f,.8f,Ink);Surface(mark,"Right pillar",.76f,.10f,.24f,.8f,Ink);Surface(mark,"Span",.12f,.53f,.76f,.24f,Accent);
            Text(root,"QuestBridge",.057f,.923f,.26f,.055f,27,true);
            var state=Surface(root,"Connection",.698f,.924f,.155f,.051f,Paper);
            connectionText=Text(state,string.IsNullOrWhiteSpace(serverUrl)?"Офлайн-демо":"Подключение…",.07f,0,.86f,1,15,true,Muted);connectionText.alignment=TextAlignmentOptions.Center;
            soundButton=Button(root,sound?"Звук: вкл":"Звук: выкл",.866f,.924f,.108f,.051f,()=>{sound=!sound;PlayerPrefs.SetInt("QuestBridge.Sound",sound?1:0);SetButtonText(soundButton,sound?"Звук: вкл":"Звук: выкл");if(sound)Play(clickClip);},Paper);

            left=Panel(root,"Assistant panel",.025f,.036f,.279f,.839f);
            middle=Rect(root,"Catalog panel",.322f,.036f,.438f,.839f);
            right=Panel(root,"Profile panel",.778f,.036f,.197f,.839f);

            Text(left,"Создать задачу",.055f,.906f,.67f,.068f,25,true);
            newChatButton=Button(left,"Сначала",.76f,.920f,.185f,.047f,ResetConversation,Paper);newChatButton.GetComponentInChildren<TMP_Text>().fontSize=14;
            assistantMode=Text(left,string.IsNullOrWhiteSpace(serverUrl)?"Без ИИ · пошаговый режим":"Чат с помощником",.055f,.855f,.65f,.047f,15,false,Muted);
            focusButton=Button(left,"Фокус",.755f,.855f,.19f,.047f,ToggleFocus,Blue);focusButton.GetComponentInChildren<TMP_Text>().fontSize=14;
            Surface(left,"Chat divider",.055f,.831f,.89f,.0015f,Line,false);
            chatContent=CreateScroll(left,"Conversation",.045f,.358f,.91f,.455f,out chatScroll);
            AddMessage("Какую проблему бизнеса хотите решить? Опишите её своими словами.",false,false);
            draftStatus=Text(left,"Личный черновик",.055f,.309f,.89f,.034f,14,false,Muted);
            var field=Panel(left,"Draft input",.055f,.110f,.89f,.185f);field.GetComponent<UnityEngine.UI.Image>().raycastTarget=true;
            input=field.gameObject.AddComponent<TMP_InputField>();input.lineType=TMP_InputField.LineType.MultiLineNewline;input.characterLimit=3000;
            var textArea=Rect(field,"Text viewport",0,0,1,1);textArea.offsetMin=new Vector2(16,12);textArea.offsetMax=new Vector2(-16,-12);textArea.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var typed=Text(textArea,"",0,0,1,1,18);typed.verticalAlignment=VerticalAlignmentOptions.Top;input.textViewport=textArea;input.textComponent=(TextMeshProUGUI)typed;
            var placeholder=Text(textArea,"Напишите сообщение…",0,0,1,1,18,false,Muted);placeholder.verticalAlignment=VerticalAlignmentOptions.Top;input.placeholder=placeholder;
            QuestBridgeComposer.Configure(input,typed,placeholder,textArea);
            sendButton=Button(left,"Отправить",.055f,.035f,.89f,.059f,()=>{if(!sending)StartCoroutine(Chat());},null,true);

            Text(middle,"Каталог задач",0,.916f,.75f,.071f,32,true);
            countText=Text(middle,"",0,.862f,1,.044f,16,false,Muted);
            filters["Все"]=Button(middle,"Все",0,.779f,.145f,.057f,()=>SetFilter("Все"),Ink,true);
            filters["Образование"]=Button(middle,"Образование",.164f,.779f,.31f,.057f,()=>SetFilter("Образование"),Color.white);
            filters["Бизнес"]=Button(middle,"Бизнес",.491f,.779f,.205f,.057f,()=>SetFilter("Бизнес"),Color.white);
            demoButton=Button(middle,"Демо",.785f,.779f,.215f,.057f,ToggleDemo,Blue);
            content=CreateScroll(middle,"Task list",-.007f,.014f,1.014f,.739f,out catalogScroll);
            // Events have a single temporary toast instead of permanent helper copy.
            var toast=Surface(root,"Event toast",.35f,.820f,.37f,.065f,Ink);toastGroup=toast.gameObject.AddComponent<CanvasGroup>();toastGroup.alpha=0;toastGroup.blocksRaycasts=false;
            toastText=Text(toast,"",.055f,0,.89f,1,17,true,Color.white);activityText=toastText;

            var veil=Surface(middle,"Focus veil",-.012f,0,1.024f,1,new Color(Paper.r,Paper.g,Paper.b,.985f));focusGroup=veil.gameObject.AddComponent<CanvasGroup>();focusGroup.alpha=0;focusGroup.blocksRaycasts=false;focusGroup.interactable=false;
            Text(veil,"Сосредоточьтесь на идее",.08f,.51f,.84f,.085f,28,true).alignment=TextAlignmentOptions.Center;
            Text(veil,"Каталог подождёт",.08f,.45f,.84f,.050f,18,false,Muted).alignment=TextAlignmentOptions.Center;
            Button(veil,"Вернуться в каталог",.17f,.335f,.66f,.081f,()=>{if(focus)ToggleFocus();},null,true);
            rightGroup=right.gameObject.AddComponent<CanvasGroup>();ShowTeam(null,null,false);
        }
        void ResetConversation()
        {
            if(sending||publishing)return;conversationId="";pendingMessage="";offlineMessages=0;chatBubbles.Clear();Clear(chatContent);chatHeight=0;lastChatWidth=0;input.text="";
            manualDraftFields.Clear();currentDraftTask=null;publicationKey="";lastPublicationPayload="";currentDraft=new DraftData();SaveDraft();PlayerPrefs.Save();draftStatus.text="Личный черновик";assistantMode.text=string.IsNullOrWhiteSpace(serverUrl)?"Без ИИ · пошаговый режим":"Чат с помощником";
            AddMessage("Какую проблему бизнеса хотите решить? Опишите её своими словами.",false,false);
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
            if(!focusGroup)return;UpdateWorkflow();float dt=Time.unscaledDeltaTime;introTime+=dt;entrance.alpha=Mathf.Clamp01(introTime/.38f);float blend=1-Mathf.Exp(-10*dt);
            focusGroup.alpha=Mathf.Lerp(focusGroup.alpha,focus?1:0,blend);focusGroup.blocksRaycasts=focus;focusGroup.interactable=focus;focusGroup.GetComponent<UnityEngine.UI.Image>().raycastTarget=focus;
            rightGroup.alpha=Mathf.Lerp(rightGroup.alpha,focus?.16f:1,blend);rightGroup.interactable=!focus;rightGroup.blocksRaycasts=!focus;
            toastGroup.alpha=Mathf.Lerp(toastGroup.alpha,!focus&&Time.unscaledTime<toastUntil?1:0,blend);
            foreach(var v in cards.Values)
            {
                v.rect.anchoredPosition=Vector2.Lerp(v.rect.anchoredPosition,new Vector2(0,v.targetY),1-Mathf.Exp(-8*dt));v.displayedScore=Mathf.MoveTowards(v.displayedScore,v.data.readiness,100*dt);v.score.text=Mathf.RoundToInt(v.displayedScore)+"%";v.bar.anchorMax=new Vector2(Mathf.Clamp01(v.displayedScore/100f),1);
                v.highlight=Mathf.Max(0,v.highlight-dt*.55f);v.group.alpha=Mathf.MoveTowards(v.group.alpha,1,dt*3.5f);v.strip.color=v.highlight>0&&!focus?new Color(Accent.r,Accent.g,Accent.b,v.highlight):Color.clear;
            }
            if(demoLive&&Time.unscaledTime>=demoTick&&!focus){demoTick=Time.unscaledTime+5;DemoLeader();}
        }
        void LateUpdate()
        {
            if(!root)return;
            if(roleOverlay)return;
            if(chatContent&&Mathf.Abs(chatContent.rect.width-lastChatWidth)>1)ReflowChat();
            if(UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame==true)
            {
                if(detailOverlay)CloseDetails();else if(focus)ToggleFocus();else if(selectedTeamId!=null)ShowTeam(null,null,false);
                return;
            }
            var pointer=UnityEngine.InputSystem.Pointer.current;
            var canvas=root.GetComponent<Canvas>();var cam=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
            if(dismissPointer!=null&&(!dismissPointer.added||pointer!=dismissPointer))dismissPointer=null;
            if(pointer!=null&&pointer.press.wasPressedThisFrame)
            {
                dismissPointer=pointer;dismissPressPosition=pointer.position.ReadValue();dismissMaxDistance=0;
                dismissStartedOutside=selectedTeamId!=null&&!detailOverlay&&!focus&&!RectTransformUtility.RectangleContainsScreenPoint(right,dismissPressPosition,cam);
            }
            if(dismissPointer!=null)
            {
                var position=dismissPointer.position.ReadValue();dismissMaxDistance=Mathf.Max(dismissMaxDistance,(position-dismissPressPosition).sqrMagnitude);
                var touch=UnityEngine.InputSystem.Touchscreen.current;
                if(touch!=null&&touch.touches.Count(t=>t.press.isPressed)>1)dismissStartedOutside=false;
                if(dismissPointer.press.wasReleasedThisFrame)
                {
                    float threshold=EventSystem.current?EventSystem.current.pixelDragThreshold:10;
                    if(dismissStartedOutside&&dismissMaxDistance<threshold*threshold&&selectedTeamId!=null&&!detailOverlay&&!focus&&Time.frameCount!=selectionFrame&&!RectTransformUtility.RectangleContainsScreenPoint(right,position,cam))ShowTeam(null,null,false);
                    dismissPointer=null;
                }
            }
        }
        static bool IsExample(Card card)=>card.demo||(card.title??"").StartsWith("ДЕМО:",StringComparison.OrdinalIgnoreCase);
        static string CardTitle(Card card)=>IsExample(card)&&(card.title??"").StartsWith("ДЕМО:",StringComparison.OrdinalIgnoreCase)?card.title.Substring(5).Trim():card.title;
        CardView CreateCard(Card data,int rank,bool animate)
        {
            var r=Surface(content,"Task "+data.id,.007f,1,.986f,0,Line,true,true);r.pivot=new Vector2(.5f,1);r.sizeDelta=new Vector2(0,RowHeight-14);
            var inner=Surface(r,"Card surface",0,0,1,1,Color.white,true,true);inner.offsetMin=Vector2.one;inner.offsetMax=-Vector2.one;
            var v=new CardView{rect=r,data=data,surface=inner.GetComponent<UnityEngine.UI.Image>()};v.group=r.gameObject.AddComponent<CanvasGroup>();v.group.alpha=animate?0:1;
            v.motion=r.gameObject.AddComponent<QuestBridgeMotion>();v.motion.scaleOnHover=false;v.motion.surface=v.surface;v.motion.resting=Color.white;v.motion.hovered=Hex(0xF4F9FF);
            var open=r.gameObject.AddComponent<UnityEngine.UI.Button>();open.targetGraphic=v.surface;open.transition=UnityEngine.UI.Selectable.Transition.None;open.onClick.AddListener(()=>{Play(openClip);OpenDetails(v.data);});
            var number=Surface(inner,"Rank badge",.028f,.716f,.075f,.185f,Paper);v.rank=Text(number,"",0,0,1,1,18,true);v.rank.alignment=TextAlignmentOptions.Center;
            v.meta=Text(inner,"",.128f,.77f,.59f,.155f,14,true,Muted);
            v.title=Text(inner,"",.128f,.43f,.65f,.33f,24,true);
            v.description=Text(inner,"",.128f,.243f,.815f,.18f,16,false,Muted);
            v.score=Text(inner,"",.79f,.635f,.165f,.23f,28,true);v.score.alignment=TextAlignmentOptions.Right;
            Text(inner,"полнота",.78f,.493f,.175f,.15f,14,false,Muted).alignment=TextAlignmentOptions.Right;
            var track=Surface(inner,"Progress track",.802f,.443f,.15f,.014f,Line);v.bar=Surface(track,"Progress",0,0,1,1,Accent);
            Surface(inner,"Divider",.128f,.221f,.824f,.005f,Line,false);
            v.teamCount=Text(inner,"",.128f,.034f,.52f,.158f,14,false,Muted);v.avatars=Rect(inner,"Applicant avatars",.742f,.109f,.21f,0);
            v.strip=Surface(inner,"Rise highlight",.002f,.14f,.004f,.72f,Color.clear).GetComponent<UnityEngine.UI.Image>();v.displayedScore=data.readiness;
            r.anchoredPosition=new Vector2(0,-rank*RowHeight+(animate?65:0));return v;
        }
        public void Apply(Snapshot next,bool animate)
        {
            Validate(next);var sorted=next.cards.OrderByDescending(c=>c.readiness).ThenByDescending(c=>c.clarity).ThenBy(c=>c.id,StringComparer.Ordinal).ToArray();
            string oldLeader=snapshot?.cards.FirstOrDefault()?.id;var previous=snapshot?.cards.ToDictionary(c=>c.id,c=>c.readiness)??new Dictionary<string,int>();next.cards=sorted;snapshot=next;
            foreach(var id in cards.Keys.Except(sorted.Select(c=>c.id)).ToArray()){cards[id].rect.gameObject.SetActive(false);Destroy(cards[id].rect.gameObject);cards.Remove(id);}
            for(int i=0;i<sorted.Length;i++)
            {
                var card=sorted[i];if(!cards.TryGetValue(card.id,out var v)){v=CreateCard(card,i,animate);cards.Add(card.id,v);}
                v.data=card;v.title.text=CardTitle(card);v.description.text=card.description;v.rank.text=(i+1).ToString("00");v.meta.text=(IsExample(card)?"Пример · ":"")+(card.category??"Задача")+" · "+ReadinessName(card.readiness);v.meta.color=IsExample(card)?Accent:Muted;
                v.rank.transform.parent.GetComponent<UnityEngine.UI.Image>().color=i==0?Ink:Paper;v.rank.color=i==0?Color.white:Ink;v.motion.resting=i==0?Hex(0xF0F7FF):Color.white;v.motion.hovered=i==0?Hex(0xE5F2FF):Hex(0xF6FAFE);
                var ids=(card.teamIds??Array.Empty<string>()).Distinct().ToArray();string fp=string.Join("|",ids.Select(id=>id+":"+next.teams.First(t=>t.id==id).initials));
                if(v.teamFingerprint!=fp)
                {
                    v.teamFingerprint=fp;Clear(v.avatars);var applicants=ids.Select(id=>next.teams.First(t=>t.id==id)).ToArray();
                    for(int j=0;j<Math.Min(4,applicants.Length);j++){var team=applicants[j];var avatar=Avatar(v.avatars,team,0,0,40,()=>SelectTeam(team.id,card.id));avatar.anchoredPosition=new Vector2(j*30+20,0);}
                    v.teamCount.text=applicants.Length==0?"Откликов пока нет":"Откликов: "+applicants.Length;
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
            int index=0;foreach(var card in snapshot.cards){var v=cards[card.id];bool visible=(filter=="Все"||card.category==filter)&&WorkflowVisible(card);v.rect.gameObject.SetActive(visible);if(!visible)continue;v.targetY=-index++*RowHeight;if(immediate)v.rect.anchoredPosition=new Vector2(0,v.targetY);}
            content.sizeDelta=new Vector2(0,index*RowHeight);var pos=content.anchoredPosition;pos.y=Mathf.Clamp(pos.y,0,Mathf.Max(0,content.rect.height-catalogScroll.viewport.rect.height));content.anchoredPosition=pos;countText.text=index+(snapshot.cards.All(IsExample)?" примеров":" задач")+"  ·  По полноте описания";
        }
        void SelectTeam(string teamId,string cardId)
        {
            var team=snapshot.teams.FirstOrDefault(t=>t.id==teamId);var card=snapshot.cards.FirstOrDefault(c=>c.id==cardId);
            if(team==null||card==null)return;selectionFrame=Time.frameCount;
            if(selectedTeamId==teamId&&selectedCardId==cardId)return;
            ShowTeam(team,card,true);
        }
        static void Clear(Transform p){foreach(Transform child in p){child.gameObject.SetActive(false);Destroy(child.gameObject);}}
        void ShowTeam(Team team,Card card,bool animate)
        {
            Clear(right);selectedTeamId=team?.id;selectedCardId=card?.id;selectionFrame=Time.frameCount;lastTeamFingerprint=JsonUtility.ToJson(team)+JsonUtility.ToJson(card);
            Text(right,"Команды",.08f,.906f,.74f,.068f,25,true);
            if(team==null)
            {
                Text(right,"Участники задачи",.08f,.851f,.84f,.05f,16,false,Muted);
                Surface(right,"Profile divider",.08f,.831f,.84f,.0015f,Line,false);
                var symbol=Rect(right,"Team symbol",.5f,.63f,0,0);symbol.sizeDelta=new Vector2(100,90);
                var back=Surface(symbol,"Back profile",.44f,.22f,.50f,.66f,Blue);Surface(back,"Head",.31f,.52f,.38f,.30f,Accent);Surface(back,"Shoulders",.19f,.19f,.62f,.20f,Accent);
                var front=Surface(symbol,"Front profile",.06f,.06f,.52f,.70f,Ink);Surface(front,"Head",.32f,.51f,.36f,.28f,Color.white);Surface(front,"Shoulders",.19f,.17f,.62f,.20f,Color.white);
                Text(right,"Найдите свою\nкоманду",.09f,.429f,.82f,.13f,25,true).alignment=TextAlignmentOptions.Center;
                Text(right,"Нажмите на аватар\nпод интересной задачей",.09f,.317f,.82f,.09f,17,false,Muted).alignment=TextAlignmentOptions.Center;
                return;
            }
            Button(right,"×",.79f,.915f,.13f,.052f,()=>ShowTeam(null,null,true),Paper);
            var banner=Surface(right,"Team identity",.08f,.625f,.84f,.215f,Paper);Avatar(banner,team,.5f,.67f,64);
            Text(banner,team.name,.06f,.075f,.88f,.31f,20,true).alignment=TextAlignmentOptions.Center;
            Text(right,team.description,.08f,.447f,.84f,.147f,17,false,Muted);
            Text(right,"Стек",.08f,.384f,.84f,.05f,16,true);Text(right,team.stack,.08f,.309f,.84f,.071f,17);
            Surface(right,"Experience divider",.08f,.286f,.84f,.0015f,Line,false);
            Text(right,team.completed.ToString(),.08f,.164f,.25f,.098f,36,true);
            Text(right,"этапов\nподтверждено",.36f,.168f,.57f,.089f,16,false,Muted);
            if(card!=null)Button(right,"Открыть задачу",.08f,.05f,.84f,.074f,()=>OpenDetails(card),Blue);
            if(animate){right.localScale=Vector3.one*.975f;StartCoroutine(RevealProfile());}
        }
        IEnumerator RevealProfile(){for(float t=0;t<.25f;t+=Time.unscaledDeltaTime){right.localScale=Vector3.Lerp(Vector3.one*.975f,Vector3.one,Mathf.SmoothStep(0,1,t/.25f));yield return null;}right.localScale=Vector3.one;}
        void OpenDetails(Card card)
        {
            if(!string.IsNullOrWhiteSpace(serverUrl)&&!card.id.StartsWith("demo-",StringComparison.Ordinal)){OpenServerTask(card);return;}
            CloseDetails();detailOverlay=Rect(root,"Task detail",0,0,1,1);
            var backdrop=Surface(detailOverlay,"Dismiss backdrop",0,0,1,1,new Color(.06f,.10f,.15f,.32f),false,true);
            var dismiss=backdrop.gameObject.AddComponent<UnityEngine.UI.Button>();dismiss.targetGraphic=backdrop.GetComponent<UnityEngine.UI.Image>();dismiss.transition=UnityEngine.UI.Selectable.Transition.None;dismiss.onClick.AddListener(CloseDetails);
            var pane=Surface(detailOverlay,"Detail pane",.24f,.14f,.52f,.72f,Color.white,true,true);
            Text(pane,(IsExample(card)?"Пример  ·  ":"")+(card.category??"Задача"),.07f,.85f,.73f,.05f,16,true,Accent);Text(pane,CardTitle(card),.07f,.65f,.76f,.18f,32,true);Button(pane,"×",.88f,.85f,.07f,.065f,CloseDetails,Paper);
            var body=CreateScroll(pane,"Description",.07f,.36f,.86f,.27f,out var bodyScroll);var description=Text(body,card.description,0,0,1,1,20);description.verticalAlignment=VerticalAlignmentOptions.Top;description.overflowMode=TextOverflowModes.Overflow;
            Canvas.ForceUpdateCanvases();body.sizeDelta=new Vector2(0,Mathf.Max(150,description.GetPreferredValues(card.description,bodyScroll.viewport.rect.width,0).y+24));Surface(pane,"Rule",.07f,.27f,.86f,.002f,Line,false);
            Text(pane,card.readiness+"%",.07f,.145f,.21f,.10f,34,true);Text(pane,"Полнота описания",.30f,.18f,.63f,.05f,19,true);
            Text(pane,"Баллы за заполненные и подтверждённые поля.",.30f,.115f,.63f,.06f,15,false,Muted);
            if(IsExample(card))Text(pane,"Демонстрационная задача. Ваша переписка хранится отдельно.",.07f,.038f,.86f,.06f,15,false,Muted);
            else if(card.clarity>0)Text(pane,"Ясность формулировки: "+card.clarity.ToString("0.0")+" / 10",.07f,.038f,.86f,.06f,15,false,Muted);
            StartCoroutine(FadeBubble(pane.gameObject.AddComponent<CanvasGroup>()));
        }
        void CloseDetails(){editorOpen=false;editorGeneration++;if(!detailOverlay)return;detailOverlay.gameObject.SetActive(false);Destroy(detailOverlay.gameObject);detailOverlay=null;}
        void Notify(string message){activityText.text=message;toastText.text=message;toastUntil=Time.unscaledTime+3.7f;}
        void AddMessage(string message,bool user,bool animate=true)
        {
            Canvas.ForceUpdateCanvases();
            float oldMax=Mathf.Max(0,chatContent.rect.height-chatScroll.viewport.rect.height);
            bool follow=user||oldMax-chatContent.anchoredPosition.y<50;
            var bubble=Surface(chatContent,user?"Your message":"Assistant message",user?.09f:0,1,user?.91f:.965f,0,user?Blue:Paper);
            bubble.pivot=new Vector2(.5f,1);
            var label=Text(bubble,message,0,0,1,1,18);label.rectTransform.offsetMin=new Vector2(16,14);label.rectTransform.offsetMax=new Vector2(-16,-14);
            label.verticalAlignment=VerticalAlignmentOptions.Top;label.overflowMode=TextOverflowModes.Overflow;
            var item=new ChatBubble{rect=bubble,label=label,user=user};chatBubbles.Add(item);ReflowChat();
            if(follow)
            {
                chatScroll.StopMovement();float max=Mathf.Max(0,chatContent.rect.height-chatScroll.viewport.rect.height);
                float target=!user&&item.height>chatScroll.viewport.rect.height-16?Mathf.Min(item.top,max):max;
                chatContent.anchoredPosition=new Vector2(0,target);
            }
            if(animate)StartCoroutine(FadeBubble(bubble.gameObject.AddComponent<CanvasGroup>()));
        }
        void ReflowChat()
        {
            if(!chatContent||!chatScroll)return;
            float width=chatContent.rect.width;if(width<=32)return;lastChatWidth=width;chatHeight=8;
            float offset=chatContent.anchoredPosition.y;
            foreach(var item in chatBubbles)
            {
                float bubbleWidth=width*(item.user?.91f:.965f);
                item.height=Mathf.Ceil(item.label.GetPreferredValues(item.label.text,bubbleWidth-32,Mathf.Infinity).y)+28;
                item.top=chatHeight;item.rect.sizeDelta=new Vector2(0,item.height);item.rect.anchoredPosition=new Vector2(0,-chatHeight);chatHeight+=item.height+12;
            }
            chatContent.sizeDelta=new Vector2(0,Mathf.Max(chatScroll.viewport.rect.height,chatHeight));
            chatContent.anchoredPosition=new Vector2(0,Mathf.Clamp(offset,0,Mathf.Max(0,chatContent.rect.height-chatScroll.viewport.rect.height)));
        }
        IEnumerator FadeBubble(CanvasGroup g){g.alpha=0;for(float t=0;t<.22f;t+=Time.unscaledDeltaTime){if(!g)yield break;g.alpha=t/.22f;yield return null;}if(g)g.alpha=1;}
        bool ValidBaseUrl()=>Uri.TryCreate(serverUrl,UriKind.Absolute,out var u)&&(u.Scheme=="http"||u.Scheme=="https");
        IEnumerator Poll()
        {
            while(true)
            {
                float nextPoll=Time.unscaledTime+Mathf.Max(5,pollSeconds);
                if(demoLive){yield return new WaitForSecondsRealtime(.5f);continue;}
                if(!string.IsNullOrWhiteSpace(serverUrl))
                {
                    if(!ValidBaseUrl()){connectionText.text="Проверьте адрес сервера";yield return new WaitForSecondsRealtime(5);continue;}
                    using(var req=UnityWebRequest.Get(serverUrl.TrimEnd('/')+"/api/catalog"))
                    {
                        requests.Add(req);req.timeout=8;yield return req.SendWebRequest();requests.Remove(req);
                        if(req.result==UnityWebRequest.Result.Success)
                        {
                            try
                            {
                                var next=JsonUtility.FromJson<Snapshot>(req.downloadHandler.text);AcceptCatalog(next);
                            }
                            catch(ArgumentException){if(!demoLive)connectionText.text=onlineSnapshot?"Ошибка данных":"Показаны примеры";}
                        }
                        else if(!demoLive)connectionText.text=onlineSnapshot?"Нет связи":"Офлайн-демо";
                    }
                }
                yield return new WaitForSecondsRealtime(Mathf.Max(.1f,nextPoll-Time.unscaledTime));
            }
        }
        void RememberOfflineAnswer(string field,string message)
        {
            if(manualDraftFields.Contains(field))return;
            string value=message.Trim();
            if(UnknownOfflineAnswer(value))return;
            SetDraftValue(currentDraft,field,value.Length>2000?value.Substring(0,2000):value);
        }
        static bool UnknownOfflineAnswer(string value)
        {
            string normalized=(value??"").Trim().Trim(' ','.','!','?').ToLowerInvariant();
            return new[]{"не знаю","пока не знаю","неизвестно","не указано","нет данных","данных нет","нет","білмеймін","әзірге білмеймін","белгісіз","мәлімет жоқ","деректер жоқ","көрсетілмеген","жоқ","tbd","n/a","unknown","-"}.Contains(normalized);
        }
        static bool ObviousOfflineJunk(string value)
        {
            if(UnknownOfflineAnswer(value))return false;
            string letters=new string(value.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
            if(letters.Length==0&&!value.Any(char.IsDigit))return true;
            if(letters.Length>=6&&letters.Distinct().Count()<=2)return true;
            string[] keyboardFragments={"asd","asdf","asdfgh","asdasd","asdasdasd","qwer","qwerty","qwertyuiop","zxcv","zxcvbn","йцук","йцукен","йцукенгш","фыва","фывапр","ываыва"};
            if(keyboardFragments.Any(fragment=>letters.Length>=fragment.Length&&letters.Replace(fragment,"").Length==0))return true;
            string[] keyboardRows={"qwertyuiop","asdfghjkl","zxcvbnm","йцукенгшщзхъ","фывапролджэ","ячсмитьбю"};
            string[] keyboardMarkers={"qwer","asdf","zxcv","йцук","цукен","фыв","ывапр","пролдж","ячсм"};
            return letters.Length>=4&&keyboardMarkers.Any(marker=>letters.Contains(marker))&&
                keyboardRows.Any(row=>letters.Count(letter=>row.IndexOf(letter)>=0)>=letters.Length*.8f);
        }
        void ShowOfflineQuestion()
        {
            assistantMode.text="Без ИИ · пошаговый режим";
            if(offlineMessages>=1&&offlineMessages<=OfflineQuestions.Length)
            {
                draftStatus.text="Уточнение "+offlineMessages+" из "+OfflineQuestions.Length;
                AddMessage("Вопрос "+offlineMessages+" из "+OfflineQuestions.Length+". "+OfflineQuestions[offlineMessages-1]+"\nЕсли пока не знаете, напишите «не знаю».",false);
            }
            else if(offlineMessages>OfflineQuestions.Length)
            {
                draftStatus.text="Карточка готова к правкам";
                AddMessage("Уточнения завершены. Откройте карточку и проверьте ответы. Неизвестные детали можно добавить позже; публикацию нужно подтвердить отдельно.",false);
                Notify("Черновик готов — откройте карточку");editDraftButton.GetComponent<QuestBridgeMotion>().Pulse(.04f);
            }
        }
        IEnumerator Chat()
        {
            string message=input.text.Trim();if(message.Length==0)yield break;if(!string.IsNullOrWhiteSpace(serverUrl)&&!ValidBaseUrl()){AddMessage("Проверьте адрес сервера. Ваш текст сохранён.",false);yield break;}
            sending=true;sendButton.interactable=false;newChatButton.interactable=false;SetButtonText(sendButton,"Отправляем…");input.interactable=false;
            if(pendingMessage!=message)AddMessage(message,true);pendingMessage=message;bool success=false;
            if(string.IsNullOrWhiteSpace(serverUrl))
            {
                yield return new WaitForSecondsRealtime(.55f);
                bool answeringQuestion=offlineMessages>=1&&offlineMessages<=OfflineQuestions.Length;
                if((UnknownOfflineAnswer(message)&&!answeringQuestion)||ObviousOfflineJunk(message))
                {
                    assistantMode.text="Без ИИ · локальная проверка";draftStatus.text="Уточните ответ";
                    string feedback=answeringQuestion?
                        "Не удалось понять ответ. Опишите его обычными словами; если сведений пока нет, напишите «не знаю».\n"+OfflineQuestions[offlineMessages-1]:
                        offlineMessages==0?"Сначала опишите проблему: что сейчас не получается и кому это мешает. Какую задачу вы хотите решить?":
                        "Уточнения завершены. Откройте карточку, чтобы дополнить или исправить сведения.";
                    AddMessage(feedback,false);
                }
                else
                {
                    if(offlineMessages==0)RememberOfflineAnswer("context",message);
                    else if(offlineMessages<=OfflineQuestionFields.Length)RememberOfflineAnswer(OfflineQuestionFields[offlineMessages-1],message);
                    offlineMessages=Mathf.Min(offlineMessages+1,OfflineQuestions.Length+1);
                    SaveDraft();PlayerPrefs.Save();ShowOfflineQuestion();success=true;
                }
            }
            else
            {
                using(var req=new UnityWebRequest(serverUrl.TrimEnd('/')+"/api/chat","POST"))
                {
                    req.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new ChatRequest{message=message,conversationId=conversationId})));req.downloadHandler=new DownloadHandlerBuffer();req.SetRequestHeader("Content-Type","application/json");req.timeout=25;requests.Add(req);yield return req.SendWebRequest();requests.Remove(req);
                    if(req.result==UnityWebRequest.Result.Success)
                    {
                        ChatReply reply=null;try{reply=JsonUtility.FromJson<ChatReply>(req.downloadHandler.text);if(reply==null||string.IsNullOrWhiteSpace(reply.message)||reply.message.Length>12000||string.IsNullOrWhiteSpace(reply.conversationId))throw new ArgumentException();}catch(ArgumentException){reply=null;}
                        if(reply!=null)
                        {
                            conversationId=reply.conversationId;AddMessage(reply.message,false);
                            assistantMode.text=reply.aiMode=="fallback"?"Без ИИ · пошаговый режим":"ИИ подключён";
                            bool accepted=reply.validation==null||reply.inputAccepted;
                            draftStatus.text=accepted?(reply.phase=="draft_ready"?"Карточка готова к правкам":"Уточняем задачу"):reply.validation?.status=="unavailable"?"Повторите проверку ответа":"Уточните ответ";
                            if(accepted){AcceptDraft(reply);success=true;}
                        }
                        else AddMessage("Ответ сервера имеет неверный формат. Ваш текст сохранён.",false);
                    }
                    else
                    {
                        string error="Сервер не ответил. Ваш текст сохранён — отправьте его ещё раз.";
                        try{var parsed=JsonUtility.FromJson<APIErrorEnvelope>(req.downloadHandler.text);if(!string.IsNullOrWhiteSpace(parsed?.error?.message))error=parsed.error.message;}catch(ArgumentException){}
                        AddMessage(error,false);
                    }
                }
            }
            if(success){input.text="";pendingMessage="";}input.interactable=true;sending=false;sendButton.interactable=true;newChatButton.interactable=true;SetButtonText(sendButton,"Отправить");if(success)Play(riseClip);
        }
        void ToggleDemo()
        {
            CloseDetails();ShowTeam(null,null,false);
            if(!demoLive)
            {
                snapshotBeforeDemo=snapshot;filterBeforeDemo=filter;demoLive=true;demoTick=Time.unscaledTime+5;
                SetButtonText(demoButton,"Выйти");Apply(Demo(),false);SetFilter("Все");DemoLeader();
            }
            else
            {
                demoLive=false;SetButtonText(demoButton,"Демо");Apply(lastServerSnapshot??snapshotBeforeDemo??Demo(),false);SetFilter(filterBeforeDemo);
                connectionText.text=string.IsNullOrWhiteSpace(serverUrl)?"Офлайн-демо":onlineSnapshot?"Сервер подключён":"Подключение…";
            }
        }
        void DemoLeader(){var next=Demo();demoVersion++;next.cards[demoVersion%next.cards.Length].readiness=99;Apply(next,true);connectionText.text="Демо-каталог";}
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
            return new Snapshot{teams=teams,cards=titles.Select((t,i)=>new Card{demo=true,id="demo-"+i,title=t,description=desc[i],category=i==0||i==3||i==5?"Образование":"Бизнес",readiness=92-i*9,clarity=8-i*.5f,teamIds=i%2==0?new[]{"a","b","c"}:new[]{"d","e"}}).ToArray()};
        }
    }
}
