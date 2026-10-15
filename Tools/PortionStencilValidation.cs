using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;
using Blinopek.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Copy alongside CookingScreenValidation.cs into an isolated QA project's Assets/Editor.
// Uses that validator's artwork loader and real game fonts, with an in-memory save only.
public static class PortionStencilValidation
{
    private sealed class MemoryStore : IGameStore
    {
        public SavedGame Data;
        public SavedGame Load() => Data;
        public void Save(SavedGame value) { Data = value; }
    }
    private static GameView view;
    private static Canvas canvas;
    private static RectTransform root;
    private static Camera camera;
    private static string output;
    private static RectTransform Find(string name) => view.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == name);
    private static void Invoke(string name, params object[] args)
        => typeof(GameView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, args);
    private static void Input()
    {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        root.localScale = Vector3.one; root.anchoredPosition = Vector2.zero;
        Canvas.ForceUpdateCanvases();
    }
    private static Vector2 Screen(RectTransform r, float x = .5f, float y = .5f)
        => RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(new Vector2((x - .5f) * r.rect.width, (y - .5f) * r.rect.height)));
    private static PointerEventData Drag(int type, Point target)
    {
        Input(); var source = Find("Ingredient" + type);
        var e = new PointerEventData(UnityEngine.Object.FindAnyObjectByType<EventSystem>()) { pointerId = 7, position = Screen(source) };
        ExecuteEvents.Execute(source.gameObject, e, ExecuteEvents.pointerDownHandler);
        e.position = Screen(Find("PlateSurface"), target.X / 1000f, target.Y / 1000f);
        ExecuteEvents.Execute(source.gameObject, e, ExecuteEvents.dragHandler); return e;
    }
    private static void Drop(int type, Point target)
    {
        var e = Drag(type, target);
        ExecuteEvents.Execute(Find("Ingredient" + type).gameObject, e, ExecuteEvents.pointerUpHandler); view.Refresh();
    }
    private static void Frame(string name, int width = 1600, int height = 900)
    {
        view.Refresh(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
        canvas.planeDistance = 1; canvas.GetComponent<CanvasScaler>().enabled = false; canvas.scaleFactor = 1;
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        root.anchoredPosition = Vector2.zero; root.localScale = Vector3.one * Mathf.Min(width / 1600f, height / 900f);
        Canvas.ForceUpdateCanvases(); camera.Render();
        foreach (var guide in view.GetComponentsInChildren<Text>().Where(t => t.name == "PortionGuide"))
            Check(guide.canvasRenderer.GetMesh().vertexCount >= 4, "Visible stencil glyph: " + name);
        if (view.Session.PlatePancake?.Stage == PancakeStage.Plate)
            foreach (string control in new[] { "Ingredient3", "Ingredient4", "PlateSurface" })
            {
                var expected = Find(control);
                var pointer = new PointerEventData(UnityEngine.Object.FindAnyObjectByType<EventSystem>()) {
                    position = RectTransformUtility.WorldToScreenPoint(camera, expected.position) };
                var hits = new List<RaycastResult>(); canvas.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
                Check(hits.Count > 0 && hits[0].gameObject == expected.gameObject, "Unobstructed input for " + control + ": " + name);
            }
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
    }
    private static void Advance()
    {
        var motions = (Dictionary<PancakeState, CookingMotion>)typeof(GameView).GetField("motions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
        for (int i = 0; i < 30; i++) foreach (var motion in motions.Values) motion.Advance(.1f);
        view.Refresh();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run()
    {
        output = Environment.GetEnvironmentVariable("BLINOPEK_PORTION_OUTPUT"); Directory.CreateDirectory(output);
        try
        {
            var art = (GameContent)typeof(CookingScreenValidation).GetMethod("Artwork", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { Environment.GetEnvironmentVariable("BLINOPEK_ANIMATION_ART") });
            art.balance.readyTicks = 2; art.balance.burnTicks = 6;
            var store = new MemoryStore { Data = new SavedGame { profile = new Profile {
                tutorialComplete = true, demonstrationSeen = true, coins = 100000, ownedIngredients = 31, scrapers = 4 } } };
            view = new GameObject("Portion stencil render").AddComponent<GameView>();
            view.Initialize(new GameController(store, new LocalDevelopmentGateway(), art.balance), art);
            Invoke("PlayFromMainMenu"); Invoke("SwitchKitchen", true);
            canvas = view.GetComponentInChildren<Canvas>(); root = Find("SafeArea");
            camera = new GameObject("Portion screenshot camera").AddComponent<Camera>(); camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10); camera.clearFlags = CameraClearFlags.SolidColor;
            Check(view.Command(ActionKind.BeginStroke, -1, 500, 500), "Start batter"); view.Command(ActionKind.EndStroke);
            view.Session.PanPancake.Batter.Stamp(500, 500, 500);
            view.Command(ActionKind.FinishBatter); view.Command(ActionKind.Tick, 2); view.Command(ActionKind.Flip);
            view.Command(ActionKind.Tick, 2); Check(view.Command(ActionKind.MoveToPlate), "Move pancake to plate"); Advance();
            for (int type = 3; type <= 4; type++)
            {
                view.SelectIngredient(type); Frame(type + "-empty-stencil");
                Drop(type, Recipes.PortionTargets[0]); Frame(type + "-one-portion");
                var e = Drag(type, Recipes.PortionTargets[1]); Frame(type + "-drag-preview");
                Input(); e.position = Screen(Find("PlateSurface"), .68f, .32f);
                ExecuteEvents.Execute(Find("Ingredient" + type).gameObject, e, ExecuteEvents.pointerUpHandler);
                for (int i = 2; i < 4; i++) Drop(type, Recipes.PortionTargets[i]);
                Check(view.Session.PlatePancake.Fillings[type].Portions.Count == 4, "Four actual UI drops for ingredient " + type);
                Frame(type + "-filled"); Frame(type + "-tablet", 1200, 900);
                if (type == 3) view.Command(ActionKind.Remove, type);
            }
            view.Command(ActionKind.BeginFold, (int)Fold.Triangle); view.Command(ActionKind.FoldGesture, 0, 400); Advance();
            Frame("5-folded-mushrooms");
            File.WriteAllText(Path.Combine(output, "PASSED.txt"), "Rendered four pointer drops for each ingredient, individual targets, drag previews and folded mushrooms. Visible stencil glyph geometry and actual GraphicRaycaster hits verified at 1600x900 and 1200x900.");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "FAILED.txt"), error.ToString()); Debug.LogException(error); EditorApplication.Exit(2);
        }
    }
}
