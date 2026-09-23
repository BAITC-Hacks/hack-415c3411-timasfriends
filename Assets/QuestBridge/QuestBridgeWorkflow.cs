using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace QuestBridge
{
    public sealed partial class QuestBridgeApp
    {
        bool isBusiness=true, mineOnly, editorOpen, draftConfirmed, publishing, editingPersonal;
        string businessId="business-demo", activeTeamId="team-1", publicationKey="", lastPublicationPayload="";
        int catalogVersion=-1;
        readonly HashSet<string> manualDraftFields=new();
        RectTransform roleOverlay, teamNavigation, editorScoreContent;
        UnityEngine.UI.Button roleButton, mineButton, readinessButton, editDraftButton, confirmDraftButton, publishButton, retryPreviewButton;
        TMP_Text teamNavigationName, teamNavigationStats, editorScore, editorBreakdown, editorStatus;
        DraftData currentDraft=new DraftData(), editingDraft;
        TaskRecord editingTask, currentDraftTask;
        int readinessFilter, editorGeneration, editorRevision, previewRevision=-1, reviewedRevision=-1;
        float previewAt;
        bool previewRunning;
        string previewError="";
        ContentReview editorValidation;
        readonly Dictionary<string,TMP_InputField> draftInputs=new();
        readonly Dictionary<string,TMP_Text> draftIssueLabels=new();
        static readonly Color InvalidField=Hex(0xB33A32), InvalidBackground=Hex(0xFFF0EE);
        readonly List<UnityEngine.UI.Button> teamMenuButtons=new();
        static readonly string[] DraftFields={"title","category","context","need","users","data","constraints","expectedResult","successCriteria","contact","interactionFormat","feedbackProcess"};
        static readonly string[] FieldLabels={"Название","Тема","Контекст — что происходит сейчас","Потребность — что нужно изменить","Пользователи","Данные и материалы","Ограничения и сроки","Ожидаемый результат","Критерии успеха","Контакт","Формат взаимодействия","Порядок обратной связи"};
        static readonly string[] FieldHints={"Коротко: что требуется сделать","Например: Образование","Опишите текущий процесс","Какую проблему должна решить команда?","Кто будет пользоваться решением?","Какие примеры, файлы или источники доступны?","Срок, технологии, доступы и другие рамки","Что команда должна передать в результате?","По каким измеримым признакам примете работу?","Как связаться с представителем бизнеса?","Например: созвон раз в неделю","Кто и когда отвечает на вопросы команды?"};

        static bool WebShowcaseEnabled()
        {
            if(!Uri.TryCreate(Application.absoluteURL,UriKind.Absolute,out var page))return false;
            return page.Query.TrimStart('?').Split('&').Any(part=>part=="showcase=1");
        }

        void ConfigureWebAddress()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if(Uri.TryCreate(Application.absoluteURL,UriKind.Absolute,out var page))
            {
                serverUrl=page.GetLeftPart(UriPartial.Authority);
                foreach(var pair in page.Query.TrimStart('?').Split('&'))
                {
                    var parts=pair.Split(new[]{'='},2);
                    if(parts.Length==2&&parts[0]=="api")serverUrl=Uri.UnescapeDataString(parts[1]);
                    if(parts.Length==2&&parts[0]=="offline"&&parts[1]=="1")serverUrl="";
                }
            }
#endif
        }
        public void ConfigureServer(string url)
        {
            url=(url??"").Trim().TrimEnd('/');
            if(url.Length>0&&(!Uri.TryCreate(url,UriKind.Absolute,out var parsed)||(parsed.Scheme!="http"&&parsed.Scheme!="https")))
            { if(root)Notify("Некорректный адрес сервера");return; }
            if(serverUrl==url)return;
            serverUrl=url;onlineSnapshot=false;lastServerSnapshot=null;conversationId="";offlineMessages=0;catalogVersion=-1;currentDraftTask=null;
            if(editorOpen)DraftChanged();
            if(root){connectionText.text=url.Length==0?"Офлайн-демо":"Подключение…";StartCoroutine(RefreshCatalog());}
        }
        void InitializeWorkflow()
        {
            roleButton=Button(root,"Выбрать роль",.39f,.924f,.285f,.051f,()=>ShowRoleChoice(),Paper);
            draftStatus.rectTransform.anchorMax=new Vector2(.60f,.343f);
            editDraftButton=Button(left,"Карточка →",.65f,.305f,.295f,.045f,()=>OpenDraftEditor(),Blue);
            editDraftButton.GetComponentInChildren<TMP_Text>().fontSize=14;
            mineButton=Button(middle,"Мои задачи",.71f,.923f,.29f,.055f,()=>{mineOnly=!mineOnly;UpdateScope();},Color.white);
            mineButton.GetComponentInChildren<TMP_Text>().fontSize=15;
            countText.rectTransform.anchorMax=new Vector2(.61f,.906f);
            readinessButton=Button(middle,"Любая готовность",.63f,.86f,.37f,.047f,()=>{readinessFilter=(readinessFilter+1)%5;SetButtonText(readinessButton,new[]{"Любая готовность","0–39 · Черновик","40–69 · Рабочая","70–89 · Готовая","90–100 · Приоритет"}[readinessFilter]);Arrange(false);},Color.white);
            readinessButton.GetComponentInChildren<TMP_Text>().fontSize=14;
            teamNavigation=Panel(root,"Team navigation",.025f,.036f,.205f,.839f);
            Text(teamNavigation,"Моя команда",.08f,.895f,.84f,.08f,25,true);
            teamNavigationName=Text(teamNavigation,"",.08f,.765f,.84f,.10f,22,true);
            teamNavigationStats=Text(teamNavigation,"",.08f,.655f,.84f,.085f,17,false,Muted);
            Button(teamNavigation,"Сменить команду",.08f,.575f,.84f,.062f,()=>ShowRoleChoice(true),Paper);
            Surface(teamNavigation,"Team rule",.08f,.532f,.84f,.002f,Line,false);
            teamMenuButtons.Add(Button(teamNavigation,"Каталог задач",.08f,.423f,.84f,.078f,()=>{mineOnly=false;UpdateScope();},Blue));
            teamMenuButtons.Add(Button(teamNavigation,"Мои отклики",.08f,.325f,.84f,.078f,()=>{mineOnly=true;UpdateScope();},Paper));
            Text(teamNavigation,"Выберите задачу\nи предложите решение",.08f,.12f,.84f,.14f,18,false,Muted);
            teamNavigation.transform.parent.gameObject.SetActive(false);
            try{var saved=PlayerPrefs.GetString("QuestBridge.LocalDraft","");if(saved.Length>0)currentDraft=JsonUtility.FromJson<DraftData>(saved)??new DraftData();}catch(ArgumentException){currentDraft=new DraftData();}
            LoadScoreFeedbackState();
            foreach(var key in PlayerPrefs.GetString("QuestBridge.ManualFields","").Split(','))if(DraftFields.Contains(key))manualDraftFields.Add(key);
            offlineMessages=Mathf.Clamp(PlayerPrefs.GetInt("QuestBridge.OfflineMessages",0),0,OfflineQuestions.Length+1);
            if(string.IsNullOrWhiteSpace(serverUrl)&&offlineMessages>0)
            {
                chatBubbles.Clear();Clear(chatContent);chatHeight=0;lastChatWidth=0;ShowOfflineQuestion();
            }
            activeTeamId=PlayerPrefs.GetString("QuestBridge.Team","team-1");
            ShowRoleChoice();
        }
        void ShowRoleChoice(bool chooseTeam=false)
        {
            if(sending||publishing){Notify("Дождитесь окончания отправки");return;}
            CloseDetails();if(focus)ToggleFocus();
            if(roleOverlay){roleOverlay.gameObject.SetActive(false);Destroy(roleOverlay.gameObject);}
            roleOverlay=Surface(root,"Role selection",0,0,1,1,Paper,false,true);
            var pane=Surface(roleOverlay,"Welcome",.25f,.15f,.5f,.7f,Color.white);
            DrawBrand(pane,.08f,.80f,.38f,.13f);
            Text(pane,chooseTeam?"Выберите команду":"В какой роли продолжим?",.08f,.70f,.84f,.08f,24,true);
            if(!chooseTeam)
            {
                Button(pane,"Я бизнес",.08f,.49f,.84f,.115f,()=>SetRole(true),null,true);
                Text(pane,"Сформулировать задачу и выбрать команду",.09f,.413f,.82f,.065f,18,false,Muted);
                Button(pane,"Я команда",.08f,.23f,.84f,.115f,()=>ShowRoleChoice(true),Blue);
                Text(pane,"Найти задачу и предложить решение",.09f,.151f,.82f,.065f,18,false,Muted);
            }
            else
            {
                var choices=(lastServerSnapshot?.teams??snapshot?.teams??Array.Empty<Team>()).Where(t=>t.id.StartsWith("team-",StringComparison.Ordinal)).ToArray();
                if(choices.Length==0)choices=new[]{new Team{id="team-1",name="TimasFriends"},new Team{id="team-2",name="Fraction Lab"},new Team{id="team-3",name="Study Map"},new Team{id="team-4",name="Science Cards"},new Team{id="team-5",name="Lab Notes"}};
                var body=CreateScroll(pane,"Choose profile",.08f,.15f,.84f,.50f,out var list);
                int n=0;foreach(var team in choices){var row=WorkflowRow(body,"Team choice",n++*64,54);Button(row,TeamName(team),0,0,1,1,()=>{activeTeamId=team.id;PlayerPrefs.SetString("QuestBridge.Team",activeTeamId);SetRole(false);},Paper);}
                body.sizeDelta=new Vector2(0,n*64);Button(pane,"← Назад",.08f,.043f,.28f,.072f,()=>ShowRoleChoice(),Paper);
            }
        }
        void SetRole(bool business)
        {
            if(sending||publishing)return;CloseDetails();
            isBusiness=business;mineOnly=false;readinessFilter=0;SetButtonText(readinessButton,"Любая готовность");
            if(roleOverlay){roleOverlay.gameObject.SetActive(false);Destroy(roleOverlay.gameObject);roleOverlay=null;}
            left.transform.parent.gameObject.SetActive(business);teamNavigation.transform.parent.gameObject.SetActive(!business);
            middle.anchorMin=new Vector2(business?.322f:.247f,.036f);middle.anchorMax=new Vector2(.760f,.875f);
            middle.offsetMin=middle.offsetMax=Vector2.zero;
            SetButtonText(roleButton,business?"Бизнес · сменить роль":"Команда · сменить роль");
            SetButtonText(mineButton,business?"Мои задачи":"Мои отклики");
            if(focus)ToggleFocus();ShowTeam(null,null,false);UpdateTeamNavigation();UpdateScope();
        }
        void UpdateTeamNavigation()
        {
            if(!teamNavigationName)return;
            var team=snapshot?.teams?.FirstOrDefault(t=>t.id==activeTeamId);
            teamNavigationName.text=TeamName(team);
            teamNavigationStats.text=team==null?"Выберите профиль команды":team.experience+" опыта  ·  "+team.completed+" этапов\n"+(team.stack??"");
        }
        void UpdateScope()
        {
            SetButtonText(mineButton,mineOnly?"Все задачи":isBusiness?"Мои задачи":"Мои отклики");
            for(int i=0;i<teamMenuButtons.Count;i++)
            {
                var button=teamMenuButtons[i];bool selected=(i==1)==mineOnly;
                var motion=button.GetComponent<QuestBridgeMotion>();motion.resting=selected?Blue:Paper;motion.hovered=selected?Blue:Color.white;
                button.GetComponentInChildren<TMP_Text>().color=selected?Accent:Ink;
            }
            Arrange(false);
        }
        bool WorkflowVisible(Card card)
        {
            bool readiness=readinessFilter==0||readinessFilter==1&&card.readiness<40||readinessFilter==2&&card.readiness>=40&&card.readiness<70||readinessFilter==3&&card.readiness>=70&&card.readiness<90||readinessFilter==4&&card.readiness>=90;
            bool scope=!mineOnly||(isBusiness?card.businessId==businessId:(card.teamIds??Array.Empty<string>()).Contains(activeTeamId));
            return readiness&&scope;
        }
        static string ReadinessName(int score)=>score<40?"Черновик":score<70?"Рабочая":score<90?"Готовая":"Приоритетная";
        void SaveDraft(){PlayerPrefs.SetString("QuestBridge.LocalDraft",JsonUtility.ToJson(currentDraft));PlayerPrefs.SetString("QuestBridge.ManualFields",string.Join(",",manualDraftFields));PlayerPrefs.SetInt("QuestBridge.OfflineMessages",offlineMessages);}
        void AcceptDraft(ChatReply reply)
        {
            if(reply.draft!=null){foreach(var key in manualDraftFields)SetDraftValue(reply.draft,key,DraftValue(currentDraft,key));currentDraft=reply.draft;SaveDraft();}
            if(reply.phase=="draft_ready"){Notify("Черновик готов — откройте карточку");editDraftButton.GetComponent<QuestBridgeMotion>().Pulse(.04f);}
        }
        RectTransform WorkflowRow(Transform parent,string name,float top,float height)
        {
            var row=Rect(parent,name,0,1,1,0);row.pivot=new Vector2(.5f,1);row.sizeDelta=new Vector2(0,height);row.anchoredPosition=new Vector2(0,-top);return row;
        }
        RectTransform WorkflowPane(string title,string subtitle)
        {
            CloseDetails();detailOverlay=Rect(root,"Workflow",0,0,1,1);
            var backdrop=Surface(detailOverlay,"Dismiss workflow",0,0,1,1,new Color(.06f,.10f,.15f,.35f),false,true);
            var dismiss=backdrop.gameObject.AddComponent<UnityEngine.UI.Button>();dismiss.targetGraphic=backdrop.GetComponent<UnityEngine.UI.Image>();dismiss.transition=UnityEngine.UI.Selectable.Transition.None;dismiss.onClick.AddListener(CloseDetails);
            var pane=Surface(detailOverlay,"Workflow pane",.14f,.045f,.72f,.91f,Color.white,true,true);
            Text(pane,title,.04f,.900f,.84f,.064f,28,true);
            Text(pane,subtitle,.04f,.850f,.84f,.048f,16,false,Muted);
            Button(pane,"×",.91f,.917f,.05f,.049f,CloseDetails,Paper);
            Surface(pane,"Header divider",.04f,.830f,.92f,.0015f,Line,false);
            return pane;
        }
        TMP_InputField WorkflowInput(Transform parent,string value,string hint,float x,float y,float w,float h,int limit=2000)
        {
            var box=Surface(parent,"Field",x,y,w,h,Paper,true,true);
            var field=box.gameObject.AddComponent<TMP_InputField>();field.characterLimit=limit;
            var viewport=Rect(box,"Text viewport",0,0,1,1);viewport.offsetMin=new Vector2(14,10);viewport.offsetMax=new Vector2(-14,-10);viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var typed=Text(viewport,"",0,0,1,1,18);var placeholder=Text(viewport,hint,0,0,1,1,17,false,Muted);
            QuestBridgeComposer.Configure(field,typed,placeholder,viewport);field.SetTextWithoutNotify(value??"");return field;
        }
        void OpenDraftEditor(TaskRecord existing=null)
        {
            if(sending||publishing){Notify("Дождитесь завершения отправки");return;}
            if(existing!=null&&existing.businessId!=businessId){Notify("Изменять задачу может её автор");return;}
            editingPersonal=existing==null;editingTask=existing??currentDraftTask;editingDraft=CloneDraft(editingPersonal?currentDraft:existing.draft);existing=editingTask;
            var pane=WorkflowPane(existing==null?"Карточка задачи":"Редактирование задачи","Проверьте сведения. Неизвестные поля можно оставить пустыми.");
            editorOpen=true;editorRevision=0;previewRevision=-1;reviewedRevision=-1;draftConfirmed=false;editorValidation=null;previewError="";draftInputs.Clear();draftIssueLabels.Clear();
            var fields=CreateScroll(pane,"Draft fields",.04f,.145f,.59f,.665f,out var scroll);
            float top=0;
            for(int i=0;i<DraftFields.Length;i++)
            {
                string key=DraftFields[i];float height=i<2?154:182;
                var row=WorkflowRow(fields,key,top,height-12);top+=height;
                Text(row,FieldLabels[i],0,.74f,1,.25f,16,true).rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top,0,28);
                var field=WorkflowInput(row,DraftValue(editingDraft,key),FieldHints[i],0,0,1,1,i==0?160:i==1?100:2000);draftInputs[key]=field;
                var fieldRect=(RectTransform)field.transform;fieldRect.offsetMin=new Vector2(0,44);fieldRect.offsetMax=new Vector2(0,-32);
                var issueLabel=Text(row,"",0,0,1,.23f,14,false,InvalidField);issueLabel.alignment=TextAlignmentOptions.TopLeft;draftIssueLabels[key]=issueLabel;
                field.onValueChanged.AddListener(value=>{SetDraftValue(editingDraft,key,value);if(editingPersonal)manualDraftFields.Add(key);DraftChanged();});
            }
            fields.sizeDelta=new Vector2(0,top);
            var scorePanel=Surface(pane,"Rating",.655f,.145f,.305f,.665f,Paper);
            Text(scorePanel,"Подтверждённые баллы",.07f,.89f,.86f,.065f,17,true);
            editorScore=Text(scorePanel,"… / 100",.07f,.765f,.86f,.12f,35,true,Accent);
            InitializeScoreFeedback(scorePanel,existing);
            var scoreBody=CreateScroll(scorePanel,"Score details",.07f,.155f,.86f,.515f,out var scoreScroll);editorScoreContent=scoreBody;
            editorBreakdown=Text(scoreBody,"Запрашиваем расчёт…",0,0,1,1,16,false,Muted);editorBreakdown.alignment=TextAlignmentOptions.TopLeft;editorBreakdown.overflowMode=TextOverflowModes.Overflow;
            scoreBody.sizeDelta=new Vector2(0,520);
            Text(scorePanel,"ИИ-ясность — после публикации",.07f,.092f,.86f,.05f,14,false,Muted);
            retryPreviewButton=Button(scorePanel,"Повторить проверку",.07f,.015f,.86f,.065f,()=>{DraftChanged();previewAt=Time.unscaledTime;},Blue);
            retryPreviewButton.GetComponentInChildren<TMP_Text>().fontSize=14;
            confirmDraftButton=Button(pane,"Подтвердить сведения",.04f,.07f,.43f,.054f,ConfirmDraftWithFeedback,Blue);
            publishButton=Button(pane,existing==null?"Опубликовать":"Сохранить изменения",.655f,.055f,.305f,.068f,()=>PublishDraft(),null,true);
            editorStatus=Text(pane,"",.04f,.017f,.59f,.046f,14,false,Muted);
            ShowPreviewPending();UpdatePublishButton();previewAt=Time.unscaledTime;
            Canvas.ForceUpdateCanvases();scroll.verticalNormalizedPosition=1;scoreScroll.verticalNormalizedPosition=1;
        }
        void DraftChanged()
        {
            editorRevision++;draftConfirmed=false;editorValidation=null;previewError="";reviewedRevision=-1;
            latestPreview=null;latestPreviewRevision=-1;scoreAnimationSerial++;
            SetButtonText(confirmDraftButton,"Подтвердить сведения");previewAt=Time.unscaledTime+.8f;
            if(editorScore){editorScore.color=Accent;editorScore.transform.localScale=Vector3.one;}
            ShowFieldIssues(Array.Empty<ContentIssue>());ShowPreviewPending();
            if(editingPersonal){currentDraft=CloneDraft(editingDraft);SaveDraft();}
            UpdatePublishButton();
        }
        bool ApprovedPreview()=>reviewedRevision==editorRevision&&editorValidation?.status=="passed"&&!previewRunning;
        void SetEditorBreakdown(string value)
        {
            if(!editorBreakdown)return;editorBreakdown.text=value;
            if(editorScoreContent)editorScoreContent.sizeDelta=new Vector2(0,Mathf.Max(360,editorBreakdown.GetPreferredValues(value,Mathf.Max(180,editorScoreContent.rect.width),Mathf.Infinity).y+24));
        }
        void ShowPreviewPending()
        {
            if(!editorScore)return;
            editorScore.text=ValidBaseUrl()?"Проверяем…":"Без оценки";
            SetEditorBreakdown(ValidBaseUrl()?"Проверяем содержание полей. После проверки появится расчёт баллов.":"Для проверки содержания и расчёта баллов подключите сервер. Черновик сохраняется на этом устройстве.");
        }
        void ShowFieldIssues(ContentIssue[] issues)
        {
            foreach(var item in draftInputs)if(item.Value)item.Value.GetComponent<UnityEngine.UI.Image>().color=Paper;
            foreach(var label in draftIssueLabels.Values)if(label)label.text="";
            foreach(var issue in issues??Array.Empty<ContentIssue>())
            {
                if(issue==null||string.IsNullOrWhiteSpace(issue.field))continue;
                if(draftInputs.TryGetValue(issue.field,out var field)&&field)field.GetComponent<UnityEngine.UI.Image>().color=InvalidBackground;
                if(draftIssueLabels.TryGetValue(issue.field,out var label)&&label)
                    label.text+=(label.text.Length>0?" ":"")+(string.IsNullOrWhiteSpace(issue.message)?"Уточните содержание этого поля.":issue.message);
            }
        }
        void UpdatePublishButton()
        {
            if(!publishButton)return;
            bool named=!string.IsNullOrWhiteSpace(editingDraft.title)&&!string.IsNullOrWhiteSpace(editingDraft.category);
            bool checkedDraft=ApprovedPreview(), connected=ValidBaseUrl();
            publishButton.interactable=!publishing&&draftConfirmed&&named&&connected&&checkedDraft;
            var motion=publishButton.GetComponent<QuestBridgeMotion>();motion.resting=publishButton.interactable?Ink:Hex(0xDDE3E9);motion.hovered=motion.resting;publishButton.GetComponentInChildren<TMP_Text>().color=publishButton.interactable?Color.white:Muted;
            if(confirmDraftButton){confirmDraftButton.interactable=!publishing&&!draftConfirmed&&checkedDraft;confirmDraftButton.GetComponentInChildren<TMP_Text>().color=confirmDraftButton.interactable?Ink:Muted;}
            if(retryPreviewButton)retryPreviewButton.interactable=connected&&!publishing&&!previewRunning;
            if(editorStatus)editorStatus.text=publishing?"Сохраняем…":!connected?"Подключите сервер для проверки и публикации.":previewError.Length>0?"Проверка не завершена. Повторите её.":reviewedRevision!=editorRevision||previewRunning?"Проверяем содержание. Дождитесь результата.":editorValidation?.status=="rejected"?"Исправьте отмеченные поля и дождитесь новой проверки.":editorValidation?.status!="passed"?"Проверка недоступна. Нажмите «Повторить проверку».":!named?"Заполните название и тему.":!draftConfirmed?"Проверьте сведения и подтвердите публикацию.":"Готово к сохранению. Низкий балл не мешает публикации.";
            RefreshScoreForecast();
        }
        void UpdateWorkflow()
        {
            if(editorOpen&&detailOverlay&&ValidBaseUrl()&&!previewRunning&&previewRevision!=editorRevision&&Time.unscaledTime>=previewAt&&!publishing)
            {previewRevision=editorRevision;previewRunning=true;UpdatePublishButton();StartCoroutine(PreviewDraft(editorGeneration,editorRevision,CloneDraft(editingDraft)));}
        }
        IEnumerator PreviewDraft(int generation,int revision,DraftData draft)
        {
            try
            {
            yield return ApiRequest<ScorePreview>("POST","/api/tasks/preview",new PreviewBody{draft=draft,confirmedFields=FilledFields(draft)},score=>
            {
                if(!editorOpen||generation!=editorGeneration||revision!=editorRevision||!editorScore)return;
                reviewedRevision=revision;previewError="";editorValidation=score.validation;
                if(editorValidation==null)editorValidation=new ContentReview{status="unavailable",message="Сервер не вернул проверку содержания. Повторите проверку после обновления сервера."};
                latestPreview=editorValidation.status=="passed"?score:null;latestPreviewRevision=latestPreview==null?-1:revision;
                ShowFieldIssues(editorValidation.issues);
                bool scored=editorValidation.status=="passed"||editorValidation.status=="rejected";
                editorScore.text=editorValidation.status=="passed"?confirmedDisplayScore+" / 100":editorValidation.status=="rejected"?"Нужны правки":"Без оценки";
                var lines=new StringBuilder(editorValidation.status=="passed"?ReadinessName(score.readiness)+"\n\n":"");
                if(!string.IsNullOrWhiteSpace(editorValidation.message))lines.Append(editorValidation.message).Append("\n\n");
                if(editorValidation.status=="passed"&&editorValidation.aiMode=="fallback")lines.Append("AI не подключён. Выполнена локальная проверка.\n\n");
                foreach(var issue in editorValidation.issues??Array.Empty<ContentIssue>())
                    if(issue!=null)lines.Append(ShortLabel(issue.field)).Append(": ").Append(issue.message).Append("\n\n");
                if(!scored){lines.Append("Нажмите «Повторить проверку». Публикация доступна после проверки.");SetEditorBreakdown(lines.ToString());return;}
                foreach(var row in score.scoreBreakdown??Array.Empty<ScoreRow>())lines.Append(row.points>0?"✓ ":"○ ").Append(ShortLabel(row.field)).Append("  ").Append(row.points).Append('/').Append(row.maxPoints).Append('\n');
                lines.Append("\nБаллы показывают полноту описания после подтверждения. Они не доказывают истинность сведений.");SetEditorBreakdown(lines.ToString());
            },error=>
            {
                if(editorOpen&&generation==editorGeneration&&revision==editorRevision&&editorScore)
                {reviewedRevision=revision;previewError=error;editorValidation=null;latestPreview=null;latestPreviewRevision=-1;editorScore.text="Без оценки";SetEditorBreakdown(error+"\n\nНажмите «Повторить проверку».");}
            });
            }
            finally{previewRunning=false;if(editorOpen)UpdatePublishButton();}
        }
        void PublishDraft()
        {
            if(publishing||!draftConfirmed||!ApprovedPreview()||!ValidBaseUrl()||string.IsNullOrWhiteSpace(editingDraft.title)||string.IsNullOrWhiteSpace(editingDraft.category))return;
            publishing=true;UpdatePublishButton();foreach(var field in draftInputs.Values)field.interactable=false;confirmDraftButton.interactable=false;
            var draft=CloneDraft(editingDraft);string[] confirmed=FilledFields(draft);int generation=editorGeneration;bool personal=editingPersonal;
            object body=editingTask==null?new PublishBody{businessId=businessId,conversationId=conversationId,draft=draft,confirmed=true,confirmedFields=confirmed}:
                new EditTaskBody{businessId=businessId,conversationId="",draft=draft,confirmed=true,confirmedFields=confirmed,reconfirmedFields=confirmed,version=editingTask.version};
            string path=editingTask==null?"/api/tasks":"/api/tasks/"+Uri.EscapeDataString(editingTask.id);string method=editingTask==null?"POST":"PATCH";
            string payload=JsonUtility.ToJson(body);if(method=="POST"&&(publicationKey.Length==0||payload!=lastPublicationPayload)){publicationKey=Guid.NewGuid().ToString("N");lastPublicationPayload=payload;}
            StartCoroutine(ApiRequest<TaskRecord>(method,path,body,task=>
            {
                publishing=false;if(personal){currentDraft=draft;currentDraftTask=task;SaveDraft();}
                if(demoLive){demoLive=false;SetButtonText(demoButton,"Демо");}
                StartCoroutine(RefreshCatalog());Notify("Задача сохранена · "+task.readiness+" баллов");
                if(detailOverlay&&generation==editorGeneration)ShowTask(task);
            },error=>
            {
                publishing=false;if(!editorOpen||generation!=editorGeneration){Notify(error);return;}
                foreach(var field in draftInputs.Values)if(field)field.interactable=true;
                draftConfirmed=false;editorValidation=null;previewError=error;latestPreview=null;latestPreviewRevision=-1;scoreAnimationSerial++;
                SetButtonText(confirmDraftButton,"Подтвердить сведения");
                if(editorScore){editorScore.text="Без оценки";editorScore.color=Accent;editorScore.transform.localScale=Vector3.one;}
                SetEditorBreakdown(error+"\n\nИсправьте сведения или повторите проверку.");UpdatePublishButton();
            }));
        }
        void OpenServerTask(Card card)
        {
            var pane=WorkflowPane(CardTitle(card),"Загрузка карточки…");int generation=editorGeneration;
            StartCoroutine(ApiRequest<TaskRecord>("GET","/api/tasks/"+Uri.EscapeDataString(card.id),null,task=>{if(detailOverlay&&generation==editorGeneration)ShowTask(task);},error=>{if(detailOverlay&&generation==editorGeneration)Text(pane,error,.05f,.4f,.9f,.2f,20,false,Muted);}));
        }
        void ShowTask(TaskRecord task)
        {
            if(task?.draft==null){Notify("Сервер вернул неполную карточку");return;}
            string taskTitle=task.demo&&(task.draft.title??"").StartsWith("ДЕМО:",StringComparison.OrdinalIgnoreCase)?task.draft.title.Substring(5).Trim():task.draft.title;
            var pane=WorkflowPane(taskTitle,task.draft.category+" · "+ReadinessName(task.readiness)+(task.demo?" · Пример":""));
            var body=CreateScroll(pane,"Full task",.04f,.14f,.61f,.67f,out var scroll);float top=0;
            Canvas.ForceUpdateCanvases();float width=Mathf.Max(240,body.rect.width);
            for(int i=2;i<DraftFields.Length;i++)
            {
                string value=DraftValue(task.draft,DraftFields[i]);if(string.IsNullOrWhiteSpace(value))value="Не указано";
                var row=WorkflowRow(body,DraftFields[i],top,100);Text(row,FieldLabels[i],0,0,1,1,16,true).rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top,0,27);
                var label=Text(row,value,0,0,1,1,18,false,Muted);label.alignment=TextAlignmentOptions.TopLeft;label.overflowMode=TextOverflowModes.Overflow;
                QuestBridgeBrowserText.AttachSelectable(label);
                float height=Mathf.Max(45,label.GetPreferredValues(value,width,0).y+8);row.sizeDelta=new Vector2(0,height+45);label.rectTransform.offsetMax=new Vector2(0,-34);top+=height+57;
            }
            body.sizeDelta=new Vector2(0,top);
            var rating=Surface(pane,"Published score",.68f,.42f,.28f,.39f,Paper);
            Text(rating,task.readiness+" / 100",.08f,.76f,.84f,.16f,32,true,Accent);
            var scoreContent=CreateScroll(rating,"Published breakdown",.08f,.07f,.84f,.65f,out var scoreScroll);
            var scoreText=new StringBuilder();foreach(var row in task.scoreBreakdown??Array.Empty<ScoreRow>())scoreText.Append(ShortLabel(row.field)).Append("  ").Append(row.points).Append('/').Append(row.maxPoints).Append('\n');
            var scoreLabel=Text(scoreContent,scoreText.ToString(),0,0,1,1,16,false,Muted);scoreLabel.alignment=TextAlignmentOptions.TopLeft;scoreLabel.overflowMode=TextOverflowModes.Overflow;scoreContent.sizeDelta=new Vector2(0,360);
            DrawAiRating(pane,task,.68f,.17f,.28f,.22f);
            Button(pane,isBusiness?"Предложения команд · "+task.proposalCount:"Откликнуться · "+task.proposalCount,.68f,.065f,.28f,.08f,()=>ShowProposals(task),null,true);
            if(isBusiness&&task.businessId==businessId)Button(pane,"Редактировать",.04f,.054f,.28f,.064f,()=>OpenDraftEditor(task),Blue);
            Canvas.ForceUpdateCanvases();scroll.verticalNormalizedPosition=1;scoreScroll.verticalNormalizedPosition=1;
        }
        IEnumerator ApiRequest<T>(string method,string path,object body,Action<T> onSuccess,Action<string> onError=null)
        {
            if(!ValidBaseUrl()){(onError??Notify)("Подключите сервер по инструкции в README.");yield break;}
            string requestBase=serverUrl.TrimEnd('/');
            using(var req=new UnityWebRequest(requestBase+path,method))
            {
                req.downloadHandler=new DownloadHandlerBuffer();req.timeout=25;
                if(body!=null){req.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));req.SetRequestHeader("Content-Type","application/json");}
                if(method=="POST"&&path=="/api/tasks"&&publicationKey.Length>0)req.SetRequestHeader("Idempotency-Key",publicationKey);
                requests.Add(req);yield return req.SendWebRequest();requests.Remove(req);
                if(req.result!=UnityWebRequest.Result.Success)
                {
                    string error=req.responseCode==0?"Нет связи с сервером. Данные сохранены — попробуйте ещё раз.":"Не удалось сохранить данные ("+req.responseCode+").";
                    try{var parsed=JsonUtility.FromJson<APIErrorEnvelope>(req.downloadHandler.text);if(!string.IsNullOrWhiteSpace(parsed?.error?.message))error=parsed.error.message;}catch(ArgumentException){}
                    (onError??Notify)(error);yield break;
                }
                T response=default;bool valid=true;try{response=JsonUtility.FromJson<T>(req.downloadHandler.text);if(response==null)valid=false;}catch(ArgumentException){valid=false;}
                if(!valid){(onError??Notify)("Ответ сервера имеет неверный формат.");yield break;}
                onSuccess(response);
            }
        }
        IEnumerator RefreshCatalog()
        {
            if(!ValidBaseUrl())yield break;
            yield return ApiRequest<Snapshot>("GET","/api/catalog",null,next=>
            {
                try{AcceptCatalog(next);}catch(ArgumentException){Notify("Не удалось обновить каталог.");}
            },error=>Notify(error));
        }
        void AcceptCatalog(Snapshot next)
        {
            Validate(next);if(next.version<catalogVersion)return;catalogVersion=next.version;lastServerSnapshot=next;
            if(!demoLive){Apply(next,onlineSnapshot);connectionText.text="Сервер подключён";}onlineSnapshot=true;UpdateTeamNavigation();
        }
        static DraftData CloneDraft(DraftData draft)=>JsonUtility.FromJson<DraftData>(JsonUtility.ToJson(draft??new DraftData()));
        static string[] FilledFields(DraftData draft)=>DraftFields.Where(field=>!string.IsNullOrWhiteSpace(DraftValue(draft,field))).ToArray();
        static string ShortLabel(string field)
        {
            int index=Array.IndexOf(DraftFields,field);if(index<0)return field;return new[]{"Название","Тема","Контекст","Потребность","Пользователи","Данные","Ограничения","Результат","Критерии успеха","Контакт","Взаимодействие","Обратная связь"}[index];
        }
        static string DraftValue(DraftData d,string field)
        {
            return field switch {"title"=>d.title,"category"=>d.category,"context"=>d.context,"need"=>d.need,"users"=>d.users,"data"=>d.data,"constraints"=>d.constraints,"expectedResult"=>d.expectedResult,"successCriteria"=>d.successCriteria,"contact"=>d.contact,"interactionFormat"=>d.interactionFormat,"feedbackProcess"=>d.feedbackProcess,_=>""};
        }
        static void SetDraftValue(DraftData d,string field,string value)
        {
            switch(field){case "title":d.title=value;break;case "category":d.category=value;break;case "context":d.context=value;break;case "need":d.need=value;break;case "users":d.users=value;break;case "data":d.data=value;break;case "constraints":d.constraints=value;break;case "expectedResult":d.expectedResult=value;break;case "successCriteria":d.successCriteria=value;break;case "contact":d.contact=value;break;case "interactionFormat":d.interactionFormat=value;break;case "feedbackProcess":d.feedbackProcess=value;break;}
        }
    }
}
