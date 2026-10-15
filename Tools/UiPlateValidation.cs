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
using UnityEngine.UI;

// Run from an Editor folder in an isolated project, alongside CookingScreenValidation.
public static class UiPlateValidation
{
    private sealed class MemoryStore : IResettableGameStore
    {
        public SavedGame Load() => null;
        public void Save(SavedGame value) { }
        public void Reset(SavedGame value) { }
    }
    private static GameView view;
    private static Camera camera;
    private static string output;
    private static readonly List<string> checks = new List<string>();
    private static GameObject Find(string name) => view.GetComponentsInChildren<Transform>(true).Single(t => t.name == name).gameObject;
    private static void Invoke(string name, params object[] args) => typeof(GameView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, args);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks.Add(message); }
    private static void Click(string name)
    {
        var b = Find(name).GetComponent<Button>();
        Check(b.IsInteractable() && b.gameObject.activeInHierarchy, name + " is available");
        b.onClick.Invoke(); view.Refresh();
    }
    private static void Render(string name, int width = 1600, int height = 900)
    {
        var canvas = view.GetComponentInChildren<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        canvas.GetComponent<CanvasScaler>().enabled = false; canvas.scaleFactor = 1;
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        var root = Find("SafeArea").transform; root.localPosition = Vector3.zero;
        root.localScale = Vector3.one * Mathf.Min(width / 1600f, height / 900f);
        Canvas.ForceUpdateCanvases(); camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
    }
    private static void VerifyBookLayout()
    {
        var paper = Find("OpenBook").GetComponent<RawImage>();
        Check(Mathf.Abs(paper.rectTransform.rect.width / paper.rectTransform.rect.height - paper.texture.width * paper.uvRect.width / (paper.texture.height * paper.uvRect.height)) < .001f, "Book artwork keeps its proportions");
        for (int i = 0; i < 4; i++) {
            var leaf = Find("RecipeLeaf" + i).GetComponent<RectTransform>();
            Check(leaf.IsChildOf(paper.transform), "Page content follows the fitted book");
            foreach (var label in leaf.GetComponentsInChildren<Text>(true))
                Check(label.preferredHeight <= label.rectTransform.rect.height, label.name + " text fits its page");
            if (i == 3) continue;
            float center = leaf.rect.width * .5f;
            foreach (string name in new[] { "RecipeName", "Fold", "Gestures", "Plus" })
                Check(Mathf.Abs(leaf.Find(name).GetComponent<RectTransform>().anchoredPosition.x - center) < .01f, name + " aligns with the page center");
            var first = leaf.Find("Filling1").GetComponent<RectTransform>();
            var second = leaf.Find("Filling2").GetComponent<RectTransform>();
            Check(Mathf.Abs((first.anchoredPosition.x + second.anchoredPosition.x) * .5f - center) < .01f, "Ingredient pair is centered as a group");
            Check(Mathf.Abs(first.anchoredPosition.y - second.anchoredPosition.y) < .01f, "Ingredients share the same row");
            foreach (var graphic in leaf.GetComponentsInChildren<RawImage>(true)) {
                var corners = new Vector3[4]; graphic.rectTransform.GetWorldCorners(corners);
                Check(corners.All(p => leaf.rect.Contains((Vector2)leaf.InverseTransformPoint(p))), graphic.name + " stays inside the page");
                Check(Mathf.Abs(graphic.rectTransform.rect.width / graphic.rectTransform.rect.height - graphic.texture.width * graphic.uvRect.width / (graphic.texture.height * graphic.uvRect.height)) < .001f, graphic.name + " keeps its proportions");
            }
        }
    }
    public static void Run()
    {
        output = Environment.GetEnvironmentVariable("BLINOPEK_UI_OUTPUT"); Directory.CreateDirectory(output);
        try {
            var art = (GameContent)typeof(CookingScreenValidation).GetMethod("Artwork", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { Environment.GetEnvironmentVariable("BLINOPEK_UI_ART") });
            art.signboardFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/YesevaOne-Regular.ttf");
            art.signboardShader = Shader.Find("Blinopek/Signboard Paint");
            art.balance = new BalanceRules { startingCoins = 10000, rent = 0, dayTicks = 2 };
            var game = new GameController(new MemoryStore(), new LocalDevelopmentGateway(), art.balance);
            game.Profile.tutorialComplete = true;
            view = new GameObject("UI plate validation").AddComponent<GameView>(); view.Initialize(game, art);
            camera = new GameObject("Screenshot camera").AddComponent<Camera>(); camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10); camera.clearFlags = CameraClearFlags.SolidColor;
            if (Environment.GetEnvironmentVariable("BLINOPEK_UI_BOOK_ONLY") == "1") {
                Click("ContinueShift"); Click("RecipeBook");
                Render("01-recipes"); VerifyBookLayout(); Render("02-recipes-mobile", 960, 540); Render("03-recipes-tablet", 1280, 960);
                Click("NextRecipe"); Render("04-recipes-next"); VerifyBookLayout(); Render("05-recipes-next-mobile", 960, 540);
                Click("PreviousRecipe"); Check(Find("RecipePage0").activeInHierarchy && !Find("RecipePage1").activeSelf, "Previous arrow restores the first spread");
                Click("CloseBook"); Check(!Find("RecipeBookPanel").activeSelf && !game.Paused, "Book closes and resumes the game");
                File.WriteAllLines(Path.Combine(output, "PASSED.txt"), checks);
                string bookFailure = Path.Combine(output, "FAILED.txt"); if (File.Exists(bookFailure)) File.Delete(bookFailure);
                Debug.Log("RECIPE_BOOK_VALIDATION_PASSED " + checks.Count); EditorApplication.Exit(0); return;
            }
            Render("01-main-menu"); Click("MainMenuShop"); Render("02-shop");
            Click("UpgradeTab"); Render("03-upgrades"); Click("CloseShop");
            Click("MainMenuSettings"); Render("04-settings"); Click("ResetProgress"); Render("05-reset");
            Click("CancelResetProgress"); Click("Resume"); Click("CafeName"); Render("06-name"); Click("CancelCafeName");
            Click("ContinueShift"); Render("07-counter"); Render("08-counter-mobile", 960, 540);
            Render("09-counter-wide", 1920, 1080); Render("10-counter-tablet", 1280, 960);
            var fill = typeof(GameView).GetMethod("FillBar", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (float amount in new[] { 0f, .02f, .5f, 1f }) {
                fill.Invoke(null, new object[] { Find("PatienceFill").GetComponent<Image>(), amount });
                Render("patience-" + Mathf.RoundToInt(amount * 100));
            }
            view.Refresh(); Click("KitchenTab"); Render("11-kitchen"); Click("ShowOrder"); Render("12-order"); Click("ShowOrder");
            Click("KitchenRecipeBook"); Render("13-recipes"); Click("NextRecipe"); Render("14-recipes-next"); Click("CloseBook");
            for (int i = 0; i < 8 && game.Session.Phase != DayPhase.Result; i++)
                Check(game.Dispatch(new GameCommand(ActionKind.Tick, game.Session.Order.PatienceLimit + art.balance.dayTicks + 100)), "Day advances");
            view.Refresh(); Check(game.Session.Phase == DayPhase.Result, "Results are reached"); Render("15-results");
            if (Environment.GetEnvironmentVariable("BLINOPEK_UI_BASELINE") != "1") {
                var plateKeys = new[] { "ui/button-cream.png", "ui/panel-cream.png", "ui/card-frame.png", "ui/patience-track.png" };
                var textures = plateKeys.Select(art.Find).ToArray();
                var plates = view.GetComponentsInChildren<RawImage>(true).Where(x => textures.Contains(x.texture)).ToArray();
                foreach (var plate in plates) Check(plate is ButtonPlateGraphic, plate.name + " preserves painted edges");
                foreach (var plate in plates) {
                    var vh = new VertexHelper();
                    typeof(ButtonPlateGraphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(VertexHelper) }, null).Invoke(plate, new object[] { vh });
                    var a = new UIVertex(); var b = new UIVertex(); var c = new UIVertex();
                    vh.PopulateUIVertex(ref a, 0); vh.PopulateUIVertex(ref b, 1); vh.PopulateUIVertex(ref c, 2);
                    float sx = (c.position.x - b.position.x) / ((c.uv0.x - b.uv0.x) * plate.texture.width);
                    float sy = (b.position.y - a.position.y) / ((b.uv0.y - a.uv0.y) * plate.texture.height);
                    Check(Mathf.Abs(sx - sy) < .002f, plate.name + " corner has uniform pixel scale"); vh.Dispose();
                }
                foreach (string name in new[] { "SpeechBubble", "OpenBook" }) {
                    var p = Find(name).GetComponent<RawImage>();
                    Check(Mathf.Abs(p.rectTransform.rect.width / p.rectTransform.rect.height - p.texture.width * p.uvRect.width / (p.texture.height * p.uvRect.height)) < .001f, name + " keeps its aspect ratio");
                }
                Check(plates.Length > 60, "Audits all " + plates.Length + " plates, including inactive screens");
                foreach (string name in new[] { "PatienceFill", "OrderPatienceFill" }) {
                    var p = Find(name).GetComponent<Image>();
                    foreach (float amount in new[] { -1f, 0f, .001f, .02f, .5f, 1f, 2f }) {
                        fill.Invoke(null, new object[] { p, amount });
                        var mesh = new VertexHelper();
                        p.GetType().GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(VertexHelper) }, null).Invoke(p, new object[] { mesh });
                        Check(p.rectTransform.localScale == Vector3.one, name + " never scales its rounded shape at " + amount);
                        Check(amount > 0 ? mesh.currentVertCount > 2 : mesh.currentVertCount == 0, name + " handles empty/full fractions");
                        var r = p.GetPixelAdjustedRect();
                        float right = r.xMin + r.width * Mathf.Clamp01(amount);
                        for (int i = 0; i < mesh.currentVertCount; i++) {
                            var vertex = new UIVertex(); mesh.PopulateUIVertex(ref vertex, i);
                            if (vertex.position.x < r.xMin - .001f || vertex.position.x > right + .001f || vertex.position.y < r.yMin - .001f || vertex.position.y > r.yMax + .001f)
                                throw new Exception(name + " fill escapes its progress boundary");
                        }
                        mesh.Dispose();
                    }
                }
            }
            string failure = Path.Combine(output, "FAILED.txt"); if (File.Exists(failure)) File.Delete(failure);
            File.WriteAllLines(Path.Combine(output, "PASSED.txt"), checks);
            Debug.Log("UI_PLATE_VALIDATION_PASSED " + checks.Count); EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(output, "FAILED.txt"), e.ToString()); Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
