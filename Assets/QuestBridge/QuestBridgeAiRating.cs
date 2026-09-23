using System;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace QuestBridge
{
    public sealed partial class QuestBridgeApp
    {
        // Place beside the separate readiness score. Recommended height: 180 logical pixels.
        RectTransform DrawAiRating(RectTransform parent, TaskRecord task, float x, float y, float w, float h)
        {
            var panel = Surface(parent, "AI clarity", x, y, w, h, Paper);
            bool available = task != null
                && string.Equals(task.aiMode, "live", StringComparison.Ordinal)
                && !float.IsNaN(task.clarity)
                && !float.IsInfinity(task.clarity)
                && task.clarity >= 0 && task.clarity <= 10;

            Text(panel, "Ясность · ИИ", .06f, .79f, .88f, .14f, 17, true);
            Text(panel,
                available ? task.clarity.ToString("0.0", CultureInfo.InvariantCulture) + " / 10" : "ИИ-оценка недоступна",
                .06f, .55f, .88f, .21f, available ? 24 : 16, true, available ? Accent : Muted);

            string reason = available
                ? (string.IsNullOrWhiteSpace(task.clarityReason) ? "Модель не передала объяснение оценки." : task.clarityReason.Trim())
                : "Полнота карточки учитывается независимо. Оценки ясности от модели пока нет.";

            var content = CreateScroll(panel, "Clarity explanation", .06f, .17f, .88f, .34f, out var scroll);
            var explanation = Text(content, reason, 0, 0, 1, 1, 14, false, Muted);
            explanation.alignment = TextAlignmentOptions.TopLeft;
            explanation.overflowMode = TextOverflowModes.Overflow;
            Canvas.ForceUpdateCanvases();
            float width = Mathf.Max(40, scroll.viewport.rect.width);
            float height = Mathf.Max(scroll.viewport.rect.height, explanation.GetPreferredValues(reason, width, Mathf.Infinity).y + 6);
            content.sizeDelta = new Vector2(0, height);
            QuestBridgeBrowserText.AttachSelectable(explanation);
            Canvas.ForceUpdateCanvases();scroll.verticalNormalizedPosition=1;

            Text(panel, "При равной полноте выше ясность.", .06f, .04f, .88f, .095f, 12, false, Muted);
            return panel;
        }
    }
}
