using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Blinopek.Domain;
using Blinopek.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Run in an isolated Unity project. The same scenes produce the before/after renders.
public static class CoverageSmoothingValidation
{
    [Serializable] private sealed class Bounds { public int width, height; public int[] bbox; }
    private static readonly List<string> checks = new List<string>();
    private static Camera camera;
    private static RenderTexture target;
    private static PancakeGraphic graphic;
    private static RawImage dish;
    private static string output;
    private static bool verify;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks.Add("PASS " + message); }

    public static void Run()
    {
        output = Environment.GetEnvironmentVariable("BLINOPEK_COVERAGE_OUTPUT");
        verify = Environment.GetEnvironmentVariable("BLINOPEK_COVERAGE_VERIFY") == "1";
        Directory.CreateDirectory(output);
        try
        {
            var art = LoadArt(Environment.GetEnvironmentVariable("BLINOPEK_ANIMATION_ART"));
            using (var atlas = new CookingAtlas(art))
            {
                camera = new GameObject("Coverage camera").AddComponent<Camera>();
                camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -10);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.26f, .23f, .20f);
                target = new RenderTexture(540, 540, 24); camera.targetTexture = target;
                var canvas = new GameObject("Coverage canvas").AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                dish = Element<RawImage>(canvas.transform, "Pan", new Vector2(520, 490));
                dish.texture = art.Find("kitchen/pan-base.png"); dish.uvRect = art.Uv(dish.texture);
                graphic = Element<PancakeGraphic>(canvas.transform, "Pancake", new Vector2(390, 350));
                graphic.Initialize(atlas, art.balance);

                var sparse = new PancakeState(1);
                Stroke(sparse.Batter, 230, 200, 720, 730);
                Stroke(sparse.Batter, 270, 710, 730, 290);
                Stroke(sparse.Batter, 220, 380, 280, 680);
                var motion = new CookingMotion(sparse);
                int count = sparse.Batter.Count, revision = sparse.Batter.Revision;
                Frame(motion, "batter");
                Check(sparse.Batter.Count == count && sparse.Batter.Revision == revision, "Rendering preserves gameplay coverage and revision");
                if (verify) Check(HasSoftVertices(), "Sparse batter has intermediate edge alpha");
                int beforeVertices = graphic.canvasRenderer.GetMesh().vertexCount;
                Stroke(sparse.Batter, 700, 320, 780, 620); Frame(motion, "batter-next-stroke");
                Check(graphic.canvasRenderer.GetMesh().vertexCount != beforeVertices, "Next stroke refreshes the visible surface on the same pancake");

                if (verify)
                {
                    var round = new PancakeState(2); round.Batter.Stamp(503, 497, 95);
                    Frame(new CookingMotion(round), "round-brush");
                    VerifyRoundContour(503, 497, 95);
                    var oldAlpha = graphic.canvasRenderer.GetMesh().colors32;
                    var shifted = new PancakeState(3); shifted.Batter.Stamp(505, 497, 95);
                    Frame(new CookingMotion(shifted), "round-brush-shifted");
                    Check(!oldAlpha.SequenceEqual(graphic.canvasRenderer.GetMesh().colors32), "Sub-cell brush movement changes the visible contour");
                    VerifyIncrementalCoverage();
                }

                for (int filling = 0; filling < 3; filling++)
                {
                    var p = new PancakeState(10 + filling); p.Batter.Stamp(500, 500, 500);
                    p.Fillings[filling] = new IngredientLayer { Dose = 1 };
                    Stroke(p.Fillings[filling].Mask, 220, 300, 720, 700);
                    Stroke(p.Fillings[filling].Mask, 270, 650, 750, 330);
                    Frame(new CookingMotion(p), "filling-" + filling);
                    if (verify)
                    {
                        // Filling vertices must feather well inside the opaque batter, too.
                        var mesh = graphic.canvasRenderer.GetMesh(); var vertices = mesh.vertices; var colors = mesh.colors32;
                        Check(Enumerable.Range(0, vertices.Length).Any(i =>
                            Mathf.Abs(vertices[i].x) < 110 && Mathf.Abs(vertices[i].y) < 90 && colors[i].a > 0 && colors[i].a < 255),
                            "Spread filling " + filling + " has its own soft edge");
                    }
                }

                var solid = new PancakeState(20); solid.Batter.Stamp(500, 500, 500);
                Frame(new CookingMotion(solid), "solid");
                var solidMesh = graphic.canvasRenderer.GetMesh();
                Check(solidMesh.colors32.Any(c => c.a == 255), "Filled interiors remain fully opaque");
                Check(solidMesh.vertexCount < 30000, "Higher-resolution solid interior still batches into strips");
                Frame(new CookingMotion(new PancakeState(21)), "empty");
                Check(graphic.canvasRenderer.GetMesh().vertexCount == 0, "Switching pancakes clears cached coverage");

                solid.Fillings[1] = new IngredientLayer { Dose = 1 }; solid.Fillings[1].Mask.Stamp(500, 500, 220);
                solid.Folds.Add(Direction.Up); Frame(new CookingMotion(solid), "fold");
                Check(graphic.UsesAuthoredFolds, "Authored folds still render");

                var fragmented = new PancakeState(30);
                for (int i = 0; i < 3; i++) fragmented.Fillings[i] = new IngredientLayer { Dose = 1 };
                for (int y = 0; y < 128; y++) for (int x = y % 2; x < 128; x += 2)
                {
                    int u = (x * 1000 + 500) / 128, v = (y * 1000 + 500) / 128;
                    fragmented.Batter.Stamp(u, v, 1);
                    for (int i = 0; i < 3; i++) fragmented.Fillings[i].Mask.Stamp(u, v, 1);
                }
                Frame(new CookingMotion(fragmented), "fragmented");
                int total = 0;
                foreach (var g in graphic.GetComponentsInChildren<MaskableGraphic>())
                {
                    int vertices = g.canvasRenderer.GetMesh().vertexCount; total += vertices;
                    Check(vertices < 65000, "Mesh page stays below the UI vertex limit");
                }
                Check(total > 65000, "Fragmented coverage is not truncated");
            }
            File.WriteAllLines(Path.Combine(output, "PASSED.txt"), checks);
            Debug.Log("COVERAGE_SMOOTHING_PASSED " + checks.Count);
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(output, "FAILED.txt"), e.ToString());
            Debug.LogException(e); EditorApplication.Exit(2);
        }
    }

    private static bool HasSoftVertices() => graphic.canvasRenderer.GetMesh().colors32.Any(c => c.a > 0 && c.a < 255);
    private static void VerifyRoundContour(int x, int y, int radius)
    {
        var mesh = graphic.canvasRenderer.GetMesh(); var positions = mesh.vertices; var colors = mesh.colors32; var indices = mesh.triangles;
        var rect = graphic.rectTransform.rect; var centre = new Vector2(x / 1000f, y / 1000f);
        float error = 0; int crossings = 0;
        for (int t = 0; t < indices.Length; t += 3) for (int edge = 0; edge < 3; edge++)
        {
            int a = indices[t + edge], b = indices[t + (edge + 1) % 3];
            float aa = colors[a].a / 255f, ab = colors[b].a / 255f;
            if ((aa < .5f) == (ab < .5f)) continue;
            var p = Vector3.Lerp(positions[a], positions[b], (.5f - aa) / (ab - aa));
            var uv = new Vector2((p.x - rect.xMin) / rect.width, (p.y - rect.yMin) / rect.height);
            error = Mathf.Max(error, Mathf.Abs(Vector2.Distance(uv, centre) - radius / 1000f)); crossings++;
        }
        Check(crossings > 50 && error < .002f, "Circular contour stays within 0.8 screen pixels of the original brush (max UV error " + error + ")");
    }
    private static void VerifyIncrementalCoverage()
    {
        var first = new PancakeState(4); first.Batter.Stamp(330, 430, 95);
        var motion = new CookingMotion(first); graphic.Show(motion); Canvas.ForceUpdateCanvases();
        first.Batter.Stamp(470, 500, 95); graphic.Show(motion); Canvas.ForceUpdateCanvases();
        var incremental = graphic.canvasRenderer.GetMesh().colors32;
        var replay = new PancakeState(5); replay.Batter.Stamp(330, 430, 95); replay.Batter.Stamp(470, 500, 95);
        graphic.Show(new CookingMotion(replay)); Canvas.ForceUpdateCanvases();
        Check(incremental.SequenceEqual(graphic.canvasRenderer.GetMesh().colors32), "Incremental strokes match a fresh reconstruction");
        replay.Batter.Stamp(470, 500, 95); graphic.Show(graphic.Motion); Canvas.ForceUpdateCanvases();
        Check(incremental.SequenceEqual(graphic.canvasRenderer.GetMesh().colors32), "Repeated stamps do not thicken the visual edge");
    }
    private static void Stroke(CoverageMask mask, int x0, int y0, int x1, int y1)
    { var sampler = new StrokeSampler(x0, y0, 20, (x, y) => mask.Stamp(x, y, 95)); sampler.Move(x1, y1); }
    private static T Element<T>(Transform parent, string name, Vector2 size) where T : Component
    {
        var go = new GameObject(name, typeof(RectTransform)); var r = (RectTransform)go.transform;
        r.SetParent(parent, false); r.sizeDelta = size; return go.AddComponent<T>();
    }
    private static void Frame(CookingMotion motion, string name)
    {
        graphic.Show(motion); Canvas.ForceUpdateCanvases(); camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = previous;
    }
    private static GameContent LoadArt(string directory)
    {
        var art = ScriptableObject.CreateInstance<GameContent>(); var entries = new List<Artwork>();
        string bounds = File.ReadAllText(Path.Combine(directory, "screens/asset-bounds.js"));
        foreach (string name in new[] { "batter-raw", "pancake-ready", "pancake-burnt", "filling-butter", "filling-jam", "filling-honey", "filling-curd", "filling-mushrooms", "pan-base", "fold-half", "fold-triangle", "fold-envelope-partial", "fold-envelope", "fold-roll-partial", "fold-roll", "fold-incorrect" })
        {
            string key = "kitchen/" + name + ".png";
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false); texture.LoadImage(File.ReadAllBytes(Path.Combine(directory, key)));
            var match = Regex.Match(bounds, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(\\{[^}]+\\})");
            var b = JsonUtility.FromJson<Bounds>(match.Groups[1].Value);
            entries.Add(new Artwork { key = key, texture = texture, uv = new Rect((float)b.bbox[0] / b.width, 1f - (float)b.bbox[3] / b.height, (float)(b.bbox[2] - b.bbox[0]) / b.width, (float)(b.bbox[3] - b.bbox[1]) / b.height) });
        }
        art.artwork = entries.ToArray(); art.raw = art.Find("kitchen/batter-raw.png"); art.ready = art.Find("kitchen/pancake-ready.png"); art.burnt = art.Find("kitchen/pancake-burnt.png");
        string[] fillings = { "butter", "jam", "honey", "curd", "mushrooms" };
        for (int i = 0; i < fillings.Length; i++) art.fillings[i] = art.Find("kitchen/filling-" + fillings[i] + ".png");
        return art;
    }
}
