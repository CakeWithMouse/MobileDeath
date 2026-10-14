using System.Collections.Generic;
using Blinopek.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PancakeGraphic : MaskableGraphic
    {
        private struct Patch { public Rect Uv; public float Depth, Facing; public int Fillings; }
        private struct Quad { public Rect Uv, Tile, Source; public Color Tint; public bool Shadow; }
        private readonly List<Patch> patches = new List<Patch>(4096);
        private readonly List<Quad> quads = new List<Quad>(8192);
        private readonly List<PancakeMeshPage> pages = new List<PancakeMeshPage>();
        private CookingAtlas atlas;
        private BalanceRules rules;
        private CookingMotion motion;
        private int revision = -1;
        private float oldFold = -1, oldFlip = -1, oldHeatA = -1, oldHeatB = -1;
        internal const int QuadsPerPage = 12000;
        public override Texture mainTexture => atlas != null ? atlas.Texture : Texture2D.whiteTexture;
        public CookingMotion Motion => motion;
        public void Initialize(CookingAtlas surfaces, BalanceRules balance)
        { atlas = surfaces; rules = balance; raycastTarget = false; material = null; }

        public void Show(CookingMotion value, BalanceRules balance = null)
        {
            if (balance != null && rules != balance) { rules = balance; revision = -1; }
            if (motion != value) { motion = value; revision = -1; }
            if (motion == null) return;
            var state = motion.State;
            int nextRevision = state.Batter.Revision;
            unchecked { for (int i = 0; i < 5; i++) if (state.Fillings[i] != null) nextRevision = nextRevision * 31 + state.Fillings[i].Mask.Revision + state.Fillings[i].Dose; }
            if (revision == nextRevision && oldFold == motion.FoldPosition && oldFlip == motion.FlipPosition && oldHeatA == motion.HeatA && oldHeatB == motion.HeatB) return;
            revision = nextRevision; oldFold = motion.FoldPosition; oldFlip = motion.FlipPosition; oldHeatA = motion.HeatA; oldHeatB = motion.HeatB;
            RebuildSurface(); SetVerticesDirty(); SetMaterialDirty();
            int required = (quads.Count + QuadsPerPage - 1) / QuadsPerPage - 1;
            while (pages.Count < required)
            {
                var go = new GameObject("Surface continuation", typeof(RectTransform), typeof(CanvasRenderer), typeof(PancakeMeshPage));
                var r = (RectTransform)go.transform; r.SetParent(transform, false); r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.sizeDelta = Vector2.zero;
                var page = go.GetComponent<PancakeMeshPage>(); page.Owner = this; page.Page = pages.Count + 1; page.raycastTarget = false; pages.Add(page);
            }
            for (int i = 0; i < pages.Count; i++) { pages[i].gameObject.SetActive(i < required); if (i < required) { pages[i].SetVerticesDirty(); pages[i].SetMaterialDirty(); } }
        }
        public static Vector2 FoldPoint(Vector2 uv, PancakeState state)
        {
            if (state == null) return uv;
            var p = CookingDeformation.FoldPoint(uv, state, state.Folds.Count).Position;
            return new Vector2(p.x, p.y + p.z * .85f);
        }
        private int FillingBits(int x, int y)
        {
            int result = 0;
            for (int i = 0; i < 3; i++) if (motion.State.Fillings[i] != null && motion.State.Fillings[i].Mask[x, y]) result |= 1 << i;
            return result;
        }
        private CookingVertex Vertex(Vector2 uv) => CookingDeformation.FoldPoint(uv, motion.State, motion.FoldPosition);
        private void RebuildSurface()
        {
            patches.Clear(); quads.Clear(); var state = motion.State;
            const int size = CoverageMask.Size;
            for (int y = 0; y < size; y++)
            {
                int x = 0;
                while (x < size)
                {
                    if (!state.Batter[x, y]) { x++; continue; }
                    int start = x, bits = FillingBits(x, y); x++;
                    while (x < size && x - start < 8 && state.Batter[x, y] && FillingBits(x, y) == bits
                        && x != 36 && x != 64 && x != 92) x++;
                    var uv = Rect.MinMaxRect(start / (float)size, y / (float)size, x / (float)size, (y + 1f) / size);
                    var point = Vertex(uv.center);
                    patches.Add(new Patch { Uv = uv, Depth = point.Position.z, Facing = point.Facing, Fillings = bits });
                }
            }
            patches.Sort((a, b) => { int z = a.Depth.CompareTo(b.Depth); return z != 0 ? z : a.Uv.y.CompareTo(b.Uv.y); });
            // A narrow contact shadow reveals thickness and the landing without extra art.
            if (motion.FoldPosition > 0 || motion.Turning)
                foreach (var patch in patches)
                    quads.Add(new Quad { Uv = patch.Uv, Tile = atlas.Tiles[1], Source = new Rect(0, 0, 1, 1), Tint = new Color(.19f, .12f, .06f, .035f), Shadow = true });
            bool sideA = state.Stage == PancakeStage.Batter || state.Stage == PancakeStage.SideA || motion.FlipPosition < .5f;
            foreach (var patch in patches)
            {
                bool front = patch.Facing >= 0;
                float heat = sideA == front ? motion.HeatA : motion.HeatB;
                float shade = (.76f + .24f * Mathf.Abs(patch.Facing)) * (front ? 1 : .97f);
                if (motion.Turning) shade *= .82f + .18f * Mathf.Abs(Mathf.Cos(motion.FlipPosition * Mathf.PI));
                float golden = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(rules.readyTicks * .12f, rules.readyTicks, heat));
                float burnt = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Mathf.Lerp(rules.readyTicks, rules.burnTicks, .6f), rules.burnTicks, heat));
                if (golden < .999f) Add(patch.Uv, 0, 1, shade);
                if (golden > .001f && burnt < .999f) Add(patch.Uv, 1, golden, shade);
                if (burnt > .001f) Add(patch.Uv, 2, burnt, shade);
                if (!front) continue;
                for (int i = 0; i < 3; i++) if ((patch.Fillings & (1 << i)) != 0) Add(patch.Uv, i + 3, 1, shade);
                for (int i = 3; i < 5; i++) if (state.Fillings[i] != null)
                    foreach (var portion in state.Fillings[i].Portions)
                    {
                        var source = new Rect(portion.X / 1000f - .08f, portion.Y / 1000f - .08f, .16f, .16f);
                        var clip = Rect.MinMaxRect(Mathf.Max(patch.Uv.xMin, source.xMin), Mathf.Max(patch.Uv.yMin, source.yMin), Mathf.Min(patch.Uv.xMax, source.xMax), Mathf.Min(patch.Uv.yMax, source.yMax));
                        if (clip.width > 0 && clip.height > 0) Add(clip, i + 3, 1, shade, source);
                    }
            }
        }
        private void Add(Rect uv, int tile, float alpha, float shade, Rect? source = null)
        { quads.Add(new Quad { Uv = uv, Tile = atlas.Tiles[tile], Source = source ?? new Rect(0, 0, 1, 1), Tint = new Color(shade, shade, shade, alpha) }); }
        protected override void OnPopulateMesh(VertexHelper vh) => PopulatePage(vh, 0);
        internal void PopulatePage(VertexHelper vh, int page)
        {
            vh.Clear(); if (motion == null) return;
            int end = Mathf.Min(quads.Count, (page + 1) * QuadsPerPage);
            for (int i = page * QuadsPerPage; i < end; i++)
            {
                var q = quads[i]; int first = vh.currentVertCount;
                AddVertex(vh, q, q.Uv.xMin, q.Uv.yMin); AddVertex(vh, q, q.Uv.xMin, q.Uv.yMax);
                AddVertex(vh, q, q.Uv.xMax, q.Uv.yMax); AddVertex(vh, q, q.Uv.xMax, q.Uv.yMin);
                vh.AddTriangle(first, first + 1, first + 2); vh.AddTriangle(first + 2, first + 3, first);
            }
        }
        private void AddVertex(VertexHelper vh, Quad q, float x, float y)
        {
            var p = Vertex(new Vector2(x, y)).Position; var rect = rectTransform.rect;
            float screenY = p.y + p.z * .85f;
            if (q.Shadow)
            {
                p.x += .002f; screenY = p.y - .005f;
                if (motion.Turning) { p.x = .5f + (x - .5f) * .92f; screenY = y; }
            }
            else if (motion.Turning)
            {
                float t = CookingDeformation.Ease(motion.FlipPosition);
                screenY = .5f + (screenY - .5f) * Mathf.Abs(Mathf.Cos(t * Mathf.PI));
                screenY += Mathf.Sin(t * Mathf.PI) * .40f;
                p.x = .5f + (p.x - .5f) * (1 + Mathf.Sin(t * Mathf.PI) * .05f);
            }
            var sourceUv = new Vector2(q.Tile.x + (x - q.Source.x) / q.Source.width * q.Tile.width, q.Tile.y + (y - q.Source.y) / q.Source.height * q.Tile.height);
            vh.AddVert(new Vector3(rect.xMin + p.x * rect.width, rect.yMin + screenY * rect.height), q.Tint, sourceUv);
        }
    }

    public sealed class PancakeMeshPage : MaskableGraphic
    {
        internal PancakeGraphic Owner;
        internal int Page;
        public override Texture mainTexture => Owner != null ? Owner.mainTexture : Texture2D.whiteTexture;
        protected override void OnPopulateMesh(VertexHelper vh) { if (Owner != null) Owner.PopulatePage(vh, Page); else vh.Clear(); }
    }
}
