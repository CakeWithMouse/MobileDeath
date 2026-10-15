using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;
using Blinopek.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Run in the isolated animation QA project; never opens the player's save.
public static class CookingScreenValidation
{
    [Serializable] private sealed class Bounds { public int width, height; public int[] bbox; }
    private sealed class MemoryStore : IGameStore
    {
        public SavedGame Data;
        public SavedGame Load() => Data;
        public void Save(SavedGame data) { Data = data; }
    }
    private static readonly List<string> checks = new List<string>();
    private static string output;
    private static GameContent art;
    private static GameView view;
    private static Camera camera;
    private static Canvas canvas;
    private static RectTransform root;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks.Add("PASS " + message); }
    private static void Invoke(string method, params object[] args)
        => typeof(GameView).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(view, args);
    private static RectTransform Rect(string name)
        => view.GetComponentsInChildren<RectTransform>(true).Single(x => x.name == name);
    private static Texture2D Load(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Check(texture.LoadImage(File.ReadAllBytes(path)), "Loads " + Path.GetFileName(path));
        texture.name = Path.GetFileNameWithoutExtension(path); return texture;
    }
    private static GameContent Artwork(string directory)
    {
        var content = ScriptableObject.CreateInstance<GameContent>();
        var entries = new List<Artwork>();
        string source = File.ReadAllText(Path.Combine(directory, "screens/asset-bounds.js"));
        foreach (Match m in Regex.Matches(source, "\"([^\"]+\\.png)\"\\s*:\\s*(\\{[^}]+\\})"))
        {
            string key = m.Groups[1].Value;
            if (key.StartsWith("screens/") || key.Contains("archive/")) continue;
            string path = Path.Combine(directory, key); if (!File.Exists(path)) continue;
            var b = JsonUtility.FromJson<Bounds>(m.Groups[2].Value);
            entries.Add(new Artwork { key = key, texture = Load(path), uv = new Rect((float)b.bbox[0] / b.width,
                1f - (float)b.bbox[3] / b.height, (float)(b.bbox[2] - b.bbox[0]) / b.width, (float)(b.bbox[3] - b.bbox[1]) / b.height) });
        }
        foreach (string folder in new[] { "ui/glyphs", "kitchen/shadows" })
            foreach (string path in Directory.GetFiles(Path.Combine(directory, folder), "*.png"))
            {
                string key = folder + "/" + Path.GetFileName(path);
                if (!entries.Any(x => x.key == key)) entries.Add(new Artwork { key = key, texture = Load(path) });
            }
        content.artwork = entries.ToArray();
        content.kitchenBackground = Load(Path.Combine(directory, "kitchen/kitchen-background-topview-v4.png"));
        content.counterBackground = content.Find("decor/service_background.png"); content.counter = content.Find("decor/service_counter.png");
        content.plate = content.Find("kitchen/plate.png"); content.tray = content.Find("kitchen/ready-tray.png");
        content.raw = content.Find("kitchen/batter-raw.png"); content.ready = content.Find("kitchen/pancake-ready.png"); content.burnt = content.Find("kitchen/pancake-burnt.png");
        string[] names = { "butter", "jam", "honey", "curd", "mushrooms" };
        for (int i = 0; i < 5; i++) { content.fillings[i] = content.Find("kitchen/filling-" + names[i] + ".png"); content.containers[i] = content.Find("kitchen/container-" + names[i] + ".png"); }
        string[] people = { "student", "office", "craftswoman", "nurse", "elder-woman", "elder-man" };
        for (int i = 0; i < 6; i++) {
            content.visitors[i] = content.Find("visitors/" + people[i] + "-neutral.png");
            content.positiveVisitors[i] = content.Find("visitors/" + people[i] + "-positive.png");
            content.negativeVisitors[i] = content.Find("visitors/" + people[i] + "-negative.png");
        }
        content.bodyFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/PT_Sans-Web-Regular.ttf");
        content.titleFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/PT_Serif-Web-Regular.ttf");
        Check(content.bodyFont != null && content.titleFont != null, "Uses the game's actual Cyrillic fonts");
        return content;
    }
    private static void Render(string name, int width = 1600, int height = 900)
    {
        view.Refresh(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
        canvas.planeDistance = 1; canvas.GetComponent<CanvasScaler>().enabled = false; canvas.scaleFactor = 1;
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        root.anchoredPosition = Vector2.zero; root.localScale = Vector3.one * Mathf.Min(width / 1600f, height / 900f);
        Canvas.ForceUpdateCanvases(); camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
    }
    private static void Advance(float seconds)
    {
        var motions = (Dictionary<PancakeState, CookingMotion>)typeof(GameView).GetField("motions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
        while (seconds > 0) { float step = Mathf.Min(seconds, .05f); foreach (var motion in motions.Values) motion.Advance(step); seconds -= step; }
        view.Refresh();
    }
    private static void PrepareInput(float scale)
    {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; root.localScale = Vector3.one * scale;
        root.anchoredPosition = Vector2.zero; Canvas.ForceUpdateCanvases();
    }
    private static Vector2 Point(RectTransform r, Vector2 normalized)
        => r.TransformPoint(Vector2.Scale(normalized - Vector2.one * .5f, r.rect.size));
    private static void Gesture(RectTransform r, Vector2 start, Vector2 end)
    {
        var router = r.GetComponent<KitchenInputRouter>();
        var e = new PointerEventData(UnityEngine.Object.FindAnyObjectByType<EventSystem>()) { pointerId = 8, position = Point(r, start) };
        router.OnPointerDown(e); e.position = Point(r, end); router.OnDrag(e); router.OnPointerUp(e); view.Refresh();
    }
    private static void BeginPancake()
    {
        Check(view.Command(ActionKind.BeginStroke, -1, 500, 500), "Starts a separate runtime pancake");
        view.Command(ActionKind.EndStroke); view.Session.Pancake.Batter.Stamp(500, 500, 500);
        Check(view.Command(ActionKind.FinishBatter), "Starts frying"); view.Command(ActionKind.Tick, 100); Advance(2);
    }
    public static void Run()
    {
        output = Environment.GetEnvironmentVariable("BLINOPEK_SCREEN_OUTPUT"); Directory.CreateDirectory(output);
        try
        {
            art = Artwork(Environment.GetEnvironmentVariable("BLINOPEK_ANIMATION_ART"));
            var store = new MemoryStore { Data = new SavedGame { profile = new Profile { day = 6, tutorialComplete = true, demonstrationSeen = true, coins = 100000, ownedIngredients = 31 }, rules = art.balance } };
            var controller = new GameController(store, new LocalDevelopmentGateway(), art.balance);
            view = new GameObject("Cooking screen validation").AddComponent<GameView>(); view.Initialize(controller, art);
            Invoke("PlayFromMainMenu"); view.Command(ActionKind.Accept); Invoke("SwitchKitchen", true);
            canvas = view.GetComponentInChildren<Canvas>(); root = Rect("SafeArea");
            camera = new GameObject("Screenshot camera").AddComponent<Camera>(); camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .16f, .13f);
            Render("01-empty");
            Check(!view.GetComponentsInChildren<RawImage>(true).Any(x => x.name == "Plate" || x.name == "ReadyTray" || x.name == "BatterPitcher"), "Empty dishes are not duplicated over the background");
            var pan = Rect("PanSurface"); var plate = Rect("PlateSurface");
            PrepareInput(1);
            Gesture(pan, Vector2.one * .5f, Vector2.one * .5f);
            Check(view.Session.Pancake != null && view.Session.Pancake.Batter.Count > 0, "Pointer on the new griddle paints the centre of the batter");
            view.Command(ActionKind.Discard); BeginPancake(); Render("02-frying");
            PrepareInput(.65f); Gesture(pan, Vector2.one * .5f, Vector2.one * .5f);
            Check(view.Session.Pancake.Stage == PancakeStage.SideB, "Tap on the resized griddle flips the pancake");
            Advance(1); view.Command(ActionKind.Tick, 100); Advance(2);
            // Drag across real screen coordinates to the new plate centre.
            var router = pan.GetComponent<KitchenInputRouter>();
            var e = new PointerEventData(UnityEngine.Object.FindAnyObjectByType<EventSystem>()) { pointerId = 8, position = Point(pan, Vector2.one * .5f) };
            router.OnPointerDown(e); e.position = Point(plate, Vector2.one * .5f); router.OnPointerUp(e); view.Refresh();
            Check(view.Session.Pancake.Stage == PancakeStage.Plate, "Drag lands on the relocated assembly plate");
            var p = view.Session.Pancake;
            view.SelectIngredient(1); Gesture(plate, new Vector2(.45f,.5f), new Vector2(.55f,.5f));
            Check(p.Fillings[1] != null && p.Fillings[1].Mask.Count > 0, "Jam is painted on the relocated plate");
            p.Fillings[1].Mask.Stamp(500, 500, 225);
            Render("03-assembly"); Render("04-wide", 1920, 900); Render("05-tablet", 1200, 900);
            foreach (float scale in new[] { .6f, 1f, 1.2f }) {
                PrepareInput(scale); var mapped = view.PlateCoordinates(Point(plate, new Vector2(.2f,.8f)));
                Check(Vector2.Distance(mapped, new Vector2(200,800)) < 1, "Plate drop coordinates survive UI scale " + scale);
            }
            PrepareInput(1); Check(view.Command(ActionKind.BeginFold, (int)Fold.Triangle), "Fold selection remains available");
            Gesture(plate, new Vector2(.5f,.5f), new Vector2(.5f,.68f));
            Check(p.Folds.Count == 1, "Swipe on the taller plate starts the first fold"); Advance(.3f); Render("06-folding");
            Advance(1); PrepareInput(1); Gesture(plate, new Vector2(.5f,.5f), new Vector2(.68f,.5f)); Advance(1); Render("07-ready");
            Check(view.Session.TrayCount > 0, "Completed animated pancake appears on the new serving tray");
            File.WriteAllLines(Path.Combine(output, "PASSED.txt"), checks); Debug.Log("COOKING_SCREEN_PASSED " + checks.Count);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "FAILED.txt"), ex.ToString()); Debug.LogException(ex); EditorApplication.Exit(2); }
    }
}
