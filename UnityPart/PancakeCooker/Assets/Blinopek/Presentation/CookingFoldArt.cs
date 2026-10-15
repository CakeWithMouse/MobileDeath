using Blinopek.Domain;
using UnityEngine;

namespace Blinopek.Presentation
{
    public enum FoldPose { Round, Half, Triangle, EnvelopeLeft, EnvelopeSides, EnvelopeTop, Envelope, RollStart, RollMiddle, Roll, Incorrect }
    public struct FoldSample
    {
        public Vector2 Source;
        public bool Inside;
        public FoldSample(Vector2 uv, bool inside) { Source = InCircle(uv); Inside = inside; }
        private static Vector2 InCircle(Vector2 p)
        {
            var delta = p - Vector2.one * .5f;
            return Vector2.one * .5f + Vector2.ClampMagnitude(delta, .455f);
        }
    }

    // Authored key poses retain their painted silhouette. UV correspondence describes the
    // original pancake layers beneath each pose, so coverage and fillings remain live data.
    public static class CookingFoldArt
    {
        public static readonly string[] Keys = { "fold-half", "fold-triangle", "fold-envelope-partial", "fold-envelope", "fold-roll-partial", "fold-roll", "fold-incorrect" };
        public static int ArtIndex(FoldPose pose)
        {
            switch (pose) {
                case FoldPose.Half: return 0;
                case FoldPose.Triangle: return 1;
                case FoldPose.EnvelopeLeft: case FoldPose.EnvelopeSides: return 2;
                case FoldPose.EnvelopeTop: case FoldPose.Envelope: return 3;
                case FoldPose.RollStart: case FoldPose.RollMiddle: return 4;
                case FoldPose.Roll: return 5;
                case FoldPose.Incorrect: return 6;
                default: return -1;
            }
        }
        public static FoldPose Pose(PancakeState state, int step)
        {
            if (step <= 0) return FoldPose.Round;
            var expected = Recipes.Gestures(state.IntendedFold);
            for (int i = 0; i < step && i < state.Folds.Count; i++)
                if (i >= expected.Length || state.Folds[i] != expected[i]) return FoldPose.Incorrect;
            switch (state.IntendedFold) {
                case Fold.Triangle: return step == 1 ? FoldPose.Half : FoldPose.Triangle;
                case Fold.Envelope: return step == 1 ? FoldPose.EnvelopeLeft : step == 2 ? FoldPose.EnvelopeSides : step == 3 ? FoldPose.EnvelopeTop : FoldPose.Envelope;
                case Fold.Roll: return step == 1 ? FoldPose.RollStart : step == 2 ? FoldPose.RollMiddle : FoldPose.Roll;
                default: return FoldPose.Incorrect;
            }
        }
        public static FoldPose CanonicalPose(int art) => new[] { FoldPose.Half, FoldPose.Triangle, FoldPose.EnvelopeSides, FoldPose.Envelope, FoldPose.RollStart, FoldPose.Roll, FoldPose.Incorrect }[art];
        public static Rect ArtworkUv(FoldPose pose, Rect uv)
        {
            // The painted triangle folds face down, and the roll starts at the bottom.
            // Match the recipe gestures: fold up, then right; roll from the top down.
            if (pose == FoldPose.Half || pose == FoldPose.Triangle || pose == FoldPose.RollStart || pose == FoldPose.RollMiddle || pose == FoldPose.Roll)
                return new Rect(uv.x, uv.yMax, uv.width, -uv.height);
            return uv;
        }
        public static Rect Placement(FoldPose pose, float aspect, Vector2 canvasSize)
        {
            float width = pose == FoldPose.Round ? 1 : pose == FoldPose.Half ? .94f : pose == FoldPose.Triangle ? .56f : pose == FoldPose.Envelope ? .58f : pose == FoldPose.EnvelopeTop ? .58f : pose == FoldPose.Roll ? .86f : pose == FoldPose.Incorrect ? .87f : .94f;
            float height = pose == FoldPose.Round ? 1 : width / aspect * canvasSize.x / canvasSize.y;
            if (pose == FoldPose.EnvelopeLeft || pose == FoldPose.EnvelopeSides) { height = .98f; width = height * aspect * canvasSize.y / canvasSize.x; }
            if (pose == FoldPose.RollMiddle) height *= .66f;
            return new Rect(.5f - width / 2, .5f - height / 2, width, height);
        }
        public static int LayerCount(FoldPose pose) => pose == FoldPose.Round ? 1 : pose == FoldPose.Triangle || pose == FoldPose.Envelope || pose == FoldPose.EnvelopeTop ? 4 : pose == FoldPose.Roll ? 3 : 2;
        public static FoldSample Sample(FoldPose pose, Vector2 uv, int layer = 0)
        {
            float x = uv.x, y = uv.y;
            if (pose == FoldPose.Round) return new FoldSample { Source = uv, Inside = true };
            if (pose == FoldPose.Half) return new FoldSample(new Vector2(x, layer == 0 ? .5f * (1 - y) : .5f + .5f * y), layer != 0);
            if (pose == FoldPose.Triangle)
            {
                float u = .43f * x, v = .43f * y;
                return new FoldSample(new Vector2(.5f + (layer % 2 == 0 ? u : -u), .5f + (layer < 2 ? v : -v)), layer % 2 != 0);
            }
            if (pose == FoldPose.EnvelopeLeft || pose == FoldPose.EnvelopeSides)
            {
                float left = .26f + .12f * Mathf.Sin(y * Mathf.PI), right = .76f - .12f * Mathf.Sin(y * Mathf.PI);
                if (layer == 0 && x < left) return new FoldSample(new Vector2(.28f * (1 - x / left), .04f + .92f * y), false);
                if (layer == 0 && x > right) return new FoldSample(new Vector2(.72f + .28f * (x - right) / (1 - right), .04f + .92f * y), false);
                return new FoldSample(new Vector2(.28f + .44f * x, .04f + .92f * y), true);
            }
            if (pose == FoldPose.Envelope || pose == FoldPose.EnvelopeTop)
            {
                if (layer == 0 && y > .28f + .3f * (1 - x)) return new FoldSample(new Vector2(.28f + .44f * x, .72f + .28f * (1 - y)), false);
                if (layer < 2 && x < .5f) return new FoldSample(new Vector2(.28f * (1 - x * 2), .28f + .44f * y), false);
                if (layer < 3 && x >= .5f) return new FoldSample(new Vector2(.72f + .28f * (x * 2 - 1), .28f + .44f * y), false);
                return new FoldSample(new Vector2(.28f + .44f * x, .28f + .44f * y), true);
            }
            if (pose == FoldPose.RollStart || pose == FoldPose.RollMiddle)
            {
                float seam = .73f - .08f * (1 - x);
                if (layer == 0 && y > seam) return new FoldSample(new Vector2(x, .94f - .2f * (1 - y) / (1 - seam)), false);
                return new FoldSample(new Vector2(x, .04f + .72f * Mathf.Clamp01(y / seam)), true);
            }
            if (pose == FoldPose.Roll) return new FoldSample(new Vector2(x, .68f - .3f * layer + .2f * y), layer == 1);
            bool flap = x < .28f + y * .32f;
            return new FoldSample(new Vector2(flap && layer == 0 ? .12f + x * .4f : x, y), !flap || layer != 0);
        }
    }
}
