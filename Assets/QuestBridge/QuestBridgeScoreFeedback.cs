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
        int latestPreviewRevision=-1, confirmedDisplayScore, scoreAnimationSerial;
        TMP_Text scoreForecast;
        RectTransform scoreFeedbackPanel;
        readonly Dictionary<string,int> rewardedFieldPoints=new();

        void InitializeScoreFeedback(RectTransform panel, TaskRecord existing)
        {
            scoreFeedbackPanel=panel;latestPreview=null;latestPreviewRevision=-1;scoreAnimationSerial++;
            confirmedDisplayScore=existing?.readiness??0;rewardedFieldPoints.Clear();
            foreach(var row in existing?.scoreBreakdown??Array.Empty<ScoreRow>())rewardedFieldPoints[row.field]=row.points;
            editorScore.text=confirmedDisplayScore+" / 100";
            scoreForecast=Text(panel,"Расчёт заполненных полей…",.07f,.705f,.86f,.055f,15,false,Muted);
        }
        void RefreshScoreForecast()
        {
            if(!scoreForecast)return;
            if(latestPreview==null||latestPreviewRevision!=editorRevision){scoreForecast.text="Обновляем расчёт…";return;}
            int delta=latestPreview.readiness-confirmedDisplayScore;
            scoreForecast.text=draftConfirmed?"Сведения подтверждены":delta>0?"После подтверждения: +"+delta:delta<0?"После подтверждения: "+latestPreview.readiness+" / 100":"Подтвердите заполненные поля";
            confirmDraftButton.interactable=!publishing&&!draftConfirmed;
        }
        void ConfirmDraftWithFeedback()
        {
            if(draftConfirmed||publishing)return;
            if(latestPreview==null||latestPreviewRevision!=editorRevision)
            {if(editorStatus)editorStatus.text="Дождитесь расчёта заполненных полей.";return;}
            draftConfirmed=true;SetButtonText(confirmDraftButton,"Сведения подтверждены");
            confirmDraftButton.interactable=false;UpdatePublishButton();
            var gains=new List<ScoreRow>();
            foreach(var row in latestPreview.scoreBreakdown??Array.Empty<ScoreRow>())
            {
                rewardedFieldPoints.TryGetValue(row.field,out int previous);
                if(row.points>previous)gains.Add(new ScoreRow{field=row.field,points=row.points-previous});
                rewardedFieldPoints[row.field]=Math.Max(previous,row.points);
            }
            int start=confirmedDisplayScore;confirmedDisplayScore=latestPreview.readiness;RefreshScoreForecast();
            int serial=++scoreAnimationSerial;
            StartCoroutine(AnimateConfirmedScore(start,confirmedDisplayScore,serial,editorGeneration));
            if(gains.Count>0){Play(riseClip);StartCoroutine(ScorePopups(gains,editorGeneration,serial));}
        }
        IEnumerator AnimateConfirmedScore(int from,int to,int serial,int generation)
        {
            for(float t=0;t<.65f;t+=Time.unscaledDeltaTime)
            {
                if(!editorOpen||generation!=editorGeneration||serial!=scoreAnimationSerial||!editorScore)yield break;
                float p=Mathf.Clamp01(t/.65f);p=1-Mathf.Pow(1-p,3);
                editorScore.text=Mathf.RoundToInt(Mathf.Lerp(from,to,p))+" / 100";
                editorScore.transform.localScale=Vector3.one*(1+.09f*Mathf.Sin(p*Mathf.PI));yield return null;
            }
            if(editorScore&&generation==editorGeneration&&serial==scoreAnimationSerial){editorScore.text=to+" / 100";editorScore.transform.localScale=Vector3.one;}
        }
        IEnumerator ScorePopups(List<ScoreRow> gains,int generation,int serial)
        {
            foreach(var row in gains)
            {
                if(!editorOpen||generation!=editorGeneration||serial!=scoreAnimationSerial||!scoreFeedbackPanel)yield break;
                var popup=Surface(scoreFeedbackPanel,"Confirmed points",.06f,.46f,.88f,.062f,Blue);
                var group=popup.gameObject.AddComponent<CanvasGroup>();group.blocksRaycasts=false;
                Text(popup,"+"+row.points+"  "+ShortLabel(row.field),.05f,0,.90f,1,15,true,Accent).alignment=TextAlignmentOptions.Center;
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
