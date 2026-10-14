using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Blinopek.Presentation;
using UnityEditor;
using UnityEngine;

namespace Blinopek.Editor
{
    public static class FigmaArtworkSetup
    {
        [Serializable] private sealed class Bounds { public int width = 0, height = 0; public int[] bbox = null; }
        // The artist's export is the source of alpha bounds, matching Figma node 4199:520.
        public static void Populate(GameContent content)
        {
            const string root = "Assets/Art/Blinopek/";
            string source = File.ReadAllText(root + "screens/asset-bounds.js");
            var entries = new List<Artwork>();
            foreach (Match match in Regex.Matches(source, "\"([^\"]+\\.png)\"\\s*:\\s*(\\{[^}]+\\})"))
            {
                string key = match.Groups[1].Value;
                if (key.StartsWith("screens/") || key.Contains("archive/")) continue;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(root + key);
                if (texture == null) continue;
                var bounds = JsonUtility.FromJson<Bounds>(match.Groups[2].Value);
                var b = bounds.bbox;
                entries.Add(new Artwork { key = key, texture = texture, uv = new Rect((float)b[0] / bounds.width,
                    1f - (float)b[3] / bounds.height, (float)(b[2] - b[0]) / bounds.width, (float)(b[3] - b[1]) / bounds.height) });
            }
            // Glyph and shadow exports have their own transparent margins; retain the full canvas.
            foreach (string folder in new[] { "ui/glyphs", "kitchen/shadows" })
                foreach (string file in Directory.GetFiles(root + folder, "*.png"))
                {
                    string key = file.Replace('\\', '/').Substring(root.Length);
                    if (entries.Exists(item => item.key == key)) continue;
                    entries.Add(new Artwork { key = key, texture = AssetDatabase.LoadAssetAtPath<Texture2D>(root + key) });
                }
            content.artwork = entries.ToArray();
            content.kitchenBackground = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "kitchen/kitchen-background-crepe-stove.png");
            EditorUtility.SetDirty(content);
        }
    }
}
