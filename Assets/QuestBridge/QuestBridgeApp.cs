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
    [Serializable] public class ChatReply { public string message, conversationId; }

    // First UI slice. Server contract is documented in docs/API.md.
    public class QuestBridgeApp : MonoBehaviour
    {
        [Tooltip("Empty = clearly labelled offline demo. No AI keys belong in this client.")]
        public string serverUrl = "";
        public float pollSeconds = 5;
        readonly Color ink = new Color32(22, 27, 34, 255), blue = new Color32(218, 238, 255, 255);
        readonly Dictionary<string, RectTransform> rows = new();
        readonly Dictionary<string, float> targets = new();
        Snapshot snapshot;
        TMP_FontAsset font;
        RectTransform root, content, right, middle, veil;
        TMP_Text status, activity, chat;
        TMP_InputField input;
        UnityEngine.UI.Button send;
        UnityEngine.UI.ScrollRect scroll;
        CanvasGroup focusGroup;
        bool focus, sound, sending;
        string conversationId = "", selectedCard;
        AudioSource audioSource;
        AudioClip clickClip, riseClip;
        int demoVersion;

        void Start()
        {
            font = TMP_FontAsset.CreateFontAsset(Resources.Load<Font>("NotoSans-Regular"));
            font.name = "QuestBridge Runtime Font";
            audioSource = gameObject.AddComponent<AudioSource>();
            clickClip = Tone(560, .055f); riseClip = Tone(330, .2f);
            Build();
            Apply(Demo(), false);
            StartCoroutine(Poll());
        }

        RectTransform Box(Transform parent, string name, float x, float y, float w, float h, Color? color = null)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = new Vector2(x, y); rect.anchorMax = new Vector2(x+w, y+h);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            if (color.HasValue) rect.gameObject.AddComponent<UnityEngine.UI.Image>().color = color.Value;
            return rect;
        }
        TMP_Text Label(Transform parent, string value, float x, float y, float w, float h, int size = 20, bool bold = false)
        {
            var r = Box(parent, "Label", x,y,w,h);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>(); t.font = font; t.text = value; t.fontSize = size;
            t.color = ink; t.richText = false; t.raycastTarget = false; t.overflowMode = TextOverflowModes.Ellipsis;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal; t.verticalAlignment = VerticalAlignmentOptions.Middle;
            return t;
        }
        UnityEngine.UI.Button Button(Transform p, string title, float x,float y,float w,float h, Action action, bool dark = false)
        {
            var r = Box(p,title,x,y,w,h,dark ? ink : blue);
            var b = r.gameObject.AddComponent<UnityEngine.UI.Button>(); b.targetGraphic = r.GetComponent<UnityEngine.UI.Image>();
            var text = Label(r,title,.07f,0,.86f,1,17,true); text.alignment = TextAlignmentOptions.Center; text.color = dark ? Color.white : ink;
            b.onClick.AddListener(() => { Play(false); action(); }); return b;
        }
        void Build()
        {
            root = Box(transform,"QuestBridge Canvas",0,0,1,1);
            var canvas = root.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600,900); scaler.matchWidthOrHeight = .5f;
            root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            if (!FindFirstObjectByType<EventSystem>()) new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
            Box(root,"Background",0,0,1,1,Color.white);
            Label(root,"questbridge",.025f,.91f,.26f,.07f,34,true);
            Label(root,"РЕАЛЬНЫЕ ЗАДАЧИ. НОВЫЕ КОМАНДЫ.",.30f,.92f,.42f,.05f,15);
            UnityEngine.UI.Button soundButton = null;
            soundButton = Button(root,"Звук: выкл",.85f,.925f,.125f,.044f,()=> { sound=!sound; soundButton.GetComponentInChildren<TMP_Text>().text=sound?"Звук: вкл":"Звук: выкл"; },false);
            Box(root,"Header rule",.025f,.90f,.95f,.002f,ink);
            var left = Box(root,"Assistant",.025f,.075f,.265f,.79f);
            middle = Box(root,"Catalog",.315f,.075f,.40f,.79f);
            right = Box(root,"Team Details",.74f,.075f,.235f,.79f);
            Box(root,"Divider A",.302f,.075f,.001f,.79f,ink);
            Box(root,"Divider B",.727f,.075f,.001f,.79f,ink);
            Label(left,"01 / ВАША ЗАДАЧА",0,.925f,1,.06f,17,true);
            Label(left,"Хорошая задача\nначинается с вопроса.",0,.78f,1,.14f,30,true);
            chat = Label(left,"Опишите проблему бизнеса. Помощник уточнит данные, результат и критерии успеха.",0,.40f,1,.34f,20);
            var field = Box(left,"Draft input",0,.15f,1,.21f,new Color32(245,247,249,255));
            input = field.gameObject.AddComponent<TMP_InputField>();
            var text = Label(field,"",.04f,.06f,.92f,.88f,19);
            input.textViewport = field; input.textComponent = (TextMeshProUGUI)text; input.lineType = TMP_InputField.LineType.MultiLineNewline;
            input.characterLimit = 3000;
            var placeholder = Label(field,"Например: хотим сократить время проверки работ…",.04f,.06f,.92f,.88f,19);
            placeholder.color = Color.gray; input.placeholder = placeholder;
            send = Button(left,"Отправить →",0,.075f,1,.06f,()=> { if(!sending) StartCoroutine(Chat()); },true);
            Button(left,"Фокус на тексте",0,0,1,.055f,()=> { focus=!focus; });
            Label(middle,"02 / ЖИВОЙ КАТАЛОГ",0,.925f,.80f,.06f,17,true);
            Label(middle,"Задачи набирают высоту",0,.84f,1,.075f,30,true);
            Label(middle,"Готовность ↓    ·    При равенстве — ясность ИИ",0,.78f,1,.05f,16);
            var viewRoot = Box(middle,"Scroll",0,.085f,1,.68f);
            scroll = viewRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            var viewport = Box(viewRoot,"Viewport",0,0,1,1,Color.white);
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            content = Box(viewport,"Content",0,1,1,0); content.pivot = new Vector2(.5f,1);
            scroll.viewport=viewport; scroll.content=content; scroll.horizontal=false; scroll.vertical=true;
            scroll.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped; scroll.inertia=true; scroll.decelerationRate=.08f; scroll.scrollSensitivity=32;
            Button(middle,"Демо: новый лидер ↑",0,0,1,.06f,DemoLeader);
            veil = Box(middle,"Focus veil",0,0,1,1,new Color(1,1,1,.97f));
            focusGroup = veil.gameObject.AddComponent<CanvasGroup>(); focusGroup.alpha=0; focusGroup.blocksRaycasts=false;
            Label(veil,"Режим фокуса\n\nКаталог продолжает обновляться.\nВернитесь к нему, когда закончите мысль.",.1f,.35f,.8f,.3f,22);
            status = Label(root,"ДЕМО · без сервера",.025f,.015f,.25f,.035f,14);
            activity = Label(root,"Выберите аватар команды у карточки",.315f,.015f,.66f,.035f,15);
            ShowTeam(null,null);
        }

        void Update()
        {
            if (!focusGroup) return;
            focusGroup.alpha = Mathf.Lerp(focusGroup.alpha,focus?1:0,1-Mathf.Exp(-10*Time.unscaledDeltaTime));
            focusGroup.blocksRaycasts=focus;
            foreach(var pair in rows)
                if (targets.TryGetValue(pair.Key,out var y)) pair.Value.anchoredPosition = Vector2.Lerp(pair.Value.anchoredPosition,new Vector2(0,y),1-Mathf.Exp(-9*Time.unscaledDeltaTime));
        }
        void Apply(Snapshot next, bool animate)
        {
            if(next?.cards == null || next.teams == null || next.cards.Any(c=>c==null || string.IsNullOrWhiteSpace(c.id)) || next.cards.Select(c=>c.id).Distinct().Count()!=next.cards.Length)
                throw new ArgumentException("Invalid catalog snapshot");
            var oldLeader = snapshot?.cards?.FirstOrDefault()?.id;
            snapshot=next;
            snapshot.cards = snapshot.cards.OrderByDescending(c=>c.readiness).ThenByDescending(c=>c.clarity).ThenBy(c=>c.id,StringComparer.Ordinal).ToArray();
            foreach(var id in rows.Keys.Except(snapshot.cards.Select(c=>c.id)).ToArray()) { Destroy(rows[id].gameObject); rows.Remove(id); targets.Remove(id); }
            content.sizeDelta = new Vector2(0,snapshot.cards.Length*190);
            for(int i=0;i<snapshot.cards.Length;i++)
            {
                var card=snapshot.cards[i]; var y=-i*190;
                if(!rows.TryGetValue(card.id,out var row))
                {
                    row=Box(content,"Card "+card.id,0,1,1,0,Color.white); row.pivot=new Vector2(.5f,1); row.sizeDelta=new Vector2(0,178);
                    row.anchoredPosition=new Vector2(0,animate?y+100:y); rows.Add(card.id,row);
                }
                foreach(Transform child in row) Destroy(child.gameObject);
                targets[card.id]=y;
                Box(row,"Rule",0,.98f,1,.012f,ink);
                Label(row,(i+1).ToString("00"),0,.69f,.10f,.23f,22,true);
                Label(row,card.title,.12f,.60f,.68f,.34f,23,true);
                Label(row,card.readiness.ToString(),.82f,.62f,.18f,.32f,36,true);
                Label(row,card.description,.12f,.32f,.84f,.24f,16);
                Button(row,"Подробнее",.12f,.035f,.27f,.21f,()=>ShowTeam(null,card));
                var ids=card.teamIds ?? Array.Empty<string>();
                for(int j=0;j<Math.Min(ids.Length,4);j++)
                {
                    var team=snapshot.teams.FirstOrDefault(t=>t.id==ids[j]); if(team==null) continue;
                    Button(row,team.initials,.54f+j*.105f,.035f,.095f,.22f,()=>ShowTeam(team,card));
                }
            }
            if(animate && snapshot.cards.Length>0 && oldLeader!=snapshot.cards[0].id)
            { activity.text="Новый лидер: "+snapshot.cards[0].title; if(!focus) Play(true); }
        }
        void ShowTeam(Team team, Card card)
        {
            foreach(Transform child in right) Destroy(child.gameObject);
            Label(right,"03 / КОМАНДЫ И РЕШЕНИЯ",0,.925f,1,.06f,17,true);
            Label(right,team?.name ?? "Знакомьтесь\nс командами",0,.73f,1,.17f,32,true);
            Label(right,team==null ? "Нажмите на аватар рядом с задачей: здесь появятся навыки команды и её опыт." : team.description,0,.50f,1,.20f,20);
            if(team!=null)
            {
                Label(right,"СТЕК\n"+team.stack,0,.34f,1,.12f,19);
                Label(right,team.completed+" подтверждённых этапов",0,.25f,1,.07f,20,true);
            }
            if(card!=null)
            {
                selectedCard=card.id;
                Label(right,card.title+"\n"+card.description+"\nГотовность: "+card.readiness+"/100 · Ясность: "+card.clarity+"/10",0,.015f,1,.22f,17);
            }
        }
        IEnumerator Poll()
        {
            while(true)
            {
                if(!string.IsNullOrWhiteSpace(serverUrl))
                {
                    using(var req=UnityWebRequest.Get(serverUrl.TrimEnd('/')+"/api/catalog"))
                    {
                        req.timeout=8; yield return req.SendWebRequest();
                        if(req.result==UnityWebRequest.Result.Success)
                        {
                            try { Apply(JsonUtility.FromJson<Snapshot>(req.downloadHandler.text),true); status.text="СЕРВЕР · обновление каждые 5 с"; }
                            catch(Exception) { status.text="Ошибка формата · сохранены последние данные"; }
                        }
                        else status.text="Нет связи · показаны последние данные";
                    }
                }
                yield return new WaitForSecondsRealtime(Mathf.Max(5,pollSeconds));
            }
        }
        IEnumerator Chat()
        {
            var message=input.text.Trim(); if(message.Length==0) yield break;
            sending=true; send.interactable=false;
            if(string.IsNullOrWhiteSpace(serverUrl))
                chat.text="ДЕМО-ПОМОЩНИК\n\n1. Кто будет пользоваться решением?\n2. Какие данные и примеры доступны?\n3. Как измерить успешный результат?\n\nДля ответов ИИ подключите сервер.";
            else
            {
                using(var req=new UnityWebRequest(serverUrl.TrimEnd('/')+"/api/chat","POST"))
                {
                    req.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new ChatRequest{message=message,conversationId=conversationId})));
                    req.downloadHandler=new DownloadHandlerBuffer(); req.SetRequestHeader("Content-Type","application/json"); req.timeout=25;
                    yield return req.SendWebRequest();
                    if(req.result==UnityWebRequest.Result.Success)
                    {
                        try { var reply=JsonUtility.FromJson<ChatReply>(req.downloadHandler.text); if(string.IsNullOrWhiteSpace(reply.message)) throw new ArgumentException(); chat.text=reply.message; conversationId=reply.conversationId ?? conversationId; }
                        catch(Exception) { chat.text="Сервер вернул некорректный ответ. Ваш текст сохранён."; }
                    }
                    else chat.text="Сервер недоступен. Ваш текст сохранён. Уточните пользователей, доступные данные и критерии успеха.";
                }
            }
            sending=false; send.interactable=true;
        }
        void DemoLeader()
        {
            if(!string.IsNullOrWhiteSpace(serverUrl)) { activity.text="Демо-события доступны только без сервера"; return; }
            var next=Demo(); demoVersion++;
            var winner=next.cards[demoVersion%next.cards.Length]; winner.readiness=99;
            Apply(next,true); status.text="ДЕМО · событие создано вручную";
        }
        AudioClip Tone(float frequency,float duration)
        {
            int count=(int)(44100*duration); var samples=new float[count];
            for(int i=0;i<count;i++) { float t=(float)i/count; samples[i]=Mathf.Sin(2*Mathf.PI*frequency*i/44100f)*Mathf.Sin(Mathf.PI*t)*(1-t)*.13f; }
            var clip=AudioClip.Create("UI tone",count,1,44100,false); clip.SetData(samples,0); return clip;
        }
        void Play(bool rise) { if(sound && !focus) audioSource.PlayOneShot(rise?riseClip:clickClip); }
        void OnDestroy() { if(font) Destroy(font); if(clickClip) Destroy(clickClip); if(riseClip) Destroy(riseClip); }
        static Snapshot Demo()
        {
            var teams=new[]{new Team{id="a",name="TimasFriends",initials="TF",stack="Unity · Python · AI",completed=3,description="Интерактивные продукты и понятные инструменты для образования."},new Team{id="b",name="Data People",initials="DP",stack="Python · Analytics",completed=7,description="Помогаем находить закономерности в данных бизнеса."},new Team{id="c",name="Next Step",initials="NS",stack="Web · Design",completed=2,description="Создаём удобные сервисы для людей."}};
            var titles=new[]{"Проверка SAT без рутины","Прогноз закупок для кафе","Умный маршрут доставки","Запись в учебный центр","Аналитика обратной связи","Помощник для библиотеки"};
            var descriptions=new[]{"Освободить время преподавателей для работы с учениками.","Сократить списания и планировать закупки по данным продаж.","Помочь диспетчеру распределять заказы между курьерами.","Сделать запись на пробные занятия понятной и быстрой.","Собрать повторяющиеся проблемы из отзывов клиентов.","Помочь читателям находить нужные учебные материалы."};
            return new Snapshot{teams=teams,cards=titles.Select((t,i)=>new Card{id="demo-"+i,title=t,description=descriptions[i],category="Образование",readiness=92-i*9,clarity=8-i*.5f,teamIds=new[]{"a","b","c"}}).ToArray()};
        }
    }
}
