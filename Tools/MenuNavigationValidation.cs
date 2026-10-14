using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;
using Blinopek.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Run from an Editor folder in an isolated Unity validation project.
public static class MenuNavigationValidation
{
    private sealed class MemoryStore : IGameStore
    {
        public SavedGame Value;
        public SavedGame Load() => Value;
        public void Save(SavedGame value) { Value = value; }
    }
    private static readonly List<string> checks = new List<string>();
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks.Add("PASS " + message); }
    private static Button Button(GameView view, string name)
        => view.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
    private static void Click(GameView view, string name)
    {
        var b = Button(view, name);
        Check(b.gameObject.activeInHierarchy && b.IsInteractable(), name + " is available");
        b.onClick.Invoke(); view.Refresh(); Canvas.ForceUpdateCanvases();
    }
    private static bool Visible(GameView view, string name)
        => view.GetComponentsInChildren<Transform>().Any(t => t.name == name);
    private static void Verify(bool failure)
    {
        var content = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameContent>("Assets/Blinopek/Content/Gameplay.asset"));
        content.balance = new BalanceRules { startingCoins = 10000, rent = failure ? 50000 : 0, dayTicks = 2 };
        var controller = new GameController(new MemoryStore(), new LocalDevelopmentGateway(), content.balance);
        controller.Profile.tutorialComplete = true;
        var view = new GameObject("Navigation validation").AddComponent<GameView>();
        try
        {
            view.Initialize(controller, content); Canvas.ForceUpdateCanvases();
            Check(Visible(view, "Facade") && !Visible(view, "Counter") && controller.Paused, "Startup shows only facade and pauses the level");
            var initial = view.Session;
            Check(!controller.Dispatch(new GameCommand(ActionKind.Tick, 1)) && initial.ElapsedTicks == 0, "Facade stops the level clock");
            Check(content.Find("ui/glyphs/shop.png") != null && content.Find("ui/glyphs/home.png") != null, "Shop and home glyphs are linked in gameplay content");
            Click(view, "FacadeShop");
            var shop = Button(view, "CloseShop").transform.parent;
            Check(shop.GetSiblingIndex() == shop.parent.childCount - 1, "Shop draws above the facade");
            Click(view, "CloseShop");
            Check(Visible(view, "Facade") && !Visible(view, "Shop") && controller.Paused, "Shop closes back to the paused facade");
            Click(view, "ContinueShift");
            Check(view.Session == initial && !controller.Paused && Visible(view, "Counter"), "Play enters the existing level without replacing it");
            Click(view, "Pause"); Click(view, "ExitToFacade"); Click(view, "ContinueShift");
            Check(view.Session == initial && !controller.Paused, "An unfinished level resumes from the facade");
            Click(view, "RecipeBook");
            var titles = view.GetComponentsInChildren<Text>().Where(t => t.name == "RecipeName").ToArray();
            Check(titles.Length == 2 && titles[0].text == "Сладкий\nтреугольник" && titles[1].text == "Грибной\nконверт", "First spread has both recipe names");
            foreach (var title in titles) Check(title.preferredHeight <= title.rectTransform.rect.height, "Recipe title fits: " + title.text.Replace('\n', ' ') + " (" + title.preferredHeight + "/" + title.rectTransform.rect.height + ")");
            Click(view, "NextRecipe");
            Check(view.GetComponentsInChildren<Text>().Any(t => t.name == "RecipeName" && t.text == "Сладкая\nтрубочка"), "Second spread has a recipe name");
            Click(view, "CloseBook");
            Check(controller.Dispatch(new GameCommand(ActionKind.Tick, 2)), "Level can finish after closing the book"); view.Refresh();
            Check(view.Session.Phase == DayPhase.Result && view.Session.Success != failure && Visible(view, "DayResults") && !Visible(view, "Facade"), "Finished level shows the result before the menu");
            int nextDay = controller.Profile.day;
            Click(view, "ReturnToFacade");
            Check(view.Session == initial && controller.Paused && !Visible(view, "DayResults") && Visible(view, "Facade"), "Result returns to the facade without starting a level");
            Click(view, "FacadeShop");
            if (!failure)
            {
                Click(view, "BuyIngredient1");
                Check(controller.Profile.Owns(Ingredient.Jam), "Purchases work between levels");
            }
            else Check(!Button(view, "BuyIngredient1").interactable, "Failed level preserves purchase restrictions");
            Click(view, "CloseShop"); Click(view, "ContinueShift");
            Check(view.Session != initial && view.Session.StartProfile.day == nextDay && !controller.Paused, "Only facade play starts the next or repeated level");
            Check(nextDay == (failure ? 1 : 2), failure ? "Failure repeats the same day" : "Success advances one day");
            if (!failure) Check(view.Session.StartProfile.Owns(Ingredient.Jam), "Next level receives the purchase");
        }
        finally { UnityEngine.Object.DestroyImmediate(view.gameObject); UnityEngine.Object.DestroyImmediate(content); }
    }
    public static void Run()
    {
        string folder = Path.GetFullPath("../../output/menu-validation"); Directory.CreateDirectory(folder);
        try
        {
            Verify(false); Verify(true);
            string failed = Path.Combine(folder, "FAILED.txt"); if (File.Exists(failed)) File.Delete(failed);
            File.WriteAllLines(Path.Combine(folder, "PASSED.txt"), checks);
            Debug.Log("MENU_NAVIGATION_PASSED " + checks.Count);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(folder, "FAILED.txt"), ex.ToString());
            Debug.LogException(ex); EditorApplication.Exit(1);
        }
    }
}
