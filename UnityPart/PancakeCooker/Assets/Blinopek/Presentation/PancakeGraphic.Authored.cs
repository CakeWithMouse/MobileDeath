using Blinopek.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    public sealed partial class PancakeGraphic
    {
        public bool UsesAuthoredFolds => atlas != null && atlas.HasFoldArt && motion != null && motion.FoldPosition > 0;
        private FoldPose foldFrom, foldTo;
        private void RebuildAuthoredFolds()
        {
            float progress = motion.FoldPosition;
            int step = Mathf.FloorToInt(progress), next = Mathf.Min(step + 1, motion.State.Folds.Count);
            float t = CookingDeformation.Ease(progress - step);
            var direction = motion.State.Folds[Mathf.Min(step, motion.State.Folds.Count - 1)];
            float blend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.28f, .72f, t));
            foldFrom = CookingFoldArt.Pose(motion.State, step); foldTo = CookingFoldArt.Pose(motion.State, next);
            // Both skins share the interpolated silhouette. Keep the lower skin opaque:
            // an ordinary two-alpha dissolve makes the plate show through the pancake.
            if (blend < .999f) BuildPose(foldFrom, 1, t, false, direction);
            if (blend > 0) BuildPose(foldTo, blend, t, true, direction);
        }
        private void BuildPose(FoldPose pose, float opacity, float warp, bool incoming, Direction direction, Rect? region = null)
        {
            if (opacity <= .001f) return;
            // The one-sided and three-sided poses are layered compositions of the same source
            // artwork; the two-sided checkpoint is exactly fold-envelope-partial.png.
            if (pose == FoldPose.EnvelopeLeft)
            {
                BuildPose(FoldPose.Round, opacity, warp, incoming, direction, new Rect(.5f, 0, .5f, 1));
                BuildPose(FoldPose.EnvelopeSides, opacity, warp, incoming, direction, new Rect(0, 0, .5f, 1)); return;
            }
            if (pose == FoldPose.EnvelopeTop)
            {
                BuildPose(FoldPose.EnvelopeSides, opacity, warp, incoming, direction, new Rect(0, 0, 1, .5f));
                BuildPose(FoldPose.Envelope, opacity, warp, incoming, direction); return;
            }
            int artwork = CookingFoldArt.ArtIndex(pose);
            float aspect = artwork < 0 ? 1 : atlas.PoseAspects[artwork];
            var placement = CookingFoldArt.Placement(pose, aspect, rectTransform.rect.size);
            Rect clip = region ?? new Rect(0, 0, 1, 1);
            const int grid = 96;
            for (int y = 0; y < grid; y++) for (int x = 0; x < grid; x++)
            {
                var center = new Vector2((x + .5f) / grid, (y + .5f) / grid);
                if (!clip.Contains(center)) continue;
                // Only skip fully transparent tiles; silhouette alpha still comes from the PNG.
                if (artwork >= 0 && atlas.PoseAlpha(artwork, center) == 0
                    && atlas.PoseAlpha(artwork, new Vector2(x / (float)grid, y / (float)grid)) == 0
                    && atlas.PoseAlpha(artwork, new Vector2((x + 1f) / grid, (y + 1f) / grid)) == 0) continue;
                FoldSample sample = default; int layer;
                for (layer = 0; layer < CookingFoldArt.LayerCount(pose); layer++)
                {
                    sample = CookingFoldArt.Sample(pose, center, layer);
                    if (Covered(sample.Source)) break;
                }
                if (layer == CookingFoldArt.LayerCount(pose)) continue;
                var uv = new Rect(x / (float)grid, y / (float)grid, 1f / grid, 1f / grid);
                float heat = sample.Inside ? motion.HeatB : motion.HeatA;
                float golden = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(rules.readyTicks * .12f, rules.readyTicks, heat));
                float burnt = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Mathf.Lerp(rules.readyTicks, rules.burnTicks, .6f), rules.burnTicks, heat));
                var quad = new Quad { Authored = true, Uv = uv, Source = new Rect(0, 0, 1, 1), Placement = placement,
                    Pose = pose, Warp = warp, Incoming = incoming, Direction = direction, Layer = layer, Tint = new Color(1, 1, 1, opacity) };
                if (golden < .999f) AddPoseQuad(quad, artwork < 0 ? 0 : atlas.PoseTile(artwork, 0), opacity);
                if (golden > .001f && burnt < .999f) AddPoseQuad(quad, artwork < 0 ? 1 : atlas.PoseTile(artwork, 1), opacity * golden);
                if (burnt > .001f) AddPoseQuad(quad, artwork < 0 ? 2 : atlas.PoseTile(artwork, 2), opacity * burnt);
                if (!sample.Inside) continue;
                // The authored skin covers enclosed fillings. Only the original exposed surface,
                // or a lower layer visible through a real hole, can display them.
                quad.MappedFilling = true;
                int mx = Mathf.Clamp((int)(sample.Source.x * 128), 0, 127), my = Mathf.Clamp((int)(sample.Source.y * 128), 0, 127);
                for (int i = 0; i < 3; i++) if (motion.State.Fillings[i] != null && motion.State.Fillings[i].Mask[mx, my]) AddPoseQuad(quad, 3 + i, opacity);
                for (int i = 3; i < 5; i++) if (motion.State.Fillings[i] != null)
                    foreach (var portion in motion.State.Fillings[i].Portions)
                    {
                        var source = new Rect(portion.X / 1000f - .08f, portion.Y / 1000f - .08f, .16f, .16f);
                        if (!source.Contains(sample.Source)) continue;
                        quad.Source = source; AddPoseQuad(quad, 3 + i, opacity); quad.Source = new Rect(0, 0, 1, 1);
                    }
            }
        }
        private bool Covered(Vector2 uv) => motion.State.Batter[Mathf.Clamp((int)(uv.x * 128), 0, 127), Mathf.Clamp((int)(uv.y * 128), 0, 127)];
        private void AddPoseQuad(Quad q, int tile, float opacity)
        { q.Tile = atlas.Tiles[tile]; q.Tint.a = opacity; quads.Add(q); }
        private void AddAuthoredVertex(VertexHelper vh, Quad q, float x, float y)
        {
            Vector2 point = new Vector2(x, y);
            point = q.Placement.position + Vector2.Scale(point, q.Placement.size);
            if (q.Warp > 0 && q.Warp < 1)
            {
                var other = q.Incoming ? foldFrom : foldTo;
                int ownArt = CookingFoldArt.ArtIndex(q.Pose), otherArt = CookingFoldArt.ArtIndex(other);
                var ownSpan = atlas.PoseSpan(ownArt, x); var otherSpan = atlas.PoseSpan(otherArt, x);
                float vertical = (y - ownSpan.x) / Mathf.Max(.001f, ownSpan.y - ownSpan.x);
                var otherPlacement = CookingFoldArt.Placement(other, otherArt < 0 ? 1 : atlas.PoseAspects[otherArt], rectTransform.rect.size);
                var mapped = otherPlacement.position + Vector2.Scale(new Vector2(x, Mathf.LerpUnclamped(otherSpan.x, otherSpan.y, vertical)), otherPlacement.size);
                point = Vector2.Lerp(point, mapped, q.Incoming ? 1 - q.Warp : q.Warp);
                point.y += Mathf.Sin(q.Warp * Mathf.PI) * .07f * Mathf.Sin(x * Mathf.PI) * Mathf.Sin(Mathf.Clamp01(vertical) * Mathf.PI);
            }
            var sampleUv = q.MappedFilling ? CookingFoldArt.Sample(q.Pose, new Vector2(x, y), q.Layer).Source : new Vector2(x, y);
            var uv = new Vector2(q.Tile.x + Mathf.Clamp01((sampleUv.x - q.Source.x) / q.Source.width) * q.Tile.width,
                q.Tile.y + Mathf.Clamp01((sampleUv.y - q.Source.y) / q.Source.height) * q.Tile.height);
            var rect = rectTransform.rect;
            vh.AddVert(new Vector3(rect.xMin + point.x * rect.width, rect.yMin + point.y * rect.height), q.Tint, uv);
        }
    }
}
