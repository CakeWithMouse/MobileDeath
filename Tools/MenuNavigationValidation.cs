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
            Check(Visible(view, "MainMenu") && !Visible(view, "Counter") && controller.Paused, "Startup shows only main menu and pauses the level");
            Check(view.Session == null && !controller.Dispatch(new GameCommand(ActionKind.Tick, 1)), "Startup waits for play before creating a level");
            Check(content.Find("ui/glyphs/shop.png") != null && content.Find("ui/glyphs/home.png") != null, "Shop and home glyphs are linked in gameplay content");
            Click(view, "MainMenuShop");
            var shop = Button(view, "CloseShop").transform.parent;
            Check(shop.GetSiblingIndex() == shop.parent.childCount - 1, "Shop draws above the main menu");
            Click(view, "CloseShop");
            Check(Visible(view, "MainMenu") && !Visible(view, "Shop") && controller.Paused, "Shop closes back to the paused main menu");
            Click(view, "ContinueShift");
            var initial = view.Session;
            Check(initial != null && !controller.Paused && Visible(view, "Counter"), "Play starts the level");
            Click(view, "Pause");
            var menuLabel = view.GetComponentsInChildren<Text>().Single(t => t.name == "ExitHelp");
            Check(menuLabel.text == "Главное меню", "Pause uses the canonical main menu title");
            Check(menuLabel.preferredWidth <= menuLabel.rectTransform.rect.width && menuLabel.preferredHeight <= menuLabel.rectTransform.rect.height, "Main menu label fits without clipping");
            Click(view, "ExitToMainMenu");
            Check(!Visible(view, "MainMenuShop"), "Unfinished level cannot open the shop from the main menu");
            Click(view, "ContinueShift");
            Check(view.Session == initial && !controller.Paused, "An unfinished level resumes from the main menu");
            Click(view, "RecipeBook");
            var titles = view.GetComponentsInChildren<Text>().Where(t => t.name == "RecipeName").ToArray();
            Check(titles.Length == 2 && titles[0].text == "Сладкий\nтреугольник" && titles[1].text == "Грибной\nконверт", "First spread has both recipe names");
            foreach (var title in titles) Check(title.preferredHeight <= title.rectTransform.rect.height, "Recipe title fits: " + title.text.Replace('\n', ' ') + " (" + title.preferredHeight + "/" + title.rectTransform.rect.height + ")");
            Click(view, "NextRecipe");
            Check(view.GetComponentsInChildren<Text>().Any(t => t.name == "RecipeName" && t.text == "Сладкая\nтрубочка"), "Second spread has a recipe name");
            Click(view, "CloseBook");
            Check(controller.Dispatch(new GameCommand(ActionKind.Tick, content.balance.dayTicks + Math.Max(1, content.balance.guestIntervalTicks))), "Level finishes after the closing guest leaves"); view.Refresh();
            Check(view.Session.Phase == DayPhase.Result && view.Session.Success != failure && Visible(view, "DayResults") && !Visible(view, "MainMenu"), "Finished level shows the result before the menu");
            int nextDay = controller.Profile.day;
            Click(view, "ReturnToMainMenu");
            Check(view.Session == initial && controller.Paused && !Visible(view, "DayResults") && Visible(view, "MainMenu"), "Result returns to the main menu without starting a level");
            Click(view, "MainMenuShop");
            if (!failure)
            {
                Click(view, "BuyIngredient1");
                Check(controller.Profile.Owns(Ingredient.Jam), "Purchases work between levels");
            }
            else Check(!Button(view, "BuyIngredient1").interactable, "Failed level preserves purchase restrictions");
            Click(view, "CloseShop"); Click(view, "ContinueShift");
            Check(view.Session != initial && view.Session.StartProfile.day == nextDay && !controller.Paused, "Only main menu play starts the next or repeated level");
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
