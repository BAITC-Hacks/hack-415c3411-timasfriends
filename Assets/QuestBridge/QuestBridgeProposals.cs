using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;

namespace QuestBridge
{
    [Serializable] public sealed class QBProposalDetails { public string idea, plan, timeline, prototypeUrl; }
    [Serializable] public sealed class QBProposal
    {
        public string id, taskId, teamId, decision, createdAt, updatedAt;
        public Team team;
        public bool publishDetails;
        public QBProposalDetails details;
    }
    [Serializable] public sealed class QBProposalList { public QBProposal[] proposals; }
    [Serializable] public sealed class QBMilestone
    {
        public string id, proposalId, taskId, teamId, description, createdAt, confirmedAt;
        public bool confirmed;
        public int experienceAwarded;
        public Team team;
    }
    [Serializable] public sealed class QBMilestoneList { public QBMilestone[] milestones; }
    [Serializable] sealed class ProposalSubmitBody { public string teamId, idea, plan, timeline, prototypeUrl; public bool publishDetails; }
    [Serializable] sealed class ProposalDecisionBody { public string businessId, decision; }
    [Serializable] sealed class ProposalMilestoneBody { public string teamId, description; }
    [Serializable] sealed class ProposalConfirmBody { public string businessId; }

    public sealed partial class QuestBridgeApp
    {
        bool OwnsTask(TaskRecord task) => isBusiness && task.businessId == businessId;
        bool ProposalPaneActive(RectTransform pane) => pane && pane.gameObject.activeInHierarchy && detailOverlay && pane.IsChildOf(detailOverlay);
        static string ProposalDecisionLabel(string decision) => decision == "selected" ? "Выбрана" : decision == "rejected" ? "Отклонена" : "На рассмотрении";
        static string ProposalUrl(string value) => Uri.EscapeDataString(value ?? "");

        RectTransform ProposalPane(string title, string subtitle)
        {
            CloseDetails();
            detailOverlay = Rect(root, "Workflow overlay", 0, 0, 1, 1);
            var backdrop = Surface(detailOverlay, "Dismiss backdrop", 0, 0, 1, 1, new Color(.06f,.10f,.15f,.34f), false, true);
            var dismiss = backdrop.gameObject.AddComponent<UnityEngine.UI.Button>();
            dismiss.targetGraphic = backdrop.GetComponent<UnityEngine.UI.Image>();
            dismiss.transition = UnityEngine.UI.Selectable.Transition.None;
            dismiss.onClick.AddListener(CloseDetails);
            var pane = Surface(detailOverlay, "Workflow pane", .15f, .055f, .7f, .89f, Color.white, true, true);
            Text(pane, title, .045f, .875f, .82f, .07f, 28, true);
            Text(pane, subtitle, .045f, .816f, .84f, .052f, 17, false, Muted);
            Button(pane, "×", .9f, .885f, .055f, .055f, CloseDetails, Paper);
            Surface(pane, "Header divider", .045f, .8f, .91f, .0015f, Line, false);
            return pane;
        }

        RectTransform ProposalRow(RectTransform parent, string name, float top, float height, Color color)
        {
            var row = Surface(parent, name, 0, 1, 1, 0, color);
            row.pivot = new Vector2(.5f, 1);
            row.sizeDelta = new Vector2(0, height);
            row.anchoredPosition = new Vector2(0, -top);
            return row;
        }

