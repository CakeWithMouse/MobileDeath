using System;
using UnityEngine;

namespace Blinopek.Presentation
{
    // One shared atlas lets the standard UI shader draw each folded patch with its own
    // cooking state and filling, in depth order. No custom overlay shader or readable imports.
    public sealed class CookingAtlas : IDisposable
    {
        public readonly Texture2D Texture;
        public readonly Rect[] Tiles;
        public CookingAtlas(GameContent art)
        {
            var sources = new Texture[8] { art.raw, art.ready, art.burnt,
                art.fillings[0], art.fillings[1], art.fillings[2], art.fillings[3], art.fillings[4] };
            var copies = new Texture2D[sources.Length];
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
                        copies[i] = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                        copies[i].ReadPixels(new Rect(0, 0, 512, 512), 0, 0); copies[i].Apply();
                    }
                    finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
                }
                Texture = new Texture2D(2048, 2048, TextureFormat.RGBA32, false) { name = "Cooking surfaces", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                Tiles = Texture.PackTextures(copies, 4, 2048, true);
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (var copy in copies) Release(copy);
            }
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
