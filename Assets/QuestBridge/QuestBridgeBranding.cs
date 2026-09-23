using TMPro;
using UnityEngine;

namespace QuestBridge
{
    public sealed partial class QuestBridgeApp
    {
        // Keep the supplied bitmap unchanged. A non-default UV rectangle can select
        // the logo region if its original file includes a surrounding white canvas.
        [SerializeField] UnityEngine.Rect bastamatLogoUV = new UnityEngine.Rect(.08f, .315f, .85f, .39f);

        RectTransform DrawBrand(RectTransform parent, float x, float y, float w, float h)
        {
            var brand = Rect(parent, "Bastamat brand", x, y, w, h);
            var original = Resources.Load<Texture2D>("BastamatLogo");
            if (original)
            {
                var picture = Rect(brand, "Original Bastamat logo", 0, 0, 1, 1);
                var graphic = picture.gameObject.AddComponent<UnityEngine.UI.RawImage>();
                graphic.texture = original;
                graphic.color = Color.white;
                graphic.raycastTarget = false;
                graphic.uvRect = bastamatLogoUV;
                var fit = picture.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
                fit.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = original.width * Mathf.Max(.001f, bastamatLogoUV.width)
                    / (original.height * Mathf.Max(.001f, bastamatLogoUV.height));
                return brand;
            }

            // A crisp UI fallback while the original attachment is unavailable.
            // Two document cards sit inside a blue bracket; all parts are decorative.
            var iconHolder = Rect(brand, "Bastamat mark holder", 0, .08f, .18f, .84f);
            var icon = Rect(iconHolder, "Bastamat mark", 0, 0, 1, 1);
            var iconFit = icon.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
            iconFit.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
            iconFit.aspectRatio = 1;
            var navy = Hex(0x34358D);
            var coral = Hex(0xEF6E60);
            Surface(icon, "Bracket left", .03f, .08f, .18f, .84f, navy, false);
            Surface(icon, "Bracket top", .03f, .75f, .55f, .17f, navy, false);
            Surface(icon, "Bracket bottom", .03f, .08f, .55f, .17f, navy, false);
            var backCard = Surface(icon, "Coral document", .36f, .39f, .42f, .47f, coral, false);
            backCard.localRotation = Quaternion.Euler(0, 0, 10);
            var frontCard = Surface(icon, "Blue document", .48f, .15f, .43f, .48f, navy, false);
            Surface(frontCard, "Document line one", .18f, .62f, .61f, .08f, Color.white, false);
            Surface(frontCard, "Document line two", .18f, .42f, .44f, .08f, Color.white, false);
            var wordmark = Text(brand, "Bastamat", .215f, 0, .785f, 1, 34, true, Ink);
            wordmark.alignment = TextAlignmentOptions.MidlineLeft;
            wordmark.overflowMode = TextOverflowModes.Overflow;
            wordmark.textWrappingMode = TextWrappingModes.NoWrap;
            return brand;
        }
    }
}
