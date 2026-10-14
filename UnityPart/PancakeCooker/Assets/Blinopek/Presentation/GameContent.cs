using Blinopek.Domain;
using UnityEngine;

namespace Blinopek.Presentation
{
    [System.Serializable]
    public sealed class Artwork
    {
        public string key;
        public Texture2D texture;
        public Rect uv = new Rect(0, 0, 1, 1);
    }

    [CreateAssetMenu(menuName = "Blinopek/Gameplay content")]
    public sealed class GameContent : ScriptableObject
    {
        public BalanceRules balance = new BalanceRules();
        public GameAudioLibrary audio;
        public Font bodyFont, titleFont;
        public Texture2D kitchenBackground, counterBackground, counter, pan, handle, plate, trash, tray;
        public Texture2D stove, assemblyTable;
        public Texture2D raw, ready, burnt;
        public Texture2D[] fillings = new Texture2D[5];
        public Texture2D[] containers = new Texture2D[5];
        public Texture2D[] visitors = new Texture2D[6];
        public Texture2D[] positiveVisitors = new Texture2D[6];
        public Texture2D[] negativeVisitors = new Texture2D[6];
        public Shader pancakeShader;
        public Artwork[] artwork = new Artwork[0];
        public Texture2D Find(string key)
        {
            foreach (var item in artwork) if (item.key == key) return item.texture;
            return null;
        }
        public Rect Uv(Texture texture)
        {
            if (texture != null && artwork != null) foreach (var item in artwork)
            {
                if (item == null || item.texture != texture) continue;
                var uv = item.uv;
                // A malformed serialized Rect can load as a zero-area crop. Never sample a single
                // transparent texel or divide by zero while fitting artwork: show the full image.
                if (uv.x >= 0 && uv.y >= 0 && uv.width > 0 && uv.height > 0 && uv.xMax <= 1.0001f && uv.yMax <= 1.0001f)
                    return uv;
                break;
            }
            return new Rect(0, 0, 1, 1);
        }
    }
}
