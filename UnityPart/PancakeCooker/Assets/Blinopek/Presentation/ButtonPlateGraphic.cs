using UnityEngine;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    // Slice the cropped artwork so painted corners keep the same scale on both axes.
    public sealed class ButtonPlateGraphic : RawImage
    {
        private float cornerSize, borderFraction = .35f;
        public float CornerSize { get => cornerSize; set { cornerSize = Mathf.Max(0, value); SetVerticesDirty(); } }
        public float BorderFraction { get => borderFraction; set { borderFraction = Mathf.Clamp(value, .01f, .49f); SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) { vh.Clear(); return; }
            if (texture == null || uvRect.height <= 0 || uvRect.width <= 0) {
                base.OnPopulateMesh(vh); return;
            }
            float sourceHeight = texture.height * uvRect.height;
            if (CornerSize > 0 || rect.width <= rect.height) {
                Frame(vh, rect, sourceHeight, CornerSize > 0 ? CornerSize : rect.width * .35f); return;
            }
            float sourceCap = Mathf.Min(sourceHeight * .5f, texture.width * uvRect.width * .5f);
            float cap = sourceCap * rect.height / sourceHeight;
            float uvCap = sourceCap / texture.width;
            vh.Clear();
            Strip(vh, rect.xMin, rect.xMin + cap, rect, uvRect.xMin, uvRect.xMin + uvCap);
            Strip(vh, rect.xMin + cap, rect.xMax - cap, rect, uvRect.xMin + uvCap, uvRect.xMax - uvCap);
            Strip(vh, rect.xMax - cap, rect.xMax, rect, uvRect.xMax - uvCap, uvRect.xMax);
        }

        private void Strip(VertexHelper vh, float left, float right, Rect rect, float uMin, float uMax)
            => Quad(vh, Rect.MinMaxRect(left, rect.yMin, right, rect.yMax), Rect.MinMaxRect(uMin, uvRect.yMin, uMax, uvRect.yMax));

        private void Frame(VertexHelper vh, Rect rect, float sourceHeight, float cornerSize)
        {
            float sourceBorder = Mathf.Min(sourceHeight, texture.width * uvRect.width) * BorderFraction;
            float corner = Mathf.Min(cornerSize, Mathf.Min(rect.width, rect.height) * .5f);
            float uBorder = sourceBorder / texture.width, vBorder = sourceBorder / texture.height;
            var x = new[] { rect.xMin, rect.xMin + corner, rect.xMax - corner, rect.xMax };
            var y = new[] { rect.yMin, rect.yMin + corner, rect.yMax - corner, rect.yMax };
            var u = new[] { uvRect.xMin, uvRect.xMin + uBorder, uvRect.xMax - uBorder, uvRect.xMax };
            var v = new[] { uvRect.yMin, uvRect.yMin + vBorder, uvRect.yMax - vBorder, uvRect.yMax };
            vh.Clear();
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                    Quad(vh, Rect.MinMaxRect(x[column], y[row], x[column + 1], y[row + 1]),
                        Rect.MinMaxRect(u[column], v[row], u[column + 1], v[row + 1]));
        }

        private void Quad(VertexHelper vh, Rect rect, Rect uv)
        {
            int first = vh.currentVertCount;
            vh.AddVert(new Vector3(rect.xMin, rect.yMin), color, new Vector2(uv.xMin, uv.yMin));
            vh.AddVert(new Vector3(rect.xMin, rect.yMax), color, new Vector2(uv.xMin, uv.yMax));
            vh.AddVert(new Vector3(rect.xMax, rect.yMax), color, new Vector2(uv.xMax, uv.yMax));
            vh.AddVert(new Vector3(rect.xMax, rect.yMin), color, new Vector2(uv.xMax, uv.yMin));
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first + 2, first + 3, first);
        }
    }
}
