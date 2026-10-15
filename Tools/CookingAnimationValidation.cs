using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Blinopek.Domain;
using Blinopek.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Run in an isolated Unity project with the real runtime scripts, uGUI and Input System.
// Environment variables supply the artwork directory and output directory.
public static class CookingAnimationValidation
{
    [Serializable] private sealed class Bounds { public int width, height; public int[] bbox; }
    private static readonly List<string> checks = new List<string>();
    private static GameContent art;
    private static Camera camera;
    private static RenderTexture target;
    private static PancakeGraphic graphic;
    private static CookingEffectsGraphic effects;
    private static Text title;
    private static RawImage dish;
    private static string output;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks.Add("PASS " + message); }
    public static void Run()
    {
        output = Environment.GetEnvironmentVariable("BLINOPEK_ANIMATION_OUTPUT"); Directory.CreateDirectory(output);
        checks.Clear(); File.Delete(Path.Combine(output, "PASSED.txt")); File.Delete(Path.Combine(output, "FAILED.txt"));
        try
        {
            art = ScriptableObject.CreateInstance<GameContent>();
            string root = Environment.GetEnvironmentVariable("BLINOPEK_ANIMATION_ART");
            string bounds = File.ReadAllText(Path.Combine(root, "screens/asset-bounds.js"));
            var entries = new List<Artwork>();
            foreach (string name in new[] { "batter-raw", "pancake-ready", "pancake-burnt", "filling-butter", "filling-jam", "filling-honey", "filling-curd", "filling-mushrooms", "plate", "pan-base", "fold-half", "fold-triangle", "fold-envelope-partial", "fold-envelope", "fold-roll-partial", "fold-roll", "fold-incorrect" })
            {
                string key = "kitchen/" + name + ".png";
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false); texture.LoadImage(File.ReadAllBytes(Path.Combine(root, key))); texture.name = name;
                var match = Regex.Match(bounds, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(\\{[^}]+\\})");
                var b = JsonUtility.FromJson<Bounds>(match.Groups[1].Value);
                entries.Add(new Artwork { key = key, texture = texture, uv = new Rect((float)b.bbox[0] / b.width, 1f - (float)b.bbox[3] / b.height, (float)(b.bbox[2] - b.bbox[0]) / b.width, (float)(b.bbox[3] - b.bbox[1]) / b.height) });
            }
            art.artwork = entries.ToArray(); art.raw = art.Find("kitchen/batter-raw.png"); art.ready = art.Find("kitchen/pancake-ready.png"); art.burnt = art.Find("kitchen/pancake-burnt.png");
            string[] ingredients = { "butter", "jam", "honey", "curd", "mushrooms" };
            for (int i = 0; i < 5; i++) art.fillings[i] = art.Find("kitchen/filling-" + ingredients[i] + ".png");
            art.bodyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); art.titleFont = art.bodyFont;
            using (var atlas = new CookingAtlas(art))
            {
                Check(atlas.HasFoldArt, "All Figma fold poses are present in the runtime atlas");
                SetupCanvas(atlas);
                RenderCooking();
                RenderFold(Fold.Triangle, false); RenderFold(Fold.Envelope, false); RenderFold(Fold.Roll, false);
                RenderFold(Fold.Triangle, true);
                VerifyReferencePoses();
                VerifyGeometry();
                VerifyFragmentedSurface();
                VerifyViewLifecycle();
            }
            File.WriteAllLines(Path.Combine(output, "PASSED.txt"), checks); Debug.Log("COOKING ANIMATIONS PASS " + checks.Count);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "FAILED.txt"), ex.ToString()); Debug.LogException(ex); EditorApplication.Exit(2); }
    }
    private static T Element<T>(Transform parent, string name, Vector2 size) where T : Component
    {
        var go = new GameObject(name, typeof(RectTransform)); var r = (RectTransform)go.transform; r.SetParent(parent, false); r.sizeDelta = size;
        return go.AddComponent<T>();
    }
    private static void SetupCanvas(CookingAtlas atlas)
    {
        camera = new GameObject("QA camera").AddComponent<Camera>(); camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -10); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.88f, .86f, .79f);
        target = new RenderTexture(960, 640, 24); camera.targetTexture = target;
        var canvas = new GameObject("QA canvas").AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        dish = Element<RawImage>(canvas.transform, "Dish", new Vector2(850, 510)); dish.raycastTarget = false;
        graphic = Element<PancakeGraphic>(canvas.transform, "Pancake", new Vector2(650, 390)); graphic.Initialize(atlas, art.balance);
        effects = Element<CookingEffectsGraphic>(canvas.transform, "Effects", new Vector2(650, 390));
        title = Element<Text>(canvas.transform, "Title", new Vector2(920, 60)); title.font = art.bodyFont; title.fontSize = 28; title.alignment = TextAnchor.MiddleCenter; title.color = new Color(.22f, .28f, .23f); title.rectTransform.anchoredPosition = new Vector2(0, 275);
    }
    private static DaySession Session()
    {
        var s = new DaySession(new Profile { day = 1, tutorialComplete = true, demonstrationSeen = true, coins = 100000, ownedIngredients = 31 }, art.balance, 77);
        s.Execute(new GameCommand(ActionKind.Accept)); s.Execute(new GameCommand(ActionKind.BeginStroke, -1, 500, 500)); s.Execute(new GameCommand(ActionKind.EndStroke));
        s.Pancake.Batter.Stamp(500, 500, 500); return s;
    }
    private static void Dish(string key) { dish.texture = art.Find("kitchen/" + key + ".png"); dish.uvRect = art.Uv(dish.texture); }
    private static void Advance(CookingMotion motion, float seconds)
    { while (seconds > .00001f) { float step = Mathf.Min(seconds, .025f); motion.Advance(step); seconds -= step; } }
    private static void Frame(CookingMotion motion, string folder, int index)
    {
        graphic.Show(motion); effects.Show(motion, art.balance); Canvas.ForceUpdateCanvases(); camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
        string dir = Path.Combine(output, folder); Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir, index.ToString("000") + ".png"), image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = previous;
        Check(graphic.canvasRenderer.GetMaterial() != null, folder + "/" + index + " UI material exists");
    }
    private static void RenderCooking()
    {
        var s = Session(); var p = s.Pancake; var motion = new CookingMotion(p); Dish("pan-base"); title.text = "Жарка и переворот";
        s.Execute(new GameCommand(ActionKind.FinishBatter));
        for (int i = 0; i <= 100; i++)
        {
            if (i == 42) { s.Execute(new GameCommand(ActionKind.Flip)); motion.Observe(); Check(motion.Turning, "Flip begins before the visible side changes"); }
            if (i < 42 || i > 55) s.Execute(new GameCommand(ActionKind.Tick, 3));
            Advance(motion, .05f); Frame(motion, "frying", i);
        }
        Check(!motion.Turning && p.HeatA != p.HeatB, "Two sides retain independent cooking states after the flip");
    }
    private static void RenderFold(Fold fold, bool burnt)
    {
        var s = Session(); var p = s.Pancake;
        if (burnt)
        {
            // A deliberately sparse pancake exercises holes through all folds.
            s = new DaySession(new Profile { tutorialComplete = true, demonstrationSeen = true, ownedIngredients = 31 }, art.balance, 77);
            s.Execute(new GameCommand(ActionKind.Accept)); s.Execute(new GameCommand(ActionKind.BeginStroke, -1, 300, 500)); s.Execute(new GameCommand(ActionKind.EndStroke));
            p = s.Pancake; p.Batter.Stamp(400, 600, 200); p.Batter.Stamp(650, 350, 240);
        }
        s.Execute(new GameCommand(ActionKind.FinishBatter)); s.Execute(new GameCommand(ActionKind.Tick, burnt ? 190 : 108)); s.Execute(new GameCommand(ActionKind.Flip)); s.Execute(new GameCommand(ActionKind.Tick, 110)); s.Execute(new GameCommand(ActionKind.MoveToPlate));
        s.Execute(new GameCommand(ActionKind.BeginStroke, 1, 500, 500)); s.Execute(new GameCommand(ActionKind.EndStroke)); p.Fillings[1].Mask.Stamp(500, 500, 210);
        s.Execute(new GameCommand(ActionKind.BeginFold, (int)fold));
        var motion = new CookingMotion(p); Dish("plate"); title.text = burnt ? "Прожарка и дырки сохраняются" : Recipes.FoldName(fold);
        string folder = burnt ? "burnt-holes" : fold.ToString().ToLowerInvariant(); int frame = 0; int originalCoverage = p.Batter.Count;
        for (int hold = 0; hold < 8; hold++) Frame(motion, folder, frame++);
        foreach (var direction in Recipes.Gestures(fold))
        {
            int dx = direction == Direction.Right ? 600 : direction == Direction.Left ? -600 : 0;
            int dy = direction == Direction.Up ? 600 : direction == Direction.Down ? -600 : 0;
            Check(s.Execute(new GameCommand(ActionKind.FoldGesture, dx, dy)), folder + " gesture accepted");
            Check(motion.Folding, folder + " transition starts without snapping");
            for (int i = 0; i < 24; i++) { Advance(motion, .05f); Frame(motion, folder, frame++); }
            Check(graphic.UsesAuthoredFolds, folder + " checkpoint uses the authored Figma pose");
        }
        Check(!motion.Folding && p.Batter.Count == originalCoverage, folder + " animation finishes and preserves coverage");
        Check(p.HeatA == (burnt ? 190 : 108) && p.HeatB == 110 && p.Fillings[1] != null, folder + " heat and filling survive folding");
        for (int hold = 0; hold < 12; hold++) Frame(motion, folder, frame++);
    }
    private static void VerifyGeometry()
    {
        var s = Session(); var p = s.Pancake; p.Folds.Add(Direction.Up);
        var before = CookingDeformation.FoldPoint(new Vector2(.25f, .2f), p, 0);
        var middle = CookingDeformation.FoldPoint(new Vector2(.25f, .2f), p, .5f);
        var after = CookingDeformation.FoldPoint(new Vector2(.25f, .2f), p, 1);
        Check(Mathf.Abs(before.Position.y - .2f) < .001f && middle.Position.z > .25f && Mathf.Abs(after.Position.y - .57f) < .001f, "Hinge raises and reflects the moving half, then centres it on the plate");
        Check(after.Facing < 0, "Fold exposes the reverse cooking side and covers filling");
        var motion = new CookingMotion(p); p.Folds.Add(Direction.Right); Advance(motion, .2f); float partial = motion.FoldPosition;
        motion.Advance(0); Check(motion.FoldPosition == partial, "Paused animation holds its exact pose");
        Advance(motion, 1); Check(!motion.Folding, "Animation resumes and reaches the next fold");
    }
    private static void VerifyReferencePoses()
    {
        dish.gameObject.SetActive(false); effects.gameObject.SetActive(false);
        var size = graphic.rectTransform.sizeDelta;
        graphic.rectTransform.sizeDelta = new Vector2(380, 300); graphic.rectTransform.anchoredPosition = new Vector2(-240, 0);
        var reference = Element<RawImage>(graphic.transform.parent, "Figma source", Vector2.one);
        reference.raycastTarget = false; reference.rectTransform.anchoredPosition = new Vector2(240, 0);
        var folds = new[] { Fold.Triangle, Fold.Envelope, Fold.Roll, Fold.Triangle, Fold.Envelope, Fold.Roll };
        var counts = new[] { 1, 2, 1, 2, 4, 3 };
        var poses = new[] { FoldPose.Half, FoldPose.EnvelopeSides, FoldPose.RollStart, FoldPose.Triangle, FoldPose.Envelope, FoldPose.Roll };
        for (int test = 0; test < folds.Length; test++)
        {
            var s = Session(); var p = s.Pancake;
            s.Execute(new GameCommand(ActionKind.FinishBatter)); s.Execute(new GameCommand(ActionKind.Tick, art.balance.readyTicks));
            s.Execute(new GameCommand(ActionKind.Flip)); s.Execute(new GameCommand(ActionKind.Tick, art.balance.readyTicks)); s.Execute(new GameCommand(ActionKind.MoveToPlate));
            s.Execute(new GameCommand(ActionKind.BeginFold, (int)folds[test]));
            var directions = Recipes.Gestures(folds[test]);
            for (int i = 0; i < counts[test]; i++)
            {
                int dx = directions[i] == Direction.Right ? 600 : directions[i] == Direction.Left ? -600 : 0;
                int dy = directions[i] == Direction.Up ? 600 : directions[i] == Direction.Down ? -600 : 0;
                Check(s.Execute(new GameCommand(ActionKind.FoldGesture, dx, dy)), "Reference pose gesture accepted");
            }
            Check(CookingFoldArt.Pose(p, counts[test]) == poses[test], "Correct Figma pose selected: " + poses[test]);
            string key = CookingFoldArt.Keys[CookingFoldArt.ArtIndex(poses[test])];
            reference.texture = art.Find("kitchen/" + key + ".png"); reference.uvRect = art.Uv(reference.texture);
            float aspect = reference.texture.width * reference.uvRect.width / (reference.texture.height * reference.uvRect.height);
            reference.uvRect = CookingFoldArt.ArtworkUv(poses[test], reference.uvRect);
            var placement = CookingFoldArt.Placement(poses[test], aspect, graphic.rectTransform.sizeDelta);
            reference.rectTransform.sizeDelta = Vector2.Scale(placement.size, graphic.rectTransform.sizeDelta);
            title.text = "Слева — Unity, справа — PNG из Figma";
            Frame(new CookingMotion(p), "reference-" + key, 0);
        }
        UnityEngine.Object.DestroyImmediate(reference.gameObject);
        graphic.rectTransform.sizeDelta = size; graphic.rectTransform.anchoredPosition = Vector2.zero;
        dish.gameObject.SetActive(true); effects.gameObject.SetActive(true);
    }
    private sealed class MemoryStore : Blinopek.Application.IGameStore
    { public Blinopek.Application.SavedGame Data; public Blinopek.Application.SavedGame Load() => Data; public void Save(Blinopek.Application.SavedGame value) { Data = value; } }
    private static void VerifyFragmentedSurface()
    {
        var s = Session(); s.Execute(new GameCommand(ActionKind.Discard));
        s.Execute(new GameCommand(ActionKind.BeginStroke, -1, 500, 500)); s.Execute(new GameCommand(ActionKind.EndStroke)); var p = s.Pancake;
        for (int i = 0; i < 3; i++) p.Fillings[i] = new IngredientLayer { Dose = 1 };
        for (int y = 0; y < 128; y++) for (int x = y % 2; x < 128; x += 2)
        {
            int u = (x * 1000 + 500) / 128, v = (y * 1000 + 500) / 128;
            p.Batter.Stamp(u, v, 1); for (int i = 0; i < 3; i++) p.Fillings[i].Mask.Stamp(u, v, 1);
        }
        s.Execute(new GameCommand(ActionKind.FinishBatter)); s.Execute(new GameCommand(ActionKind.Tick, 50));
        graphic.Show(new CookingMotion(p)); Canvas.ForceUpdateCanvases();
        int total = 0;
        foreach (var g in graphic.GetComponentsInChildren<MaskableGraphic>())
        {
            var mesh = g.canvasRenderer.GetMesh(); total += mesh.vertexCount;
            Check(mesh.vertexCount < 65000, "Fragmented surface page stays inside the uGUI vertex limit");
        }
        Check(total > 65000, "Dense holes and three fillings are rendered across multiple pages without truncation");
    }
    private static void VerifyViewLifecycle()
    {
        // View integration is checked separately from the asset preview; no user save is opened.
        var store = new MemoryStore { Data = new Blinopek.Application.SavedGame { profile = new Profile { day = 1, tutorialComplete = true, demonstrationSeen = true, ownedIngredients = 31 }, rules = art.balance } };
        var controller = new Blinopek.Application.GameController(store, new Blinopek.Infrastructure.LocalDevelopmentGateway(), art.balance);
        var go = new GameObject("View lifecycle validation"); var view = go.AddComponent<GameView>(); view.Initialize(controller, art);
        Check(view.Session != null, "GameView initializes with animated surfaces");
        typeof(GameView).GetMethod("PlayFromMainMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(view, null);
        Check(view.Command(ActionKind.Accept), "View accepts an order after entering from the main menu");
        typeof(GameView).GetMethod("SwitchKitchen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(view, new object[] { true });
        Check(view.Command(ActionKind.BeginStroke, -1, 500, 500), "View starts batter after entering the kitchen"); view.Command(ActionKind.EndStroke);
        view.Session.Pancake.Batter.Stamp(500, 500, 500);
        view.Command(ActionKind.FinishBatter); view.Command(ActionKind.Tick, 100); view.Command(ActionKind.Flip); view.Command(ActionKind.Tick, 100); view.Command(ActionKind.MoveToPlate);
        view.Command(ActionKind.BeginFold, 0); view.Refresh();
        PancakeGraphic plate = null;
        foreach (var g in view.GetComponentsInChildren<PancakeGraphic>(true)) if (g.transform.parent.name == "PancakeOnPlate") plate = g;
        Check(plate != null, "Plate contains the actual animated pancake");
        view.Command(ActionKind.FoldGesture, 0, 600); view.Command(ActionKind.FoldGesture, 600, 0); view.Refresh();
        Check(view.Session.Pancake == null && plate.gameObject.activeInHierarchy && plate.Motion.Folding, "Final fold remains on the plate after simulation serves the order");
        Check(!view.Capture(42), "Finished animation cannot accidentally start another pancake");
        controller.SetPause(Blinopek.Application.PauseReason.Menu, true); float pose = plate.Motion.FoldPosition;
        typeof(GameView).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(view, null);
        Check(plate.Motion.FoldPosition == pose, "Game pause freezes the final fold before delivery");
        controller.SetPause(Blinopek.Application.PauseReason.Menu, false); Advance(plate.Motion, 2); view.Refresh();
        Check(!plate.gameObject.activeInHierarchy, "Delivery switches screens only after the final fold completes");
        UnityEngine.Object.DestroyImmediate(go);
    }
}
