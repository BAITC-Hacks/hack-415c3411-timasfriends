using UnityEngine;

namespace QuestBridge
{
    /// <summary>Fits the original logo inside its UI rectangle without changing layout.</summary>
    public sealed class QuestBridgeLogoGraphic : UnityEngine.UI.RawImage
    {
        // Source-file ratio, deliberately not texture.width / texture.height:
        // Unity can resample a texture during import without changing its UV content.
        public float sourceAspect = 1.5f;

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertices)
        {
            vertices.Clear();
            var bounds = GetPixelAdjustedRect();
            var uv = uvRect;
            if (bounds.width <= 0 || bounds.height <= 0 || uv.width == 0 || uv.height == 0) return;

            float aspect = Mathf.Max(.001f, sourceAspect * Mathf.Abs(uv.width / uv.height));
            float width = Mathf.Min(bounds.width, bounds.height * aspect);
            float height = width / aspect;
            float left = bounds.center.x - width * .5f;
            float right = left + width;
            float bottom = bounds.center.y - height * .5f;
            float top = bottom + height;
            Color32 tint = color;

            vertices.AddVert(new Vector3(left, bottom, 0), tint, new Vector2(uv.xMin, uv.yMin));
            vertices.AddVert(new Vector3(left, top, 0), tint, new Vector2(uv.xMin, uv.yMax));
            vertices.AddVert(new Vector3(right, top, 0), tint, new Vector2(uv.xMax, uv.yMax));
            vertices.AddVert(new Vector3(right, bottom, 0), tint, new Vector2(uv.xMax, uv.yMin));
            vertices.AddTriangle(0, 1, 2);
            vertices.AddTriangle(2, 3, 0);
        }
    }
}
