using Blinopek.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    public sealed class CookingEffectsGraphic : MaskableGraphic
    {
        private CookingMotion motion;
        private BalanceRules rules;
        private float lastTime = -1;
        public void Show(CookingMotion value, BalanceRules balance)
        {
            raycastTarget = false; motion = value; rules = balance;
            bool visible = value != null && !value.Turning && (value.State.Stage == PancakeStage.SideA || value.State.Stage == PancakeStage.SideB);
            if (enabled != visible) enabled = visible;
            if (visible && lastTime != value.Time) { lastTime = value.Time; SetVerticesDirty(); }
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (motion == null || rules == null) return;
            var rect = rectTransform.rect; float time = motion.Time;
            float heat = motion.State.Stage == PancakeStage.SideA ? motion.HeatA : motion.HeatB;
            float warmth = Mathf.Clamp01(heat / rules.readyTicks);
            for (int i = 0; i < 19; i++)
            {
                float x = .5f + Mathf.Cos(i * 2.39996f) * (.12f + i % 5 * .065f);
                float y = .5f + Mathf.Sin(i * 2.39996f) * (.12f + i % 5 * .065f);
                if (!motion.State.Batter[Mathf.Clamp((int)(x * 128), 0, 127), Mathf.Clamp((int)(y * 128), 0, 127)]) continue;
                float age = Mathf.Repeat(time * (.5f + i % 3 * .07f) + i * .173f, 1);
                if (age > .8f) continue;
                float alpha = Mathf.Sin(age / .8f * Mathf.PI) * .28f * (1 - warmth * .82f);
                float radius = (1.3f + i % 4 * .5f) * Mathf.Sin(age / .8f * Mathf.PI * .75f);
                Ring(vh, new Vector2(rect.xMin + x * rect.width, rect.yMin + y * rect.height), radius, new Color(.98f, .91f, .72f, alpha));
            }
            for (int i = 0; i < 5; i++)
            {
                float u = .22f + i * .14f, v = .55f + i % 2 * .15f;
                if (!motion.State.Batter[(int)(u * 128), (int)(v * 128)]) continue;
                float age = Mathf.Repeat(time * .3f + i * .217f, 1);
                float opacity = Mathf.Sin(age * Mathf.PI) * warmth * (heat >= rules.burnTicks ? .15f : .075f);
                var tint = heat >= rules.burnTicks ? new Color(.44f, .43f, .40f, opacity) : new Color(.92f, .94f, .88f, opacity);
                Vector2 origin = new Vector2(rect.xMin + u * rect.width, rect.yMin + v * rect.height + age * 35);
                for (int j = 0; j < 15; j++)
                {
                    float t = j / 15f, next = (j + 1f) / 15;
                    Vector2 a = origin + new Vector2(Mathf.Sin(t * 7 + time + i) * (3 + t * 10), t * (45 + age * 30));
                    Vector2 b = origin + new Vector2(Mathf.Sin(next * 7 + time + i) * (3 + next * 10), next * (45 + age * 30));
                    Color ca = tint, cb = tint; ca.a *= Mathf.Sin(t * Mathf.PI); cb.a *= Mathf.Sin(next * Mathf.PI);
                    Ribbon(vh, a, b, (2 + t * 5) * rect.width / 363, ca, cb);
                }
            }
        }
        private static void Ring(VertexHelper vh, Vector2 center, float radius, Color tint)
        {
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2 / 10, b = (i + 1) * Mathf.PI * 2 / 10;
                var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * .65f); var q = new Vector2(Mathf.Cos(b), Mathf.Sin(b) * .65f);
                int start = vh.currentVertCount;
                vh.AddVert(center + p * radius, tint, Vector2.zero); vh.AddVert(center + q * radius, tint, Vector2.zero);
                var clear = tint; clear.a = 0;
                vh.AddVert(center + q * (radius + 1.5f), clear, Vector2.zero); vh.AddVert(center + p * (radius + 1.5f), clear, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start + 2, start + 3, start);
            }
        }
        private static void Ribbon(VertexHelper vh, Vector2 a, Vector2 b, float width, Color ca, Color cb)
        {
            int first = vh.currentVertCount; var clear = new Color(ca.r, ca.g, ca.b, 0);
            vh.AddVert(a - Vector2.right * width, clear, Vector2.zero); vh.AddVert(b - Vector2.right * width, clear, Vector2.zero);
            vh.AddVert(b, cb, Vector2.zero); vh.AddVert(a, ca, Vector2.zero);
            vh.AddVert(b + Vector2.right * width, clear, Vector2.zero); vh.AddVert(a + Vector2.right * width, clear, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2); vh.AddTriangle(first + 2, first + 3, first);
            vh.AddTriangle(first + 3, first + 2, first + 4); vh.AddTriangle(first + 4, first + 5, first + 3);
        }
    }
}
