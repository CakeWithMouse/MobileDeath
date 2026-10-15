using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blinopek.Presentation
{
    // One shared atlas lets the standard UI shader draw each folded patch with its own
    // cooking state and filling, in depth order. No custom overlay shader or readable imports.
    public sealed class CookingAtlas : IDisposable
    {
        public readonly Texture2D Texture;
        public readonly Rect[] Tiles;
        public readonly float[] PoseAspects = new float[CookingFoldArt.Keys.Length];
        private readonly Color32[][] posePixels = new Color32[CookingFoldArt.Keys.Length][];
        private readonly Vector2Int[] poseSizes = new Vector2Int[CookingFoldArt.Keys.Length];
        private readonly Vector2[][] silhouettes = new Vector2[CookingFoldArt.Keys.Length + 1][];
        public bool HasFoldArt { get; private set; } = true;
        public int PoseTile(int pose, int cooking) => 8 + pose * 3 + cooking;
        public Vector2 PoseSpan(int pose, float x)
        {
            var columns = silhouettes[pose + 1];
            float column = Mathf.Clamp01(x) * (columns.Length - 1);
            int left = Mathf.FloorToInt(column);
            return Vector2.Lerp(columns[left], columns[Mathf.Min(left + 1, columns.Length - 1)], column - left);
        }
        public float PoseAlpha(int pose, Vector2 uv)
        {
            var size = poseSizes[pose];
            int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * (size.x - 1)), 0, size.x - 1), y = Mathf.Clamp(Mathf.RoundToInt(uv.y * (size.y - 1)), 0, size.y - 1);
            return posePixels[pose][y * size.x + x].a / 255f;
        }
        public CookingAtlas(GameContent art)
        {
            var sources = new Texture[8] { art.raw, art.ready, art.burnt,
                art.fillings[0], art.fillings[1], art.fillings[2], art.fillings[3], art.fillings[4] };
            var copies = new List<Texture2D>();
            var previous = RenderTexture.active;
            try
            {
                for (int i = 0; i < sources.Length; i++)
                {
                    var target = RenderTexture.GetTemporary(512, 512, 0, RenderTextureFormat.ARGB32);
                    try
                    {
                        var uv = art.Uv(sources[i]);
                        Graphics.Blit(sources[i] != null ? sources[i] : Texture2D.whiteTexture, target, uv.size, uv.position);
                        RenderTexture.active = target;
                        copies.Add(new Texture2D(512, 512, TextureFormat.RGBA32, false));
                        copies[i].ReadPixels(new Rect(0, 0, 512, 512), 0, 0); copies[i].Apply();
                    }
                    finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
                }
                foreach (var key in CookingFoldArt.Keys) if (art.Find("kitchen/" + key + ".png") == null) HasFoldArt = false;
                silhouettes[0] = Silhouette(copies[1].GetPixels32(), 512, 512);
                if (HasFoldArt) for (int pose = 0; pose < CookingFoldArt.Keys.Length; pose++)
                {
                    var source = art.Find("kitchen/" + CookingFoldArt.Keys[pose] + ".png"); var uv = art.Uv(source);
                    float aspect = source.width * uv.width / (source.height * uv.height); PoseAspects[pose] = aspect;
                    // Orient the pixels before deriving alpha, silhouettes and cooking variants,
                    // so the entire transition uses the same gesture-aligned shape.
                    uv = CookingFoldArt.ArtworkUv(CookingFoldArt.CanonicalPose(pose), uv);
                    int width = Mathf.RoundToInt(aspect >= 1 ? 512 : 512 * aspect), height = Mathf.RoundToInt(aspect >= 1 ? 512 / aspect : 512);
                    var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                    Texture2D ready;
                    try
                    {
                        Graphics.Blit(source, target, uv.size, uv.position); RenderTexture.active = target;
                        ready = new Texture2D(width, height, TextureFormat.RGBA32, false);
                        ready.ReadPixels(new Rect(0, 0, width, height), 0, 0); ready.Apply();
                    }
                    finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
                    var pixels = ready.GetPixels32(); posePixels[pose] = pixels; poseSizes[pose] = new Vector2Int(width, height);
                    silhouettes[pose + 1] = Silhouette(pixels, width, height);
                    // Transfer the existing raw/burnt surface colour into the authored pose. The
                    // painted fold highlights and thickness remain; no golden icon hides bad cooking.
                    copies.Add(CookingVariant(pixels, width, height, copies[0], copies[1], pose));
                    copies.Add(ready);
                    copies.Add(CookingVariant(pixels, width, height, copies[2], copies[1], pose));
                }
                Texture = new Texture2D(2048, 2048, TextureFormat.RGBA32, false) { name = "Cooking surfaces and authored folds", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                Tiles = Texture.PackTextures(copies.ToArray(), 4, 4096, true);
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (var copy in copies) Release(copy);
            }
        }
        private static Vector2[] Silhouette(Color32[] pixels, int width, int height)
        {
            var columns = new Vector2[width];
            for (int x = 0; x < width; x++)
            {
                int low = height, high = -1;
                for (int y = 0; y < height; y++) if (pixels[y * width + x].a >= 32) { low = Mathf.Min(low, y); high = y; }
                columns[x] = high < low ? new Vector2(.49f, .51f) : new Vector2(low / (float)height, (high + 1f) / height);
            }
            return columns;
        }
        private static Texture2D CookingVariant(Color32[] painted, int width, int height, Texture2D surface, Texture2D reference, int poseIndex)
        {
            var result = new Color32[painted.Length]; var pose = CookingFoldArt.CanonicalPose(poseIndex);
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int index = y * width + x; if (painted[index].a == 0) continue;
                Vector2 uv = CookingFoldArt.Sample(pose, new Vector2((x + .5f) / width, (y + .5f) / height)).Source;
                var cook = surface.GetPixelBilinear(uv.x, uv.y); var basis = reference.GetPixelBilinear(uv.x, uv.y); Color color = painted[index];
                color.r = Mathf.Clamp01(color.r * cook.r / Mathf.Max(.12f, basis.r));
                color.g = Mathf.Clamp01(color.g * cook.g / Mathf.Max(.12f, basis.g));
                color.b = Mathf.Clamp01(color.b * cook.b / Mathf.Max(.12f, basis.b));
                result[index] = color;
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false); texture.SetPixels32(result); texture.Apply(); return texture;
        }
        private static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
        public void Dispose() { Release(Texture); }
    }
}
