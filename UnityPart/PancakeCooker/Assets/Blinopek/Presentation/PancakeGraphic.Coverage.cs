using Blinopek.Domain;
using UnityEngine;

namespace Blinopek.Presentation
{
    public sealed partial class PancakeGraphic
    {
        private readonly SoftCoverage[] coverage = { new SoftCoverage(), new SoftCoverage(), new SoftCoverage(), new SoftCoverage() };

        private void RefreshCoverage()
        {
            coverage[0].Refresh(motion.State.Batter);
            for (int i = 0; i < 3; i++) coverage[i + 1].Refresh(motion.State.Fillings[i]?.Mask);
        }

        private bool SoftEdge(int x, int y)
        {
            for (int i = 0; i < coverage.Length; i++) if (coverage[i].IsEdge(x, y)) return true;
            return false;
        }

        private Color CoverageTint(Quad quad, Vector2 uv)
        {
            var tint = quad.Tint;
            float alpha = coverage[0].Sample(uv);
            if (quad.CoverageLayer > 0) alpha = Mathf.Min(alpha, coverage[quad.CoverageLayer].Sample(uv));
            tint.a *= alpha;
            return tint;
        }

        // Draw the original circular stamps, independent of the coarse scoring mask.
        // Apply only newly added stamps, keeping the narrow feather at a higher resolution.
        private sealed class SoftCoverage
        {
            public const int Size = 256;
            private const int Stride = Size + 1;
            private const float Feather = .65f;
            private CoverageMask mask;
            private int revision = -1;
            private int paintedStamps;
            private float[] alpha;
            private byte[] cells;
            private static readonly float[] circle = CircleCoverage();

            public void Refresh(CoverageMask value)
            {
                if (mask == value && (value == null || revision == value.Revision)) return;
                if (mask != value)
                {
                    paintedStamps = 0;
                    if (alpha != null) { System.Array.Clear(alpha, 0, alpha.Length); System.Array.Clear(cells, 0, cells.Length); }
                }
                mask = value;
                if (mask == null) { revision = -1; return; }
                revision = mask.Revision;
                if (alpha == null) { alpha = new float[Stride * Stride]; cells = new byte[Size * Size]; }
                int left = Size, bottom = Size, right = -1, top = -1;
                for (; paintedStamps < mask.Stamps.Count; paintedStamps++)
                {
                    var stamp = mask.Stamps[paintedStamps];
                    float cx = stamp.X * (Size / 1000f), cy = stamp.Y * (Size / 1000f), radius = stamp.Radius * (Size / 1000f);
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius - Feather)), x1 = Mathf.Min(Size, Mathf.CeilToInt(cx + radius + Feather));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius - Feather)), y1 = Mathf.Min(Size, Mathf.CeilToInt(cy + radius + Feather));
                    left = Mathf.Min(left, x0); bottom = Mathf.Min(bottom, y0); right = Mathf.Max(right, x1); top = Mathf.Max(top, y1);
                    for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                    {
                        int index = y * Stride + x;
                        if (alpha[index] >= circle[index]) continue;
                        float dx = x - cx, dy = y - cy;
                        float opacity = Mathf.Min(circle[index], EdgeAlpha(radius - Mathf.Sqrt(dx * dx + dy * dy)));
                        alpha[index] = Mathf.Max(alpha[index], opacity);
                    }
                }
                // A changed vertex also belongs to the cell immediately below and left.
                for (int y = Mathf.Max(0, bottom - 1); y <= Mathf.Min(Size - 1, top); y++)
                for (int x = Mathf.Max(0, left - 1); x <= Mathf.Min(Size - 1, right); x++)
                {
                    int corner = y * Stride + x;
                    float sum = alpha[corner] + alpha[corner + 1] + alpha[corner + Stride] + alpha[corner + Stride + 1];
                    cells[y * Size + x] = (byte)(sum == 0 ? 0 : sum == 4 ? 1 : 2);
                }
            }

            private static float EdgeAlpha(float distance) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-Feather, Feather, distance));
            private static float[] CircleCoverage()
            {
                var result = new float[Stride * Stride];
                for (int y = 0; y <= Size; y++) for (int x = 0; x <= Size; x++)
                {
                    float dx = x - Size * .5f, dy = y - Size * .5f;
                    result[y * Stride + x] = EdgeAlpha(Size * .5f - Mathf.Sqrt(dx * dx + dy * dy));
                }
                return result;
            }

            public bool Visible(int x, int y) => mask != null && cells[y * Size + x] != 0;
            public bool IsEdge(int x, int y) => mask != null && cells[y * Size + x] == 2;
            public float Sample(Vector2 uv)
            {
                if (mask == null || uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) return 0;
                float x = uv.x * Size, y = uv.y * Size;
                int ix = Mathf.Min((int)x, Size - 1), iy = Mathf.Min((int)y, Size - 1), corner = iy * Stride + ix;
                return Mathf.Lerp(Mathf.Lerp(alpha[corner], alpha[corner + 1], x - ix),
                    Mathf.Lerp(alpha[corner + Stride], alpha[corner + Stride + 1], x - ix), y - iy);
            }
        }
    }
}
