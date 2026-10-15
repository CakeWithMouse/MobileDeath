using UnityEngine;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    // Clip a full-size rounded fill; low patience never squashes its rounded end.
    public sealed class PatienceFillGraphic : Image
    {
        private const int ArcSteps = 16;
        private float fraction = 1;
        public float Fraction
        {
            get => fraction;
            set { float next = Mathf.Clamp01(value); if (next == fraction) return; fraction = next; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            if (fraction <= 0 || rect.width <= 0 || rect.height <= 0) return;
            float radius = Mathf.Min(rect.width, rect.height) * .5f;
            float right = rect.xMin + rect.width * fraction;
            // Convex capsule fan clipped at the progress boundary. Texture and end radii stay fixed.
            Add(vh, new Vector2((rect.xMin + right) * .5f, rect.center.y));
            var previous = CapsulePoint(2 * ArcSteps + 1, rect, radius);
            for (int i = 0; i < 2 * (ArcSteps + 1); i++) {
                var point = CapsulePoint(i, rect, radius);
                bool inside = point.x <= right, previousInside = previous.x <= right;
                if (inside != previousInside) Add(vh, Vector2.Lerp(previous, point, (right - previous.x) / (point.x - previous.x)));
                if (inside) Add(vh, point);
                previous = point;
            }
            for (int i = 1; i < vh.currentVertCount - 1; i++) vh.AddTriangle(0, i + 1, i);
            vh.AddTriangle(0, 1, vh.currentVertCount - 1);
        }

        private static Vector2 CapsulePoint(int index, Rect rect, float radius)
        {
            bool left = index <= ArcSteps;
            int step = left ? index : index - ArcSteps - 1;
            float angle = ((left ? 90 : 270) + 180f * step / ArcSteps) * Mathf.Deg2Rad;
            return new Vector2((left ? rect.xMin + radius : rect.xMax - radius) + Mathf.Cos(angle) * radius,
                rect.center.y + Mathf.Sin(angle) * radius);
        }

        private void Add(VertexHelper vh, Vector2 position) => vh.AddVert(position, color, Vector2.zero);
    }
}
