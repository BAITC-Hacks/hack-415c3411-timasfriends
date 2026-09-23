using System;
using TMPro;
using UnityEngine;

namespace QuestBridge
{
    public sealed partial class QuestBridgeApp
    {
        void DrawRoleLanding()
        {
            DrawRoleHalf(false,()=>SetRole(true));
            DrawRoleHalf(true,()=>ShowRoleChoice(true));
            Surface(roleOverlay,"Role divider",.4996f,.11f,.0008f,.65f,Line,false);
            DrawBrand(roleOverlay,.405f,.862f,.19f,.082f);
            Text(roleOverlay,"Выберите вашу роль",.32f,.79f,.36f,.042f,17,false,Muted).alignment=TextAlignmentOptions.Center;
        }

        void DrawRoleHalf(bool team,Action choose)
        {
            var half=Surface(roleOverlay,team?"Team role":"Business role",team?.5f:0,0,.5f,1,Color.white,false,true);
            var button=half.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic=half.GetComponent<UnityEngine.UI.Image>();
            button.transition=UnityEngine.UI.Selectable.Transition.None;
            button.onClick.AddListener(()=>{QuestBridgeBrowserText.CloseActive();Play(openClip);choose();});

            var markHolder=Rect(half,"Role symbol holder",.385f,.55f,.23f,.185f);
            var mark=Rect(markHolder,"Role symbol",0,0,1,1);
            var fit=mark.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
            fit.aspectMode=UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=1;
            var halo=Surface(mark,"Symbol background",0,0,1,1,Blue,false).GetComponent<UnityEngine.UI.Image>();halo.sprite=circle;
            if(team)DrawTeamRoleMark(mark);else DrawBusinessRoleMark(mark);

            var title=Text(half,team?"Команда":"Бизнес",.12f,.424f,.76f,.094f,52,true);
            title.alignment=TextAlignmentOptions.Center;
            var description=Text(half,team?"Найдите задачу и предложите\nсвоё решение":"Опишите задачу и найдите\nкоманду для её решения",.12f,.31f,.76f,.09f,21,false,Muted);
            description.alignment=TextAlignmentOptions.Center;
            var action=Text(half,"Продолжить →",.2f,.202f,.6f,.05f,17,true,Muted);
            action.alignment=TextAlignmentOptions.Center;
            var marker=Surface(half,"Selection underline",.41f,.18f,.18f,.003f,new Color(Accent.r,Accent.g,Accent.b,0),false).GetComponent<UnityEngine.UI.Image>();
            var hover=half.gameObject.AddComponent<QuestBridgeRoleHover>();
            hover.surface=half.GetComponent<UnityEngine.UI.Image>();hover.illustration=markHolder;
            hover.title=title;hover.actionLabel=action;hover.marker=marker;
        }

        void DrawBusinessRoleMark(RectTransform mark)
        {
            Surface(mark,"Handle",.35f,.64f,.30f,.18f,Ink);
            Surface(mark,"Handle opening",.415f,.645f,.17f,.105f,Blue);
            Surface(mark,"Briefcase",.14f,.22f,.72f,.46f,Ink);
            Surface(mark,"Case seam",.16f,.45f,.68f,.018f,Blue,false);
            Surface(mark,"Case clasp",.455f,.415f,.09f,.09f,Accent);
        }

        void DrawTeamRoleMark(RectTransform mark)
        {
            RoleHead(mark,"Left member",.13f,.60f,.205f,Ink);
            RoleHead(mark,"Right member",.665f,.60f,.205f,Ink);
            Surface(mark,"Left shoulders",.055f,.285f,.37f,.275f,Ink);
            Surface(mark,"Right shoulders",.575f,.285f,.37f,.275f,Ink);
            RoleHead(mark,"Centre member",.365f,.525f,.27f,Accent);
            Surface(mark,"Centre outline",.235f,.14f,.53f,.355f,Blue);
            Surface(mark,"Centre shoulders",.275f,.155f,.45f,.285f,Accent);
        }

        void RoleHead(RectTransform parent,string name,float x,float y,float size,Color color)
        {
            Surface(parent,name,x,y,size,size,color,false).GetComponent<UnityEngine.UI.Image>().sprite=circle;
        }
    }
}
