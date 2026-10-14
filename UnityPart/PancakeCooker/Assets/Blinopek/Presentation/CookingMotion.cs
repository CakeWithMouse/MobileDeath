using Blinopek.Domain;
using UnityEngine;

namespace Blinopek.Presentation
{
    // Presentation time is deliberately separate from the deterministic, saved simulation.
    public sealed class CookingMotion
    {
        public const float FoldSeconds = .62f;
        public const float FlipSeconds = .68f;
        public readonly PancakeState State;
        public float FoldPosition { get; private set; }
        public float FlipPosition { get; private set; } = 1;
        public float HeatA { get; private set; }
        public float HeatB { get; private set; }
        public float Time { get; private set; }
        private PancakeStage previousStage;
        public bool Turning => FlipPosition < 1;
        public bool Folding => FoldPosition < State.Folds.Count;
        public bool Busy => Turning || Folding;

        public CookingMotion(PancakeState state)
        {
            State = state; previousStage = state.Stage; FoldPosition = state.Folds.Count;
            HeatA = state.HeatA; HeatB = state.HeatB;
        }
        public void Observe()
        {
            if (previousStage == PancakeStage.SideA && State.Stage == PancakeStage.SideB) FlipPosition = 0;
            previousStage = State.Stage;
        }
        public void Advance(float seconds)
        {
            Observe();
            seconds = Mathf.Clamp(seconds, 0, .1f);
            Time += seconds;
            FoldPosition = Mathf.MoveTowards(FoldPosition, State.Folds.Count, seconds / FoldSeconds);
            FlipPosition = Mathf.MoveTowards(FlipPosition, 1, seconds / FlipSeconds);
            HeatA = Mathf.MoveTowards(HeatA, State.HeatA, seconds * 80);
            HeatB = Mathf.MoveTowards(HeatB, State.HeatB, seconds * 80);
        }
    }

    public struct CookingVertex
    {
        public Vector3 Position;
        public float Facing;
        public CookingVertex(Vector3 position, float facing) { Position = position; Facing = facing; }
    }

    public static class CookingDeformation
    {
        public static float Ease(float t) => t * t * (3 - 2 * t);

        public static CookingVertex FoldPoint(Vector2 uv, PancakeState state, float progress)
        {
            var p = new Vector3(uv.x, uv.y, 0); float facing = 1;
            int rollSteps = 0;
            if (state.IntendedFold == Fold.Roll)
                while (rollSteps < state.Folds.Count && state.Folds[rollSteps] == Direction.Down) rollSteps++;
            if (rollSteps > 0 && progress > 0)
            {
                float rolled = Mathf.Min(progress, rollSteps);
                rolled = Mathf.Floor(rolled) + Ease(rolled - Mathf.Floor(rolled));
                float edge = 1 - Mathf.Clamp01(rolled / 3) * .98f;
                const float radius = .055f;
                if (p.y > edge)
                {
                    float angle = (p.y - edge) / radius;
                    float thickness = (p.y - edge) * .012f;
                    p.y = edge + Mathf.Sin(angle) * (radius + thickness);
                    p.z = (1 - Mathf.Cos(angle)) * (radius + thickness);
                    facing = Mathf.Cos(angle);
                }
            }
            // Incorrect later gestures bend the already rolled surface without resetting it.
            for (int i = rollSteps; i < state.Folds.Count && i < progress; i++)
            {
                var direction = state.Folds[i];
                bool horizontal = direction == Direction.Left || direction == Direction.Right;
                bool low = direction == Direction.Up || direction == Direction.Right;
                float hinge = state.IntendedFold == Fold.Envelope ? (low ? .28f : .72f) : .5f;
                float coordinate = horizontal ? p.x : p.y, distance = coordinate - hinge;
                if (low ? distance >= 0 : distance <= 0) continue;
                float turn = Mathf.Clamp01(progress - i), angle = Ease(turn) * Mathf.PI;
                // Exact endpoints avoid hairline cracks between coincident folded strips.
                float c = turn == 1 ? -1 : Mathf.Cos(angle), s = turn == 1 ? 0 : Mathf.Sin(angle), sign = low ? -1 : 1;
                float rotated = hinge + distance * c + sign * p.z * s;
                p.z = Mathf.Abs(distance) * s + p.z * c + (1 - c) * .0015f * (i + 1);
                if (horizontal) p.x = rotated; else p.y = rotated;
                facing *= c;
            }
            // Let the finished package settle toward the plate centre as its footprint shrinks.
            p.y += .42f * Mathf.Clamp01(Mathf.Min(progress, rollSteps) / 3);
            for (int i = rollSteps; i < state.Folds.Count && i < progress; i++)
            {
                float distance = (state.IntendedFold == Fold.Envelope ? .10f : .23f) * Ease(Mathf.Clamp01(progress - i));
                switch (state.Folds[i])
                {
                    case Direction.Up: p.y -= distance; break;
                    case Direction.Down: p.y += distance; break;
                    case Direction.Right: p.x -= distance; break;
                    case Direction.Left: p.x += distance; break;
                }
            }
            return new CookingVertex(p, facing);
        }
    }
}