        float ProposalParagraph(RectTransform parent, string heading, string value, float top, float width)
        {
            var row = ProposalRow(parent, heading, top, 72, Color.white);
            var label = Text(row, heading, 0, 1, 1, 0, 15, true, Muted);
            label.rectTransform.pivot = new Vector2(.5f, 1);
            label.rectTransform.sizeDelta = new Vector2(0, 25);
            var text = Text(row, string.IsNullOrWhiteSpace(value) ? "Не указано" : value, 0, 1, 1, 0, 19);
            text.rectTransform.pivot = new Vector2(.5f, 1);
            text.rectTransform.anchoredPosition = new Vector2(0, -30);
            text.verticalAlignment = VerticalAlignmentOptions.Top;
            text.overflowMode = TextOverflowModes.Overflow;
            var height = Mathf.Max(30, text.GetPreferredValues(text.text, Mathf.Max(100, width), Mathf.Infinity).y + 8);
            text.rectTransform.sizeDelta = new Vector2(0, height);
            row.sizeDelta = new Vector2(0, height + 46);
            return top + height + 58;
        }

        TMP_InputField ProposalInput(RectTransform parent, string name, string hint, float top, float height, bool singleLine = false)
        {
            var block = ProposalRow(parent, name, top, height + 35, Color.white);
            var label = Text(block, name, 0, 1, 1, 0, 16, true);
            label.rectTransform.pivot = new Vector2(.5f, 1);
            label.rectTransform.sizeDelta = new Vector2(0, 28);
            var box = Surface(block, name + " input", 0, 0, 1, 0, Paper, true, true);
            box.pivot = new Vector2(.5f, 0);
            box.sizeDelta = new Vector2(0, height);
            var field = box.gameObject.AddComponent<TMP_InputField>();
            field.characterLimit = 2000;
            var viewport = Rect(box, "Text viewport", 0, 0, 1, 1);
            viewport.offsetMin = new Vector2(14, 12);
            viewport.offsetMax = new Vector2(-14, -12);
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var typed = Text(viewport, "", 0, 0, 1, 1, 18);
            var placeholder = Text(viewport, hint, 0, 0, 1, 1, 18, false, Muted);
            QuestBridgeComposer.Configure(field, typed, placeholder, viewport);
            field.lineType = singleLine ? TMP_InputField.LineType.SingleLine : TMP_InputField.LineType.MultiLineNewline;
            return field;
        }

        string ProposalScope(TaskRecord task)
        {
            if (OwnsTask(task)) return "?scope=demo-business&businessId=" + ProposalUrl(businessId);
            return isBusiness ? "" : "?scope=demo-team&teamId=" + ProposalUrl(activeTeamId);
        }

        void ShowProposals(TaskRecord task)
        {
            var pane = ProposalPane("Предложения команд", task.draft?.title ?? "Задача");
            var list = CreateScroll(pane, "Proposal list", .045f, .185f, .91f, .59f, out var scroll);
            var status = Text(pane, "Загружаем предложения…", .045f, .112f, .91f, .051f, 15, false, Muted);
            Button(pane, "К задаче", .045f, .034f, .23f, .061f, () => ShowTask(task), Paper);
            UnityEngine.UI.Button apply = null;
            QBProposal own = null;
            if (!isBusiness)
                apply = Button(pane, "Предложить решение", .62f, .034f, .335f, .061f,
                    () => { if (own != null) ShowProposal(task, own); else OpenProposalForm(task); }, null, true);
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                status.text = "Подключите сервер, чтобы отправлять и получать предложения.";
                if (apply) apply.interactable = false;
                return;
            }
            if (apply) apply.interactable = false;
            StartCoroutine(WatchProposals(task, pane, list, scroll, status, result =>
            {
                own = result.FirstOrDefault(p => p.teamId == activeTeamId);
                if (apply)
                {
                    apply.interactable = true;
                    SetButtonText(apply, own == null ? "Предложить решение" : "Мой отклик");
                }
            }));
        }

