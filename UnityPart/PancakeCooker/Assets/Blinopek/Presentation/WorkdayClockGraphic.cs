using Blinopek.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    // The shift runs clockwise from nine to six across a fixed three-quarter sector.
    // The session owns time: tutorials, pauses and restored saves need no UI timer.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WorkdayClockGraphic : MaskableGraphic
    {
        private static readonly Color Face = new Color(.98f, .95f, .85f);
        private static readonly Color Ink = new Color(60 / 255f, 73 / 255f, 62 / 255f);
        private static readonly Color Track = new Color(.77f, .78f, .67f);
        private static readonly Color Green = new Color(87 / 255f, 111 / 255f, 89 / 255f);
        private static readonly Color Closing = new Color(.75f, .44f, .18f);
        private const float StartTurn = .75f;
        private const float ShiftSweep = .75f;
        public float Progress { get; private set; }
        private bool closed;

        public void Show(DaySession session)
        {
            raycastTarget = false;
            float next = session == null ? 0 : Mathf.Clamp01((float)session.ElapsedTicks / Mathf.Max(1, session.Rules.dayTicks));
            bool nextClosed = session != null && session.Phase != DayPhase.Serving;
            if (Progress == next && closed == nextClosed) return;
            Progress = next; closed = nextClosed; SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            var center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * .46f;
            if (radius <= 0) return;
            Color active = closed ? Closing : Green;
            Disc(vh, center + Vector2.down * radius * .045f, radius * 1.04f, new Color(Ink.r, Ink.g, Ink.b, .13f));
            Disc(vh, center, radius, Face);
            // Keep the whole working interval visible, including before the first tick.
            // The empty lower-left quarter separates opening from closing.
            Sector(vh, center, radius * .79f, ShiftSweep, new Color(Green.r, Green.g, Green.b, .18f), StartTurn);
            Arc(vh, center, radius * .89f, radius * .10f, ShiftSweep, Track, StartTurn);
            if (Progress > 0)
            {
                Sector(vh, center, radius * .79f, Progress * ShiftSweep, new Color(active.r, active.g, active.b, .13f), StartTurn);
                Arc(vh, center, radius * .89f, radius * .10f, Progress * ShiftSweep, active, StartTurn);
                Disc(vh, center + Direction(StartTurn + Progress * ShiftSweep) * radius * .89f, radius * .05f, active);
            }
            var boundary = new Color(Ink.r, Ink.g, Ink.b, .28f);
            Line(vh, center, center + Direction(StartTurn) * radius * .79f, radius * .025f, boundary);
            Line(vh, center, center + Direction(StartTurn + ShiftSweep) * radius * .79f, radius * .025f, boundary);
            for (int i = 1; i < 9; i++)
            {
                Vector2 direction = Direction(StartTurn + i / 12f);
                Line(vh, center + direction * radius * (i % 3 == 0 ? .63f : .68f),
                    center + direction * radius * .73f, radius * .032f, new Color(Ink.r, Ink.g, Ink.b, .5f));
            }
            // A hollow opening marker and a solid closing diamond remain distinct
            // even without colour or text labels.
            var start = center + Direction(StartTurn) * radius * .89f;
            var end = center + Direction(StartTurn + ShiftSweep) * radius * .89f;
            Disc(vh, start, radius * .095f, Green);
            Disc(vh, start, radius * .048f, Face);
            Quad(vh, end + Vector2.up * radius * .11f, end + Vector2.right * radius * .11f,
                end + Vector2.down * radius * .11f, end + Vector2.left * radius * .11f, Closing);
            var hand = Direction(StartTurn + Progress * ShiftSweep);
            Line(vh, center - hand * radius * .13f, center + hand * radius * .60f, radius * .065f, Ink);
            Disc(vh, center + hand * radius * .60f, radius * .0325f, Ink);
            Disc(vh, center, radius * .09f, Ink);
            Disc(vh, center, radius * .035f, Face);
        }

        private static Vector2 Direction(float turn)
        {
            float angle = turn * Mathf.PI * 2;
            return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
        }

        private static void Disc(VertexHelper vh, Vector2 center, float radius, Color tint)
            => Sector(vh, center, radius, 1, tint);

        private static void Sector(VertexHelper vh, Vector2 center, float radius, float turn, Color tint, float startTurn = 0)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(96 * turn));
            int first = vh.currentVertCount;
            vh.AddVert(center, tint, Vector2.zero);
            for (int i = 0; i <= steps; i++)
            {
                vh.AddVert(center + Direction(startTurn + turn * i / steps) * radius, tint, Vector2.zero);
                if (i > 0) vh.AddTriangle(first, first + i, first + i + 1);
            }
        }

        private static void Arc(VertexHelper vh, Vector2 center, float radius, float width, float turn, Color tint, float startTurn = 0)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(96 * turn));
            for (int i = 0; i < steps; i++)
            {
                var a = Direction(startTurn + turn * i / steps); var b = Direction(startTurn + turn * (i + 1) / steps);
                Quad(vh, center + a * (radius - width * .5f), center + a * (radius + width * .5f),
                    center + b * (radius + width * .5f), center + b * (radius - width * .5f), tint);
            }
        }

        private static void Line(VertexHelper vh, Vector2 start, Vector2 end, float width, Color tint)
        {
            var direction = (end - start).normalized;
            var side = new Vector2(-direction.y, direction.x) * width * .5f;
            Quad(vh, start - side, start + side, end + side, end - side, tint);
        }

        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            int first = vh.currentVertCount;
            vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero);
            vh.AddVert(c, tint, Vector2.zero); vh.AddVert(d, tint, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2); vh.AddTriangle(first + 2, first + 3, first);
        }
    }
}
