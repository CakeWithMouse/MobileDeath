#if UNITY_EDITOR || DEVELOPMENT_BUILD || BLINOPEK_QA
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Blinopek.Bootstrap
{
    // Opt-in QA player smoke test. Requires a separate -blinopekSave path.
    public sealed partial class CoreSmokeDriver : MonoBehaviour
    {
        public GameView View;
        private string output;
        private readonly List<string> assertions = new List<string>();
        private IEnumerator Start()
        {
            UnityEngine.Application.runInBackground = true;
            string[] args = Environment.GetCommandLineArgs(); int arg = Array.IndexOf(args, "-blinopekSave");
            if (arg < 0 || arg + 1 >= args.Length) { Debug.LogError("Smoke test requires isolated -blinopekSave."); UnityEngine.Application.Quit(2); yield break; }
            output = Path.Combine(Path.GetDirectoryName(args[arg + 1]), "screenshots"); Directory.CreateDirectory(output);
            yield return null; yield return null;
            var routines = new Stack<IEnumerator>(); routines.Push(Run());
            while (routines.Count > 0)
            {
                object current;
                try {
                    var routine = routines.Peek();
                    if (!routine.MoveNext()) { routines.Pop(); continue; }
                    current = routine.Current;
                    if (current is IEnumerator nested) { routines.Push(nested); continue; }
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(output, "FAILED.txt"), ex.ToString()); File.WriteAllLines(Path.Combine(output, "progress.txt"), assertions); Debug.LogException(ex); UnityEngine.Application.Quit(2); yield break; }
                yield return current;
            }
            File.WriteAllLines(Path.Combine(output, "PASSED.txt"), assertions); UnityEngine.Application.Quit(0);
        }
        private void Check(bool pass, string message) { if (!pass) throw new Exception(message); assertions.Add("PASS " + message); }
        private void Resume() { View.Controller.SetPause(PauseReason.Focus | PauseReason.Background | PauseReason.Menu, false); }
        private IEnumerator Shot(string name)
        {
            View.Refresh(); Canvas.ForceUpdateCanvases(); yield return new WaitForEndOfFrame();
            // Hidden Windows players can have no back buffer. Render the actual Canvas into a target
            // through the same URP camera, then restore its normal screen-overlay mode before input.
            var canvas = View.GetComponentInChildren<Canvas>(); var camera = Camera.main;
            var mode = canvas.renderMode; var canvasCamera = canvas.worldCamera; float distance = canvas.planeDistance;
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            Texture2D screenshot = null;
            try {
                camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases();
                if (GraphicsSettings.currentRenderPipeline != null)
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                else camera.Render();
                RenderTexture.active = target; screenshot = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); screenshot.Apply();
                var colors = new HashSet<Color32>();
                for (int y = 40; y < target.height; y += 70) for (int x = 40; x < target.width; x += 90) colors.Add(screenshot.GetPixel(x, y));
                Check(colors.Count > 8, name + " renders visible graphics");
                File.WriteAllBytes(Path.Combine(output, name + ".png"), screenshot.EncodeToPNG());
            }
            finally {
                canvas.renderMode = mode; canvas.worldCamera = canvasCamera; canvas.planeDistance = distance;
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target); if (screenshot != null) Destroy(screenshot); Canvas.ForceUpdateCanvases();
            }
            yield return null; yield return null;
        }
        private void Click(string name)
        {
            var go = GameObject.Find(name); Check(go != null, name + " exists");
            var button = go.GetComponent<Button>(); Check(button != null && button.IsInteractable(), name + " is interactable");
            var eventData = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(go, eventData, ExecuteEvents.pointerClickHandler); View.Refresh(); Canvas.ForceUpdateCanvases();
        }
        private static Vector2 Position(RectTransform rect, float x, float y)
        { return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3((x - .5f) * rect.rect.width, (y - .5f) * rect.rect.height))); }
        private void Drag(string surfaceName, Vector2 from, Vector2 to, bool checkHit = true)
        {
            var surface = GameObject.Find(surfaceName); Check(surface != null, surfaceName + " is active");
            var pointer = new PointerEventData(EventSystem.current) { pointerId = 1, position = from, button = PointerEventData.InputButton.Left };
            if (checkHit) { var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits); Check(hits.Count > 0 && hits[0].gameObject == surface, surfaceName + " receives the pointer through graphics (hit " + (hits.Count > 0 ? hits[0].gameObject.name : "none") + " at " + from + ")"); }
            ExecuteEvents.Execute(surface, pointer, ExecuteEvents.pointerDownHandler);
            pointer.position = to; ExecuteEvents.Execute(surface, pointer, ExecuteEvents.dragHandler); ExecuteEvents.Execute(surface, pointer, ExecuteEvents.pointerUpHandler);
            View.Refresh(); Canvas.ForceUpdateCanvases();
        }
        private void Paint(string surfaceName)
        {
            var rect = GameObject.Find(surfaceName).GetComponent<RectTransform>();
            for (int y = 60; y <= 940; y += 110)
            {
                int edge = (int)Math.Sqrt(250000 - (y - 500) * (y - 500));
                Drag(surfaceName, Position(rect, (501 - edge) / 1000f, y / 1000f), Position(rect, (499 + edge) / 1000f, y / 1000f));
            }
        }
        private IEnumerator Run()
        {
            View.Controller.SetPause(PauseReason.Focus | PauseReason.Background, false);
            Check(GameObject.Find("Facade") != null && View.Controller.Paused, "Game opens at the facade with the shift paused");
            yield return Shot("00-facade");
            Click("FacadeShop"); yield return Shot("00-facade-shop"); Click("CloseShop");
            Check(GameObject.Find("Facade") != null && View.Controller.Paused, "Closing shop returns to the paused facade");
            Click("ContinueShift");
            Check(!View.Controller.Paused && GameObject.Find("Facade") == null, "Facade play starts the shift");
            yield return Shot("01-counter"); Click("AcceptOrder"); yield return Shot("02-kitchen");
            yield return VerifyGestureAudio();
            Paint("PanSurface"); Check(View.Session.Pancake.Batter.Percent >= 90, "Pointer strokes cover the pan");
            Click("FinishBatter"); yield return VerifyAudio(); View.Command(ActionKind.Tick, 200); yield return Shot("03-burnt-lesson");
            var pan = GameObject.Find("PanSurface").GetComponent<RectTransform>(); var bin = GameObject.Find("DiscardZone").GetComponent<RectTransform>();
            Drag("PanSurface", Position(pan, .5f, .5f), Position(bin, .5f, .5f));
            Check(View.Session.Pancake == null && View.Session.DemonstrationSeen, "Dragging to the bin completes demonstration");
            Paint("PanSurface"); Click("FinishBatter"); View.Command(ActionKind.Tick, 200); View.Refresh();
            Click("FlipHandle"); View.Command(ActionKind.Tick, 200); View.Refresh(); yield return Shot("04-second-side");
            var plate = GameObject.Find("PlateSurface").GetComponent<RectTransform>();
            Drag("PanSurface", Position(pan, .5f, .5f), Position(plate, .5f, .5f));
            Check(View.Session.Pancake.Stage == PancakeStage.Plate, "Dragging moves a cooked pancake to the plate");
            Paint("PlateSurface"); yield return Shot("05-butter"); Click("Fold0");
            Drag("PlateSurface", Position(plate, .5f, .2f), Position(plate, .5f, .8f)); yield return Shot("06-fold");
            Drag("PlateSurface", Position(plate, .2f, .7f), Position(plate, .8f, .7f));
            Check(View.Session.Order.Phase == OrderPhase.Served && View.Session.Order.Report.Total >= 95, "Pointer-driven tutorial serves a correct order");
            yield return new WaitForSecondsRealtime(2);
            Check(View.Audio.PlayedCount(SoundCue.Serve) == 1 && View.Audio.PlayedCount(SoundCue.Payment) == 1, "Tutorial serving and payment clips play once");
            yield return Shot("07-served");
            View.Command(ActionKind.Tick, View.Session.Rules.dayTicks); View.Refresh();
            Check(View.Session.Phase == DayPhase.Result && View.Controller.Profile.day == 2, "Day settles and advances"); yield return Shot("08-results");
            var completedSession = View.Session;
            Click("ReturnToFacade");
            Check(View.Session == completedSession && View.Session.Phase == DayPhase.Result && View.Controller.Paused, "Result returns to facade without starting another shift");
            yield return Shot("08-facade-after-results");
            Click("FacadeShop"); yield return Shot("09-shop");
            Click("BuyIngredient1"); Check(View.Controller.Profile.Owns(Ingredient.Jam), "Shop unlock persists");
            Click("BuyIngredient3"); Click("UpgradeTab"); Click("BuyScraper");
            Click("CloseShop"); Click("ContinueShift");
            Check(View.Session.StartProfile.Owns(Ingredient.Jam) && View.Session.StartProfile.day == 2, "Next shift receives purchases");
            Click("RecipeBook"); int beforePause = View.Session.ElapsedTicks;
            var names = View.GetComponentsInChildren<Text>();
            Check(Array.Exists(names, t => t.name == "RecipeName" && t.text == "Сладкий\nтреугольник") && Array.Exists(names, t => t.name == "RecipeName" && t.text == "Грибной\nконверт"), "First recipe spread has recipe names");
            yield return Shot("recipes-first"); Click("NextRecipe");
            Check(Array.Exists(View.GetComponentsInChildren<Text>(), t => t.name == "RecipeName" && t.text == "Сладкая\nтрубочка"), "Second recipe spread has a recipe name");
            yield return Shot("recipes-second");
            Check(View.Controller.Paused && !View.Command(ActionKind.Tick, 20) && View.Session.ElapsedTicks == beforePause, "Recipe book pauses the real view controller");
            Click("CloseBook"); Check(!View.Controller.Paused, "Recipe book closes without leaving a pause");
            Click("AcceptOrder"); yield return null; Canvas.ForceUpdateCanvases();
            int price = View.Session.Order.Price;
            for (int portion = 0; portion < View.Session.Order.Portions.Count; portion++)
            {
                var recipe = View.Session.Order.Portions[portion];
                Paint("PanSurface"); Click("FinishBatter"); View.Command(ActionKind.Tick, View.Session.Rules.readyTicks);
                Click("FlipHandle"); View.Command(ActionKind.Tick, View.Session.Rules.readyTicks); View.Refresh(); Click("MoveToPlate");
                yield return null; Canvas.ForceUpdateCanvases(); // Newly visible ingredient graphics enter the raycast registry on the next frame.
                if (portion == 0) {
                    var curd = GameObject.Find("Ingredient3").GetComponent<RectTransform>();
                    Drag("Ingredient3", Position(curd, .5f, .5f), Position(plate, .32f, .32f));
                    Check(View.Session.Pancake.Fillings[3]?.Portions.Count == 1, "Ingredient drag places a portion on the plate");
                    Click("Scraper"); Check(View.Session.Pancake.Fillings[3] == null && View.Session.Scrapers == 0, "Scraper UI removes the selected filling and consumes one tool");
                }
                for (int ingredient = 0; ingredient < 5; ingredient++) if (recipe.Needs((Ingredient)ingredient)) {
                    var card = GameObject.Find("Ingredient" + ingredient).GetComponent<RectTransform>();
                    if (ingredient < 3) { Drag("Ingredient" + ingredient, Position(card, .5f, .5f), Position(card, .5f, .5f)); Paint("PlateSurface"); }
                    else foreach (var point in Recipes.PortionTargets)
                        Drag("Ingredient" + ingredient, Position(card, .5f, .5f), Position(plate, point.X / 1000f, point.Y / 1000f));
                }
                Click("Fold" + (int)recipe.Shape);
                foreach (var direction in Recipes.Gestures(recipe.Shape)) {
                    Vector2 from = new Vector2(.5f, .5f), to = from;
                    if (direction == Direction.Up) { from.y = .2f; to.y = .8f; }
                    if (direction == Direction.Down) { from.y = .8f; to.y = .2f; }
                    if (direction == Direction.Right) { from.x = .2f; to.x = .8f; }
                    if (direction == Direction.Left) { from.x = .8f; to.x = .2f; }
                    Drag("PlateSurface", Position(plate, from.x, from.y), Position(plate, to.x, to.y));
                }
                // Rapid QA gestures queue visual folds; let delivery finish before painting the next portion.
                yield return new WaitForSecondsRealtime(CookingMotion.FoldSeconds * Recipes.Gestures(recipe.Shape).Length + .1f);
            }
            Check(View.Session.Order.Phase == OrderPhase.Served && View.Session.Order.Report.Total >= 95, "Ordinary order serves every requested portion with correct fillings and folds");
            Check(View.Session.Ledger.Products > 0 && View.Session.Ledger.Revenue == price, "Ordinary cooking spends products and pays acceptance once");
            yield return new WaitForSecondsRealtime(3);
            yield return Shot("10-ordinary-served");
        }
    }
}
#endif