        IEnumerator WatchProposals(TaskRecord task, RectTransform pane, RectTransform list,
            UnityEngine.UI.ScrollRect scroll, TMP_Text status, Action<QBProposal[]> loaded)
        {
            string previous = null;
            while (ProposalPaneActive(pane))
            {
                yield return ApiRequest<QBProposalList>("GET", "/api/tasks/" + ProposalUrl(task.id) + "/proposals" + ProposalScope(task), null, response =>
                {
                    if (!ProposalPaneActive(pane)) return;
                    var proposals = response?.proposals ?? Array.Empty<QBProposal>();
                    loaded(proposals);
                    status.text = proposals.Length == 0 ? "Предложений пока нет. Команда может отправить первое." : proposals.Length + " предложений · обновляем каждые 5 секунд";
                    string fingerprint = JsonUtility.ToJson(response);
                    if (fingerprint == previous) return;
                    previous = fingerprint;
                    float offset = list.anchoredPosition.y;
                    Clear(list);
                    float top = 0;
                    foreach (var proposal in proposals)
                    {
                        var item = proposal;
                        var row = ProposalRow(list, "Proposal " + item.id, top, 172, Paper);
                        string teamName = item.team?.name ?? item.teamId;
                        Text(row, teamName, .035f, .71f, .70f, .22f, 21, true);
                        var badge = Text(row, ProposalDecisionLabel(item.decision), .70f, .71f, .265f, .22f, 16, true,
                            item.decision == "selected" ? Accent : Muted);
                        badge.alignment = TextAlignmentOptions.Right;
                        Text(row, item.details?.idea ?? "Описание предложения доступно бизнесу и автору.", .035f, .34f, .93f, .33f, 17);
                        Text(row, (item.team?.stack ?? "") + " · " + (item.team?.completed ?? 0) + " этапов", .035f, .075f, .61f, .2f, 15, false, Muted);
                        Button(row, "Подробнее", .75f, .073f, .215f, .225f, () => ShowProposal(task, item), Color.white);
                        top += 186;
                    }
                    list.sizeDelta = new Vector2(0, Mathf.Max(top, scroll.viewport.rect.height));
                    list.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0, Mathf.Max(0, top - scroll.viewport.rect.height)));
                }, error => { if (ProposalPaneActive(pane) && status) status.text = error; });
                if (!ProposalPaneActive(pane)) yield break;
                yield return new WaitForSecondsRealtime(5);
            }
        }

        void OpenProposalForm(TaskRecord task)
        {
            var pane = ProposalPane("Предложить решение", task.draft?.title ?? "Задача");
            var fields = CreateScroll(pane, "Proposal fields", .045f, .19f, .91f, .59f, out _);
            var idea = ProposalInput(fields, "Идея", "Как вы решите проблему бизнеса?", 0, 105);
            var plan = ProposalInput(fields, "План работы", "Основные шаги и результат каждого шага", 157, 110);
            var timeline = ProposalInput(fields, "Срок", "Когда покажете первый результат?", 319, 64);
            var link = ProposalInput(fields, "Ссылка на прототип или проект", "https://github.com/…", 430, 64, true);
            bool publicDetails = false;
            var privacyRow = ProposalRow(fields, "Proposal visibility", 546, 56, Color.white);
            UnityEngine.UI.Button privacy = null;
            privacy = Button(privacyRow, "Показывать детали только бизнесу", 0, 0, 1, 1, () =>
            {
                publicDetails = !publicDetails;
                SetButtonText(privacy, publicDetails ? "Детали видны другим командам ✓" : "Показывать детали только бизнесу");
            }, Blue);
            fields.sizeDelta = new Vector2(0, 614);
            var status = Text(pane, "Отправляете от команды: " + (snapshot?.teams?.FirstOrDefault(t => t.id == activeTeamId)?.name ?? activeTeamId),
                .045f, .112f, .91f, .055f, 15, false, Muted);
            Button(pane, "Назад", .045f, .034f, .23f, .061f, () => ShowProposals(task), Paper);
            UnityEngine.UI.Button send = null;
            send = Button(pane, "Отправить предложение", .60f, .034f, .355f, .061f, () =>
            {
                if (new[] { idea.text, plan.text, timeline.text, link.text }.Any(string.IsNullOrWhiteSpace))
                { status.text = "Заполните идею, план, срок и ссылку на проект."; return; }
                if (!Uri.TryCreate(link.text.Trim(), UriKind.Absolute, out var url) || (url.Scheme != "http" && url.Scheme != "https"))
                { status.text = "Ссылка должна начинаться с https:// или http://."; return; }
                send.interactable = false;
                status.text = "Отправляем предложение…";
                var body = new ProposalSubmitBody { teamId = activeTeamId, idea = idea.text.Trim(), plan = plan.text.Trim(), timeline = timeline.text.Trim(), prototypeUrl = link.text.Trim(), publishDetails = publicDetails };
                StartCoroutine(ApiRequest<QBProposal>("POST", "/api/tasks/" + ProposalUrl(task.id) + "/proposals", body, proposal =>
                {
                    StartCoroutine(RefreshCatalog());
                    Notify("Предложение отправлено бизнесу");
                    if (ProposalPaneActive(pane)) ShowProposal(task, proposal);
                }, error => { if (ProposalPaneActive(pane)) { status.text = error; send.interactable = true; } }));
            }, null, true);
        }

        void ShowProposal(TaskRecord task, QBProposal proposal)
        {
            var pane = ProposalPane(proposal.team?.name ?? "Предложение команды", ProposalDecisionLabel(proposal.decision) + " · " + (task.draft?.title ?? "Задача"));
            var body = CreateScroll(pane, "Proposal description", .045f, .21f, .91f, .565f, out var scroll);
            Canvas.ForceUpdateCanvases();
            float width = scroll.viewport.rect.width;
            float top = ProposalParagraph(body, "Стек команды", proposal.team?.stack, 0, width);
            if (proposal.details == null)
                top = ProposalParagraph(body, "Приватное предложение", "Команда открыла подробности только заказчику. Её профиль и статус участия доступны всем.", top, width);
            else
            {
                top = ProposalParagraph(body, "Идея", proposal.details.idea, top, width);
                top = ProposalParagraph(body, "План", proposal.details.plan, top, width);
                top = ProposalParagraph(body, "Срок", proposal.details.timeline, top, width);
                top = ProposalParagraph(body, "Прототип или проект", proposal.details.prototypeUrl, top, width);
                var urlRow = ProposalRow(body, "Prototype link", top, 45, Blue);
                Button(urlRow, "Открыть ссылку", 0, 0, 1, 1, () =>
                {
                    if (Uri.TryCreate(proposal.details.prototypeUrl, UriKind.Absolute, out var url) && (url.Scheme == "https" || url.Scheme == "http")) Application.OpenURL(url.AbsoluteUri);
                }, Blue);
                top += 65;
            }
            body.sizeDelta = new Vector2(0, top);
            var status = Text(pane, OwnsTask(task) ? "Можно выбрать несколько команд. Решение принимает бизнес." : "Решение по предложению принимает заказчик.", .045f, .125f, .91f, .053f, 15, false, Muted);
            Button(pane, "Все предложения", .045f, .034f, .26f, .061f, () => ShowProposals(task), Paper);
            bool owner = OwnsTask(task);
            bool decisionBusy = false;
            if (proposal.decision == "selected" && (owner || (!isBusiness && proposal.teamId == activeTeamId)))
                Button(pane, "Этапы работы", .67f, .034f, .285f, .061f, () => ShowMilestones(task, proposal), null, true);
            if (owner)
            {
                UnityEngine.UI.Button select = null, reject = null;
                Action<string> decide = decision =>
                {
                    decisionBusy = true;
                    if (select) select.interactable = false;
                    if (reject) reject.interactable = false;
                    status.text = "Сохраняем решение…";
                    StartCoroutine(ApiRequest<QBProposal>("PATCH", "/api/proposals/" + ProposalUrl(proposal.id) + "/decision", new ProposalDecisionBody { businessId = businessId, decision = decision }, result =>
                    {
                        StartCoroutine(RefreshCatalog());
                        Notify(decision == "selected" ? "Команда выбрана" : "Предложение отклонено");
                        if (ProposalPaneActive(pane)) ShowProposal(task, result);
                    }, error => { decisionBusy = false; if (ProposalPaneActive(pane)) { status.text = error; if (select) select.interactable = true; if (reject) reject.interactable = true; } }));
                };
                if (proposal.decision != "selected")
                    select = Button(pane, "Выбрать команду", .67f, .034f, .285f, .061f, () => decide("selected"), null, true);
                if (proposal.decision != "rejected")
                    reject = Button(pane, "Отклонить", .38f, .034f, .245f, .061f, () => decide("rejected"), Paper);
            }
            StartCoroutine(WatchProposalDecision(task, proposal, pane, status, () => decisionBusy));
        }

        IEnumerator WatchProposalDecision(TaskRecord task, QBProposal proposal, RectTransform pane, TMP_Text status, Func<bool> busy)
        {
            string path = "/api/tasks/" + ProposalUrl(task.id) + "/proposals" + ProposalScope(task);
            string normalStatus = status.text;
            while (ProposalPaneActive(pane))
            {
                yield return new WaitForSecondsRealtime(5);
                if (!ProposalPaneActive(pane)) yield break;
                if (busy()) continue;
                yield return ApiRequest<QBProposalList>("GET", path, null, result =>
                {
                    if (!ProposalPaneActive(pane) || busy()) return;
                    var current = result?.proposals?.FirstOrDefault(item => item.id == proposal.id);
                    if (current == null) return;
                    if (current.decision != proposal.decision)
                    {
                        Notify("Статус предложения: " + ProposalDecisionLabel(current.decision));
                        ShowProposal(task, current);
                    }
                    else status.text = normalStatus;
                }, error => { if (ProposalPaneActive(pane) && !busy()) status.text = error; });
            }
        }

        void ShowMilestones(TaskRecord task, QBProposal proposal)
        {
            bool owner = OwnsTask(task);
            bool ownTeam = !isBusiness && proposal.teamId == activeTeamId;
            var pane = ProposalPane("Этапы работы", proposal.team?.name ?? "Команда");
            var list = CreateScroll(pane, "Milestones", .045f, ownTeam ? .335f : .185f, .91f, ownTeam ? .44f : .59f, out var scroll);
            var status = Text(pane, "Загружаем этапы…", .045f, .11f, .91f, .055f, 15, false, Muted);
            Button(pane, "К предложению", .045f, .034f, .26f, .061f, () => ShowProposal(task, proposal), Paper);
            if (ownTeam)
            {
                var form = Rect(pane, "New milestone", .045f, .185f, .91f, .14f);
                var text = ProposalInput(form, "Готовый этап", "Что уже сделано? Укажите результат и ссылку при наличии.", 0, 60);
                UnityEngine.UI.Button send = null;
                send = Button(pane, "Передать на проверку", .60f, .034f, .355f, .061f, () =>
                {
                    if (string.IsNullOrWhiteSpace(text.text)) { status.text = "Опишите выполненный этап."; return; }
                    send.interactable = false;
                    status.text = "Передаём этап бизнесу…";
                    StartCoroutine(ApiRequest<QBMilestone>("POST", "/api/proposals/" + ProposalUrl(proposal.id) + "/milestones", new ProposalMilestoneBody { teamId = activeTeamId, description = text.text.Trim() }, milestone =>
                    {
                        Notify("Этап отправлен на подтверждение");
                        if (ProposalPaneActive(pane)) ShowMilestones(task, proposal);
                    }, error => { if (ProposalPaneActive(pane)) { status.text = error; send.interactable = true; } }));
                }, null, true);
            }
            StartCoroutine(WatchMilestones(task, proposal, pane, list, scroll, status, owner));
        }

        IEnumerator WatchMilestones(TaskRecord task, QBProposal proposal, RectTransform pane, RectTransform list,
            UnityEngine.UI.ScrollRect scroll, TMP_Text status, bool owner)
        {
            string previous = null;
            string scope = owner ? "?scope=demo-business&businessId=" + ProposalUrl(businessId) : "?scope=demo-team&teamId=" + ProposalUrl(activeTeamId);
            while (ProposalPaneActive(pane))
            {
                yield return ApiRequest<QBMilestoneList>("GET", "/api/proposals/" + ProposalUrl(proposal.id) + "/milestones" + scope, null, result =>
                {
                    if (!ProposalPaneActive(pane)) return;
                    var milestones = result?.milestones ?? Array.Empty<QBMilestone>();
                    status.text = milestones.Length == 0 ? "Этапов пока нет. Команда передаёт выполненную работу на проверку." : "Подтверждённый этап даёт команде +10 опыта.";
                    string fingerprint = JsonUtility.ToJson(result);
                    if (fingerprint == previous) return;
                    previous = fingerprint;
                    float offset = list.anchoredPosition.y;
                    Clear(list);
                    Canvas.ForceUpdateCanvases();
                    float top = 0;
                    foreach (var milestone in milestones)
                    {
                        var item = milestone;
                        var row = ProposalRow(list, "Milestone " + item.id, top, 150, Paper);
                        var description = Text(row, item.description, .035f, 1, .93f, 0, 18);
                        description.rectTransform.pivot = new Vector2(.5f, 1);
                        description.rectTransform.anchoredPosition = new Vector2(0, -16);
                        description.verticalAlignment = VerticalAlignmentOptions.Top;
                        description.overflowMode = TextOverflowModes.Overflow;
                        float height = Mathf.Max(60, description.GetPreferredValues(item.description, Mathf.Max(100, scroll.viewport.rect.width * .93f), Mathf.Infinity).y + 6);
                        description.rectTransform.sizeDelta = new Vector2(0, height);
                        row.sizeDelta = new Vector2(0, height + 82);
                        var state = Text(row, item.confirmed ? "Подтверждено · +" + item.experienceAwarded + " опыта" : "Ожидает подтверждения", .035f, 0, .59f, 0, 16, true, item.confirmed ? Accent : Muted);
                        state.rectTransform.pivot = new Vector2(.5f, 0);
                        state.rectTransform.anchoredPosition = new Vector2(0, 14);
                        state.rectTransform.sizeDelta = new Vector2(0, 42);
                        if (owner && !item.confirmed)
                        {
                            UnityEngine.UI.Button confirm = null;
                            confirm = Button(row, "Подтвердить", .67f, 0, .295f, 0, () =>
                            {
                                confirm.interactable = false;
                                status.text = "Подтверждаем результат…";
                                StartCoroutine(ApiRequest<QBMilestone>("POST", "/api/milestones/" + ProposalUrl(item.id) + "/confirm", new ProposalConfirmBody { businessId = businessId }, updated =>
                                {
                                    StartCoroutine(RefreshCatalog());
                                    Notify("Этап подтверждён · команде +" + updated.experienceAwarded + " опыта");
                                    if (ProposalPaneActive(pane)) ShowMilestones(task, proposal);
                                }, error => { if (ProposalPaneActive(pane)) { status.text = error; if (confirm) confirm.interactable = true; } }));
                            }, Blue);
                            var confirmRect = (RectTransform)confirm.transform;
                            confirmRect.pivot = new Vector2(.5f, 0);
                            confirmRect.anchoredPosition = new Vector2(0, 14);
                            confirmRect.sizeDelta = new Vector2(0, 42);
                        }
                        top += height + 96;
                    }
                    list.sizeDelta = new Vector2(0, Mathf.Max(top, scroll.viewport.rect.height));
                    list.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0, Mathf.Max(0, top - scroll.viewport.rect.height)));
                }, error => { if (ProposalPaneActive(pane) && status) status.text = error; });
                if (!ProposalPaneActive(pane)) yield break;
                yield return new WaitForSecondsRealtime(5);
            }
        }
    }
}
