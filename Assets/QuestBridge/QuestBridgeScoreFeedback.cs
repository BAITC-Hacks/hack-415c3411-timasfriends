using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace QuestBridge
{
    public sealed partial class QuestBridgeApp
    {
        ScorePreview latestPreview;
        ScorePreview confirmedPersonalPreview;
        int latestPreviewRevision=-1, confirmedDisplayScore, scoreAnimationSerial;
        TMP_Text scoreForecast;
        RectTransform scoreFeedbackPanel;
        readonly Dictionary<string,int> rewardedFieldPoints=new();

        void LoadScoreFeedbackState()
        {
            try{var json=PlayerPrefs.GetString("QuestBridge.ConfirmedDraftScore","");if(json.Length>0)confirmedPersonalPreview=JsonUtility.FromJson<ScorePreview>(json);}
            catch(ArgumentException){confirmedPersonalPreview=null;}
        }
        void ResetScoreFeedbackState()
        {
            confirmedPersonalPreview=null;PlayerPrefs.DeleteKey("QuestBridge.ConfirmedDraftScore");
        }

        void InitializeScoreFeedback(RectTransform panel, TaskRecord existing)
        {
            scoreFeedbackPanel=panel;latestPreview=null;latestPreviewRevision=-1;scoreAnimationSerial++;
            var personal=editingPersonal?confirmedPersonalPreview:null;
            confirmedDisplayScore=personal?.readiness??existing?.readiness??0;rewardedFieldPoints.Clear();
            foreach(var row in personal?.scoreBreakdown??existing?.scoreBreakdown??Array.Empty<ScoreRow>())rewardedFieldPoints[row.field]=row.points;
            editorScore.text=confirmedDisplayScore+" / 100";
            scoreForecast=Text(panel,"Расчёт заполненных полей…",.07f,.705f,.86f,.055f,15,false,Muted);
        }
        void RefreshScoreForecast()
        {
            if(!scoreForecast)return;
            if(!ApprovedPreview()||latestPreview==null||latestPreviewRevision!=editorRevision)
            {
                scoreForecast.text=!ValidBaseUrl()?"Подключите сервер для проверки":previewError.Length>0?"Проверка не завершена":editorValidation?.status=="rejected"?"Исправьте отмеченные поля":editorValidation?.status=="unavailable"?"Проверка недоступна":"Проверяем содержание…";
                if(confirmDraftButton)confirmDraftButton.interactable=false;
                return;
            }
            int delta=latestPreview.readiness-confirmedDisplayScore;
            scoreForecast.text=draftConfirmed?"Сведения подтверждены":delta>0?"После подтверждения: +"+delta:delta<0?"После подтверждения: "+latestPreview.readiness+" / 100":"Подтвердите заполненные поля";
            confirmDraftButton.interactable=!publishing&&!draftConfirmed&&ApprovedPreview();
        }
        void ConfirmDraftWithFeedback()
        {
            if(draftConfirmed||publishing)return;
            if(!ApprovedPreview()||latestPreview==null||latestPreviewRevision!=editorRevision)
            {if(editorStatus)editorStatus.text="Подтверждение доступно после успешной проверки содержания.";return;}
            draftConfirmed=true;SetButtonText(confirmDraftButton,"Сведения подтверждены");
            confirmDraftButton.interactable=false;UpdatePublishButton();
            var gains=new List<ScoreRow>();
            foreach(var row in latestPreview.scoreBreakdown??Array.Empty<ScoreRow>())
            {
                rewardedFieldPoints.TryGetValue(row.field,out int previous);
                if(row.points!=previous)gains.Add(new ScoreRow{field=row.field,points=row.points-previous});
                rewardedFieldPoints[row.field]=row.points;
            }
            if(editingPersonal)
            {
                confirmedPersonalPreview=JsonUtility.FromJson<ScorePreview>(JsonUtility.ToJson(latestPreview));
                PlayerPrefs.SetString("QuestBridge.ConfirmedDraftScore",JsonUtility.ToJson(confirmedPersonalPreview));PlayerPrefs.Save();
            }
            int start=confirmedDisplayScore;confirmedDisplayScore=latestPreview.readiness;RefreshScoreForecast();
            int serial=++scoreAnimationSerial;
            StartCoroutine(AnimateConfirmedScore(start,confirmedDisplayScore,serial,editorGeneration));
            if(gains.Count>0){if(gains.Any(row=>row.points>0))Play(riseClip);StartCoroutine(ScorePopups(gains,editorGeneration,serial));}
        }
        IEnumerator AnimateConfirmedScore(int from,int to,int serial,int generation)
        {
            for(float t=0;t<.65f;t+=Time.unscaledDeltaTime)
            {
                if(!editorOpen||generation!=editorGeneration||serial!=scoreAnimationSerial||!editorScore)yield break;
                float p=Mathf.Clamp01(t/.65f);p=1-Mathf.Pow(1-p,3);
                editorScore.text=Mathf.RoundToInt(Mathf.Lerp(from,to,p))+" / 100";
                editorScore.color=to<from?Color.Lerp(Hex(0xB6534A),Accent,p):Accent;
                editorScore.transform.localScale=Vector3.one*(1+.09f*Mathf.Sin(p*Mathf.PI));yield return null;
            }
            if(editorScore&&generation==editorGeneration&&serial==scoreAnimationSerial){editorScore.text=to+" / 100";editorScore.color=Accent;editorScore.transform.localScale=Vector3.one;}
        }
        IEnumerator ScorePopups(List<ScoreRow> gains,int generation,int serial)
        {
            foreach(var row in gains)
            {
                if(!editorOpen||generation!=editorGeneration||serial!=scoreAnimationSerial||!scoreFeedbackPanel)yield break;
                bool positive=row.points>0;
                var popup=Surface(scoreFeedbackPanel,"Confirmed points",.06f,.46f,.88f,.062f,positive?Blue:Hex(0xFFF0ED));
                var group=popup.gameObject.AddComponent<CanvasGroup>();group.blocksRaycasts=false;
                Text(popup,(positive?"+":"")+row.points+"  "+ShortLabel(row.field),.05f,0,.90f,1,15,true,positive?Accent:Hex(0xB6534A)).alignment=TextAlignmentOptions.Center;
                StartCoroutine(FloatScorePopup(popup,group,generation,serial));yield return new WaitForSecondsRealtime(.30f);
            }
        }
        IEnumerator FloatScorePopup(RectTransform popup,CanvasGroup group,int generation,int serial)
        {
            Vector2 start=popup.anchoredPosition;
            for(float t=0;t<1.15f;t+=Time.unscaledDeltaTime)
            {
                if(!popup||!editorOpen||generation!=editorGeneration||serial!=scoreAnimationSerial){if(popup)Destroy(popup.gameObject);yield break;}
                float p=t/1.15f;popup.anchoredPosition=start+Vector2.up*(30+150*p);
                group.alpha=Mathf.Min(1,p*10)*Mathf.Clamp01((1-p)*4);yield return null;
            }
            if(popup)Destroy(popup.gameObject);
        }
    }
}
