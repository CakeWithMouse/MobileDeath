using System;
using System.Collections.Generic;
using Blinopek.Application;
using Blinopek.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    // Coordinates and cropped artwork follow the artist's 1600 x 900 Figma export, node 4199:520.
    public sealed partial class GameView
    {
        private Text dayText, honeyText, scraperText, resultDay, profitText;
        private GameObject orderDetails, tutorialPanel, orderHud, facade;
        private Image kitchenPatienceBar;
        private RawImage sideArtA, sideArtB;
        private RawImage panGuide;
        private GameObject sideReadyA, sideReadyB;
        private readonly List<GameObject> ingredientSelections = new List<GameObject>();
        private readonly List<GameObject> helperBadges = new List<GameObject>();
        private readonly List<Text> resultRows = new List<Text>();
        private readonly List<GameObject> shopPages = new List<GameObject>();
        private readonly List<GameObject> recipePages = new List<GameObject>();
        private bool atFacade;

        private RawImage Art(string name, Transform parent, string key, float x, float y, float w, float h, bool preserve = true)
            => Picture(name, parent, art.Find(key), x, y, w, h, preserve);
        private RawImage Paper(string name, Transform parent, float x, float y, float w, float h)
            => Art(name, parent, "ui/panel-cream.png", x, y, w, h, false);
        private Button IconButton(string name, Transform parent, string key, float x, float y, float w, float h, Action action, bool background = true)
        {
            var b = Button(name, parent, "", x, y, w, h, action);
            if (!background) b.targetGraphic.color = Color.clear;
            Art("Icon", b.transform, key, background ? 20 : 0, background ? 12 : 0, background ? w - 40 : w, background ? h - 24 : h);
            return b;
        }
        private void Shadow(Transform parent, string key, float x, float y, float w, float h)
            => Art("Shadow", parent, "kitchen/shadows/" + key + ".png", x - .08f * w, y + .77f * h, w * 1.2f, h * .3f, false);
        private void ServiceBackground(Transform parent)
        {
            Picture("Room", parent, art.counterBackground, 0, 0, 1600, 900, false);
            Art("Chair", parent, "decor/chair_premium.png", 1025, 414, 160, 280);
            Art("Table", parent, "decor/table_premium.png", 846, 500, 320, 247);
            Art("Stool", parent, "decor/stool_base.png", 1350, 580, 150, 180);
            Art("SideTable", parent, "decor/table_base.png", 1270, 440, 270, 230);
            Art("Flowers", parent, "decor/vase_flowers.png", 1000, 391, 100, 130);
            Art("Lamp", parent, "decor/lamp_brass.png", 1140, -38, 165, 240);
        }
        private GameObject Modal(string name)
        {
            var group = Group(name);
            Panel("Dim", group.transform, 0, 0, 1600, 900, new Color(.14f, .18f, .14f, .55f));
            return group;
        }
        private void OpenBook() { book.SetActive(true); controller.SetPause(PauseReason.RecipeBook, true); Audio.PlayUi(SoundCue.BookOpen); }
        private void CloseBook() { book.SetActive(false); controller.SetPause(PauseReason.RecipeBook, false); Audio.PlayUi(SoundCue.BookClose); }
        private void OpenShop() { shop.transform.SetAsLastSibling(); shop.SetActive(true); dirty = true; }
        private void ShowFacade()
        {
            controller.SetPause(PauseReason.Menu, true);
            ReleasePointer(); HidePortionGhost();
            atFacade = true; facade.SetActive(true); shop.SetActive(false); pausePanel.SetActive(false);
            Audio.PlayUi(SoundCue.Back); dirty = true;
        }
        private void PlayFromFacade()
        {
            if (controller.StorageBlocked) return;
            if (Session == null || Session.Phase == DayPhase.Result) StartNextDay();
            if (Session == null || Session.Phase == DayPhase.Result) return;
            atFacade = false; facade.SetActive(false); shop.SetActive(false);
            controller.SetPause(PauseReason.Menu, false); dirty = true;
        }

        private void Build()
        {
            var cg = new GameObject("CafeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); cg.transform.SetParent(transform, false);
            canvas = cg.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = cg.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            root = Rect("SafeArea", cg.transform, 0, 0, 1600, 900); root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            if (FindAnyObjectByType<EventSystem>() == null)
            { var es = new GameObject("CafeInput", typeof(EventSystem), typeof(InputSystemUIInputModule)); es.transform.SetParent(transform); es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions(); }
            counter = Group("Counter"); BuildCounter();
            kitchen = Group("Kitchen"); BuildKitchen();
            BuildHud(); BuildResults(); BuildShop(); BuildBook(); BuildFacade(); BuildPause();
            storage = Label("StorageError", root, "", 270, 760, 1060, 38, 20, new Color(.65f, .14f, .1f));
            ghost = Picture("DraggedPortion", root, art.fillings[3], 0, 0, 70, 60, false); ghost.gameObject.SetActive(false);
        }
        private void BuildHud()
        {
            Paper("TimePanel", root, 22, 18, 325, 78);
            Art("Day", root, "ui/day-sun.png", 35, 28, 50, 48);
            dayText = Label("DayNumber", root, "", 91, 28, 50, 51, 30);
            Art("ClockIcon", root, "ui/clock-day.png", 147, 26, 52, 53);
            clock = Label("Clock", root, "", 211, 28, 126, 51, 30);
            Paper("WalletPanel", root, 1040, 18, 400, 78);
            Art("Coin", root, "ui/coin-pancake.png", 1058, 29, 56, 51);
            hud = Label("Wallet", root, "", 1120, 28, 130, 51, 29);
            Art("Honey", root, "ui/currency-honey.png", 1257, 26, 45, 55);
            honeyText = Label("HoneyAmount", root, "", 1310, 28, 112, 51, 30);
            IconButton("Pause", root, "ui/glyphs/pause.png", 1462, 18, 112, 78, () => controller.SetPause(PauseReason.Menu, true));
            orderHud = Rect("KitchenOrderHud", root, 402, 18, 591, 78).gameObject;
            var receipt = IconButton("ShowOrder", orderHud.transform, "ui/receipt-order.png", 0, 0, 591, 78, () => orderDetails.SetActive(!orderDetails.activeSelf));
            var receiptPaper = receipt.GetComponent<RawImage>(); receiptPaper.texture = art.Find("ui/panel-cream.png"); receiptPaper.uvRect = art.Uv(receiptPaper.texture);
            var icon = receipt.transform.Find("Icon").GetComponent<RectTransform>(); icon.anchoredPosition = new Vector2(42, -39); icon.sizeDelta = new Vector2(40, 52);
            progress = Label("Progress", orderHud.transform, "", 87, 10, 116, 56, 30);
            Art("OrderPatienceTrack", orderHud.transform, "ui/patience-track.png", 222, 20, 335, 37, false);
            kitchenPatienceBar = Panel("OrderPatienceFill", orderHud.transform, 249, 31, 281, 12, Green); kitchenPatienceBar.raycastTarget = false;
            orderDetails = Rect("OrderDetails", root, 402, 110, 591, 180).gameObject;
            Paper("Paper", orderDetails.transform, 0, 0, 591, 180).raycastTarget = true;
            orderStrip = Label("Order", orderDetails.transform, "", 30, 20, 531, 140, 25);
            orderDetails.SetActive(false);
        }
        private void BuildCounter()
        {
            ServiceBackground(counter.transform);
            visitor = Picture("Visitor", counter.transform, art.visitors[0], 94, 191, 470, 792);
            Picture("ServiceCounter", counter.transform, art.counter, -30, 723, 1660, 240, false);
            Art("Delivery", counter.transform, "decor/service_delivery.png", 1040, 755, 400, 110);
            Art("CounterPitcher", counter.transform, "decor/pitcher.png", 1444, 658, 112, 183);
            Art("SpeechBubble", counter.transform, "ui/speech-bubble.png", 620, 145, 825, 285, false);
            dialogue = Label("Request", counter.transform, "", 737, 160, 600, 195, 34, Ink, true); dialogue.alignment = TextAnchor.MiddleCenter;
            accept = IconButton("AcceptOrder", counter.transform, "ui/glyphs/check.png", 856, 448, 176, 86, () => { if (Command(ActionKind.Accept)) SwitchKitchen(true); });
            clarify = IconButton("ClarifyOrder", counter.transform, "ui/glyphs/help.png", 1065, 448, 176, 86, () => Command(ActionKind.Clarify));
            nextGuest = IconButton("NextGuest", counter.transform, "ui/glyphs/arrow-right.png", 930, 448, 260, 86, () => Command(ActionKind.NextGuest));
            Art("PatienceTrack", counter.transform, "ui/patience-track.png", 747, 560, 580, 53, false);
            patienceBar = Panel("PatienceFill", counter.transform, 785, 579, 500, 14, Green); patienceBar.raycastTarget = false;
            patienceText = Label("Patience", counter.transform, "", 747, 615, 580, 32, 21); patienceText.alignment = TextAnchor.MiddleCenter;
            reaction = Label("Reaction", counter.transform, "", 650, 650, 740, 66, 22); reaction.alignment = TextAnchor.MiddleCenter;
            IconButton("RecipeBook", counter.transform, "ui/recipe-book-closed.png", 28, 770, 108, 104, OpenBook, false);
            toKitchen = IconButton("KitchenTab", counter.transform, "ui/glyphs/arrow-right.png", 1448, 795, 120, 80, () => SwitchKitchen(true));
            displays.Add(Display(counter.transform, "ServedPancake", 1150, 770, 240, 90));
        }
        private void BuildKitchen()
        {
            Picture("KitchenRoom", kitchen.transform, art.kitchenBackground, 0, 0, 1600, 900, false);
            Shadow(kitchen.transform, "pitcher", 18, 265, 260, 260);
            Art("BatterPitcher", kitchen.transform, "kitchen/batter-pitcher-floral-v2.png", 18, 265, 260, 260);
            Shadow(kitchen.transform, "tray", 1140, 200, 395, 139);
            Picture("ReadyTray", kitchen.transform, art.tray, 1140, 200, 395, 139, false);
            Shadow(kitchen.transform, "plate", 1103, 351, 472, 291);
            Picture("Plate", kitchen.transform, art.plate, 1103, 351, 472, 291, false);
            var panSurface = Rect("PanSurface", kitchen.transform, 377, 308, 363, 235);
            var panHit = panSurface.gameObject.AddComponent<Image>(); panHit.color = Color.clear;
            var panRouter = panSurface.gameObject.AddComponent<KitchenInputRouter>(); panRouter.View = this; panRouter.Pan = true;
            panGuide = Art("BatterGuide", panSurface, "kitchen/guide-batter-circle.png", 0, 0, 363, 235, false); panGuide.color = new Color(1, .94f, .75f, .65f);
            displays.Add(Display(panSurface, "PancakeOnPan", 0, 0, 363, 235));
            plateSurface = Rect("PlateSurface", kitchen.transform, 1158, 390, 362, 213);
            var plateHit = plateSurface.gameObject.AddComponent<Image>(); plateHit.color = Color.clear;
            plateSurface.gameObject.AddComponent<KitchenInputRouter>().View = this;
            displays.Add(Display(plateSurface, "PancakeOnPlate", 0, 0, 362, 213));
            displays.Add(Display(kitchen.transform, "ReadyPancake", 1192, 220, 126, 90));
            for (int i = 0; i < 4; i++)
            {
                var p = Recipes.PortionTargets[i]; var marker = Label("PortionGuide", plateSurface, "+", p.X * .362f - 16, (1000 - p.Y) * .213f - 16, 32, 32, 29, Green);
                marker.alignment = TextAnchor.MiddleCenter; portionGuides.Add(marker);
            }
            var positions = new[] { new Rect(1029, 526, 112, 105), new Rect(864, 255, 141, 127), new Rect(1015, 255, 116, 130), new Rect(882, 403, 140, 119), new Rect(886, 540, 133, 96) };
            string[] names = { "butter", "jam", "honey", "curd", "mushrooms" };
            for (int i = 0; i < 5; i++)
            {
                var p = positions[i]; var card = Panel("Ingredient" + i, kitchen.transform, p.x, p.y, p.width, p.height, Color.clear); ingredientCards.Add(card);
                Shadow(card.transform, names[i], 0, 0, p.width, p.height);
                var ring = Art("SelectedIngredient", card.transform, "kitchen/guide-batter-circle.png", -6, -6, p.width + 12, p.height + 12, false); ring.color = new Color(.39f, .55f, .42f, .85f); ingredientSelections.Add(ring.gameObject);
                Picture("Container", card.transform, art.containers[i], 0, 0, p.width, p.height);
                var badge = Art("HelperBadge", card.transform, "ui/helper-badge.png", p.width - 19, p.height - 15, 27, 27); helperBadges.Add(badge.gameObject);
                var label = Label("Name", card.transform, Recipes.IngredientName((Ingredient)i), -20, p.height + 2, p.width + 40, 25, 18); label.alignment = TextAnchor.MiddleCenter; ingredientLabels.Add(label);
                var drag = card.gameObject.AddComponent<IngredientDrag>(); drag.View = this; drag.Ingredient = i;
            }
            Paper("HeatStatus", kitchen.transform, 370, 151, 330, 62);
            heatA = Label("SideA", kitchen.transform, "1", 390, 158, 24, 35, 22);
            heatB = Label("SideB", kitchen.transform, "2", 552, 158, 24, 35, 22);
            sideArtA = Picture("SideAImage", kitchen.transform, art.raw, 423, 165, 49, 34);
            sideArtB = Picture("SideBImage", kitchen.transform, art.raw, 585, 165, 49, 34);
            sideReadyA = Art("SideAReady", kitchen.transform, "ui/glyphs/check.png", 480, 171, 23, 23).gameObject;
            sideReadyB = Art("SideBReady", kitchen.transform, "ui/glyphs/check.png", 642, 171, 23, 23).gameObject;
            heatBarA = Panel("HeatA", kitchen.transform, 390, 198, 120, 4, Gold); heatBarA.raycastTarget = false;
            heatBarB = Panel("HeatB", kitchen.transform, 552, 198, 120, 4, Gold); heatBarB.raycastTarget = false;
            finishBatter = Button("FinishBatter", kitchen.transform, "Начать жарку", 397, 588, 282, 62, () => Command(ActionKind.FinishBatter));
            flip = Button("FlipHandle", kitchen.transform, "Перевернуть", 397, 588, 282, 62, () => Command(ActionKind.Flip));
            move = Button("MoveToPlate", kitchen.transform, "На тарелку →", 397, 588, 282, 62, () => Command(ActionKind.MoveToPlate));
            for (int i = 0; i < 3; i++)
            {
                int shape = i; string[] forms = { "triangle", "envelope", "roll" };
                var b = IconButton("Fold" + i, kitchen.transform, "kitchen/fold-" + forms[i] + ".png", 1145 + i * 145, 665, 135, 82, () => Command(ActionKind.BeginFold, shape)); foldButtons.Add(b);
            }
            foldGuide = Label("FoldGuide", kitchen.transform, "", 1120, 668, 450, 66, 25); foldGuide.alignment = TextAnchor.MiddleCenter;
            finishFold = Button("FinishIncorrectFold", kitchen.transform, "Закончить как есть", 1170, 735, 365, 55, () => Command(ActionKind.FinalizeFold));
            IconButton("CounterTab", kitchen.transform, "ui/glyphs/arrow-left.png", 24, 804, 110, 76, () => SwitchKitchen(false));
            IconButton("KitchenRecipeBook", kitchen.transform, "ui/recipe-book-closed.png", 159, 797, 87, 90, OpenBook, false);
            remove = IconButton("Scraper", kitchen.transform, "kitchen/scraper.png", 1271, 805, 203, 75, () => Command(ActionKind.Remove, SelectedIngredient));
            var scraperIcon = remove.transform.Find("Icon").GetComponent<RectTransform>(); scraperIcon.anchoredPosition = new Vector2(48, -37); scraperIcon.sizeDelta = new Vector2(69, 61);
            scraperText = Label("ScraperCount", remove.transform, "", 110, 6, 82, 63, 30);
            trashZone = Rect("DiscardZone", kitchen.transform, 1495, 805, 87, 75);
            discard = IconButton("Discard", trashZone, "kitchen/discard-bowl.png", 0, 0, 87, 75, () => Command(ActionKind.Discard));
            tutorialPanel = Rect("TutorialHint", kitchen.transform, 282, 797, 952, 86).gameObject;
            Paper("Paper", tutorialPanel.transform, 0, 0, 952, 86);
            hint = Label("Hint", tutorialPanel.transform, "", 25, 8, 902, 70, 22); hint.alignment = TextAnchor.MiddleCenter;
        }

        private void BuildResults()
        {
            results = Group("DayResults"); ServiceBackground(results.transform);
            Panel("Dim", results.transform, 0, 0, 1600, 900, new Color(.14f, .18f, .14f, .55f));
            Paper("DayReceipt", results.transform, 335, 118, 930, 693);
            Art("DaySun", results.transform, "ui/day-sun.png", 651, 152, 70, 70);
            resultDay = Label("DayNumber", results.transform, "", 749, 153, 200, 68, 47);
            string[] icons = { "order-plate", "tip-coins", "refund-coins", "receipt-order", "repair-stove" };
            string[] labels = { "Выручка", "Чаевые", "Возвраты", "Продукты", "Аренда" };
            for (int i = 0; i < 5; i++)
            {
                int y = 244 + i * 77;
                Art("ReceiptIcon", results.transform, "ui/" + icons[i] + ".png", 490, y, 65, 62);
                Label("RowName", results.transform, labels[i], 587, y, 300, 62, 25);
                var amount = Label("RowAmount", results.transform, "", 900, y, 210, 62, 33); amount.alignment = TextAnchor.MiddleRight; resultRows.Add(amount);
                var rule = Panel("Rule", results.transform, 587, y + 65, 523, 1, new Color(.4f, .35f, .26f, .22f)); rule.raycastTarget = false;
            }
            Art("ProfitCoin", results.transform, "ui/coin-pancake.png", 561, 660, 92, 86);
            profitText = Label("Profit", results.transform, "", 700, 657, 410, 82, 50);
            resultText = Label("ResultMessage", results.transform, "", 425, 750, 760, 36, 22); resultText.alignment = TextAnchor.MiddleCenter;
            IconButton("ReturnToFacade", results.transform, "ui/glyphs/home.png", 1320, 707, 142, 95, ShowFacade);
            results.SetActive(false);
        }
        private void SelectShopPage(int page) { for (int i = 0; i < shopPages.Count; i++) shopPages[i].SetActive(i == page); dirty = true; }
        private void BuildShop()
        {
            shop = Modal("Shop"); Paper("ShopPaper", shop.transform, 180, 152, 1240, 634);
            shopBalance = Label("ShopBalance", shop.transform, "", 390, 205, 820, 45, 25); shopBalance.alignment = TextAnchor.MiddleCenter;
            IconButton("IngredientTab", shop.transform, "kitchen/container-jam.png", 410, 119, 193, 69, () => SelectShopPage(0));
            IconButton("HelperTab", shop.transform, "ui/helper-badge.png", 665, 119, 193, 69, () => SelectShopPage(2));
            IconButton("UpgradeTab", shop.transform, "ui/upgrade-patience.png", 920, 119, 193, 69, () => SelectShopPage(4));
            for (int group = 0; group < 4; group++)
            {
                bool helpers = group >= 2; int first = group % 2 * 4;
                var page = Rect("ShopPage" + group, shop.transform, 0, 0, 1600, 900).gameObject; shopPages.Add(page);
                for (int i = first; i < Math.Min(5, first + 4); i++)
                {
                    int type = i; int x = 235 + (i - first) * 295;
                    Art("Card", page.transform, "ui/card-frame.png", x, 265, 252, 418, false);
                    Picture("IngredientArt", page.transform, art.containers[i], x + 36, 320, 180, 160);
                    if (helpers) Art("Helper", page.transform, "ui/helper-badge.png", x + 154, 431, 55, 55);
                    var title = Label("Name", page.transform, Recipes.IngredientName((Ingredient)i), x + 18, 285, 216, 38, 24); title.alignment = TextAnchor.MiddleCenter;
                    var price = Label("Price", page.transform, "", x + 25, 493, 202, 48, 26); price.alignment = TextAnchor.MiddleCenter;
                    var buy = Button((helpers ? "Helper" : "BuyIngredient") + i, page.transform, "", x + 25, 559, 202, 65,
                        () => Buy(helpers ? ((controller.Profile.helpersOwned & (1 << type)) != 0 ? ShopItem.ToggleHelper : ShopItem.Helper) : ShopItem.Ingredient, type));
                    shopRefreshers.Add(() => {
                        bool owned = controller.Profile.Owns((Ingredient)type), hasHelper = (controller.Profile.helpersOwned & (1 << type)) != 0;
                        price.text = helpers ? (hasHelper ? "Куплен" : art.balance.helperHoneyPrice + " мёда") : (owned ? "Открыто" : Money(art.balance.ingredientUnlockCost));
                        buy.GetComponentInChildren<Text>().text = helpers ? (hasHelper ? (controller.Profile.HelperEnabled((Ingredient)type) ? "Выключить" : "Включить") : "Купить") : (owned ? "✓" : "Купить");
                        buy.interactable = Session.Success && (helpers ? owned && (hasHelper || controller.Profile.honey >= art.balance.helperHoneyPrice) : !owned && controller.Profile.coins >= art.balance.ingredientUnlockCost);
                    });
                }
                int other = group % 2 == 0 ? group + 1 : group - 1;
                IconButton("ShopPageArrow", page.transform, "ui/glyphs/" + (group % 2 == 0 ? "arrow-right" : "arrow-left") + ".png", group % 2 == 0 ? 1275 : 203, 702, 120, 65, () => SelectShopPage(other));
            }
            var upgrades = Rect("ShopUpgrades", shop.transform, 0, 0, 1600, 900).gameObject; shopPages.Add(upgrades);
            for (int i = 0; i < 2; i++)
            {
                bool scraper = i == 0; int x = 480 + i * 340;
                Art("Card", upgrades.transform, "ui/card-frame.png", x, 265, 252, 418, false);
                Art("Upgrade", upgrades.transform, scraper ? "kitchen/scraper.png" : "ui/upgrade-patience.png", x + 36, 320, 180, 160);
                var title = Label("Title", upgrades.transform, scraper ? "Скребок" : "Терпение", x + 18, 285, 216, 38, 24); title.alignment = TextAnchor.MiddleCenter;
                var price = Label("Price", upgrades.transform, Money(scraper ? art.balance.scraperPrice : art.balance.patienceUpgradeCost), x + 25, 493, 202, 48, 26); price.alignment = TextAnchor.MiddleCenter;
                var buy = Button(scraper ? "BuyScraper" : "BuyPatience", upgrades.transform, "Купить", x + 25, 559, 202, 65, () => Buy(scraper ? ShopItem.Scraper : ShopItem.Patience));
                shopRefreshers.Add(() => { buy.interactable = Session.Success && (scraper || !controller.Profile.patienceUpgrade) && controller.Profile.coins >= (scraper ? art.balance.scraperPrice : art.balance.patienceUpgradeCost); buy.GetComponentInChildren<Text>().text = !scraper && controller.Profile.patienceUpgrade ? "Куплено" : "Купить"; });
            }
            IconButton("CloseShop", shop.transform, "ui/glyphs/arrow-left.png", 25, 795, 115, 79, () => shop.SetActive(false));
            SelectShopPage(0); shop.SetActive(false);
        }
        private void ShowRecipePage(int page)
        {
            bool changed = page >= 0 && page < recipePages.Count && !recipePages[page].activeSelf;
            for (int i = 0; i < recipePages.Count; i++) recipePages[i].SetActive(i == page);
            if (changed && audioUiReady) Audio.PlayUi(SoundCue.BookPage);
        }
        private void BuildBook()
        {
            book = Modal("RecipeBookPanel"); Art("OpenBook", book.transform, "ui/recipe-book-open.png", 142, 100, 1320, 716, false);
            string[] shapes = { "triangle", "envelope", "roll" };
            string[] names = { "Сладкий\nтреугольник", "Грибной\nконверт", "Сладкая\nтрубочка" };
            for (int page = 0; page < 2; page++)
            {
                var group = Rect("RecipePage" + page, book.transform, 0, 0, 1600, 900).gameObject; recipePages.Add(group);
                for (int side = 0; side < 2; side++)
                {
                    int shape = page * 2 + side; int x = 260 + side * 620;
                    if (shape > 2)
                    { Label("CookingHelp", group.transform, "Намажьте тесто на круг.\nНачните жарку.\nПереверните готовую сторону.\nПеренесите блин на тарелку.\nДобавьте начинку из заказа.\nВыберите форму и сложите.", x, 245, 420, 330, 28); continue; }
                    var title = Label("RecipeName", group.transform, names[shape], x, 150, 420, 104, 34, Ink, true); title.alignment = TextAnchor.MiddleCenter;
                    Art("Fold", group.transform, "kitchen/fold-" + shapes[shape] + ".png", x + 18, 267, 363, 230);
                    var gesture = Label("Gestures", group.transform, shape == 0 ? "↑   →" : shape == 1 ? "→   ←   ↓   ↑" : "↓   ↓   ↓", x, 510, 420, 55, 34); gesture.alignment = TextAnchor.MiddleCenter;
                    Picture("Filling1", group.transform, art.containers[shape == 1 ? 4 : 0], x + 30, 576, 130, 118);
                    Art("Plus", group.transform, "ui/glyphs/plus.png", x + 176, 602, 45, 45);
                    Picture("Filling2", group.transform, art.containers[shape == 1 ? 3 : 1], x + 245, 576, 130, 118);
                }
            }
            IconButton("PreviousRecipe", book.transform, "ui/glyphs/arrow-left.png", 203, 726, 120, 70, () => ShowRecipePage(0));
            IconButton("NextRecipe", book.transform, "ui/glyphs/arrow-right.png", 1282, 726, 120, 70, () => ShowRecipePage(1));
            IconButton("CloseBook", book.transform, "ui/glyphs/close.png", 1470, 29, 102, 78, CloseBook);
            ShowRecipePage(0); book.SetActive(false);
        }
        private void BuildFacade()
        {
            facade = Group("Facade"); Art("FacadeArt", facade.transform, "decor/facade_base.png", 0, 0, 1600, 900, false);
            IconButton("ContinueShift", facade.transform, "ui/glyphs/play.png", 698, 744, 205, 100, PlayFromFacade);
            IconButton("FacadeShop", facade.transform, "ui/glyphs/shop.png", 940, 744, 120, 100, OpenShop);
            facade.SetActive(false);
        }
        private void BuildPause()
        {
            pausePanel = Modal("PauseMenu"); Paper("Menu", pausePanel.transform, 420, 130, 760, 640);
            var title = Label("Title", pausePanel.transform, "Пауза", 520, 160, 560, 65, 40, Ink, true); title.alignment = TextAnchor.MiddleCenter;
            IconButton("Resume", pausePanel.transform, "ui/glyphs/play.png", 510, 250, 139, 114, () => { controller.SetPause(PauseReason.Menu, false); Audio.PlayUi(SoundCue.Back); });
            var sound = IconButton("Sound", pausePanel.transform, "ui/settings-speaker.png", 730, 250, 139, 114, () => { Audio.SetMuted(!Audio.Muted); Audio.PlayUi(SoundCue.Toggle); });
            soundIcon = sound.transform.Find("Icon").GetComponent<RawImage>();
            IconButton("ExitToFacade", pausePanel.transform, "ui/glyphs/exit.png", 950, 250, 139, 114, ShowFacade);
            Label("ResumeHelp", pausePanel.transform, "Продолжить", 490, 370, 179, 45, 23).alignment = TextAnchor.MiddleCenter;
            Label("SoundHelp", pausePanel.transform, "Звук", 710, 370, 179, 45, 23).alignment = TextAnchor.MiddleCenter;
            Label("ExitHelp", pausePanel.transform, "К фасаду", 930, 370, 179, 45, 23).alignment = TextAnchor.MiddleCenter;
            BuildAudioControls();
            pausePanel.SetActive(false);
        }
    }
}
