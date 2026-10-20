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

// Isolated QA project only; uses its own save file and actual game textures.
public static class CafeTablesValidation
{
    private static readonly List<string> checks = new List<string>();
    private static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static string output;
    private static GameContent art;
    private static GameSystem view;
    private static Camera camera;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks.Add("PASS " + message); }
    private static void Invoke(string method, params object[] args)
    {
        object target = method == "SelectShopPage" ? (object)view.Scene : view;
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    }
    private static void SetRenderField(string name, object value)
        => typeof(CookingScreenValidation).GetField(name, PrivateStatic).SetValue(null, value);
    private static void Render(string name)
    {
        SetRenderField("output", output); SetRenderField("view", view); SetRenderField("camera", camera);
        SetRenderField("canvas", view.GetComponentInChildren<Canvas>());
        SetRenderField("root", view.GetComponentsInChildren<RectTransform>(true).Single(x => x.name == "SafeArea"));
        typeof(CookingScreenValidation).GetMethod("Render", PrivateStatic).Invoke(null, new object[] { name, 1600, 900 });
    }
    private static void Tables(bool dark)
    {
        view.Refresh(); var texture = art.Find(dark ? "decor/table_premium.png" : "decor/table_base.png");
        var tables = view.GetComponentsInChildren<RawImage>(true).Where(x => x.name == "Table" || x.name == "SideTable").ToArray();
        Check(tables.Length >= 2 && tables.All(x => x.texture == texture), dark ? "Both cafe tables use the purchased dark sprite" : "Both cafe tables start light");
        foreach (var table in tables) {
            var r = table.rectTransform; var uv = art.Uv(texture);
            Check(Mathf.Abs(r.rect.width / r.rect.height - texture.width * uv.width / (texture.height * uv.height)) < .001f, table.name + " preserves sprite proportions");
            Check(Mathf.Abs(-r.anchoredPosition.y - r.rect.height / 2 - (table.name == "Table" ? 500 : 440)) < .01f, table.name + " keeps its Figma position");
        }
    }
    public static void Run()
    {
        output = Environment.GetEnvironmentVariable("BLINOPEK_TABLE_OUTPUT"); Directory.CreateDirectory(output);
        try {
            art = (GameContent)typeof(CookingScreenValidation).GetMethod("Artwork", PrivateStatic).Invoke(null, new object[] { Environment.GetEnvironmentVariable("BLINOPEK_ANIMATION_ART") });
            Check(art.balance.darkTableUpgradeCost == 18000, "Upgrade costs 180 coins");
            Check(!JsonUtility.FromJson<Profile>("{\"day\":3,\"coins\":3000}").darkTablesUpgrade, "Legacy saves default to light tables");
            string save = Path.Combine(output, "isolated-save.json");
            var store = new FileGameStore(save);
            store.Save(new SavedGame { profile = new Profile { day = 6, tutorialComplete = true, demonstrationSeen = true, coins = 100000, ownedIngredients = 31 }, rules = art.balance, awaitingDay = true });
            var controller = new GameController(store, new LocalDevelopmentGateway(), art.balance);
            view = new GameObject("Cafe table validation").AddComponent<GameSystem>(); view.Initialize(controller, art);
            camera = new GameObject("Screenshot camera").AddComponent<Camera>(); camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .16f, .13f);
            Invoke("PlayFromMainMenu"); Tables(false); Render("01-default-light");
            controller.Dispatch(new GameCommand(ActionKind.Tick, art.balance.dayTicks + art.balance.guestIntervalTicks));
            Check(controller.Session.Success, "Shop is tested after a successful day");
            Invoke("ShowMainMenu"); Invoke("OpenShop"); Invoke("SelectShopPage", 4); view.Refresh();
            var buy = view.GetComponentsInChildren<Button>(true).Single(x => x.name == "BuyDarkTables");
            Check(buy.gameObject.activeInHierarchy && buy.interactable, "Dark table purchase button is visible and enabled"); Render("02-shop-before-purchase");
            int before = controller.Profile.coins;
            buy.onClick.Invoke(); view.Refresh();
            Check(controller.Profile.darkTablesUpgrade && controller.Profile.coins == before - art.balance.darkTableUpgradeCost, "UI purchase charges once and grants the upgrade");
            Check(!buy.interactable && buy.GetComponentInChildren<Text>().text == "Куплено", "Purchased card is disabled and labelled Куплено");
            Check(!controller.Buy(ShopItem.DarkTables), "Duplicate purchase is rejected"); Tables(true); Render("03-shop-purchased");
            UnityEngine.Object.DestroyImmediate(view.gameObject);
            controller = new GameController(new FileGameStore(save), new LocalDevelopmentGateway(), art.balance);
            Check(controller.Profile.darkTablesUpgrade, "Real JSON save reload retains the purchase");
            view = new GameObject("Reloaded cafe").AddComponent<GameSystem>(); view.Initialize(controller, art);
            Invoke("PlayFromMainMenu"); Tables(true); Render("04-upgrade-after-reload");
            File.WriteAllLines(Path.Combine(output, "PASSED.txt"), checks); Debug.Log("CAFE_TABLES_PASSED " + checks.Count);
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "FAILED.txt"), ex.ToString()); Debug.LogException(ex); EditorApplication.Exit(2); }
    }
}
