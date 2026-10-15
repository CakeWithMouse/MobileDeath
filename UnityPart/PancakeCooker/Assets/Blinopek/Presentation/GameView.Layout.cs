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
        private Text dayText, embersText, scraperText, resultDay, profitText, orderPrice;
        private GameObject orderDetails, tutorialPanel, orderHud, mainMenu, counterPatience, orderPayment;
        private Image kitchenPatienceBar;
        private RawImage sideArtA, sideArtB;
        private RawImage panGuide;
        private GameObject sideReadyA, sideReadyB, batterSelection;
        private readonly List<GameObject> ingredientSelections = new List<GameObject>();
        private readonly List<GameObject> helperBadges = new List<GameObject>();
        private readonly List<Text> resultRows = new List<Text>();
        private readonly List<GameObject> shopPages = new List<GameObject>();
        private readonly List<GameObject> recipePages = new List<GameObject>();
        private bool atMainMenu;
        private Button mainMenuShop;
        private Text mainMenuPlayLabel;
        private bool CanOpenShop => atMainMenu && !controller.StorageBlocked && (Session == null || Session.Phase == DayPhase.Result);
        private readonly List<RawImage> cafeTables = new List<RawImage>();

        private RawImage Art(string name, Transform parent, string key, float x, float y, float w, float h, bool preserve = true)
            => Picture(name, parent, art.Find(key), x, y, w, h, preserve);
        private GameObject CookingSelection(string name, Transform parent, float x, float y, float w, float h)
        {
            var selection = Rect(name, parent, x, y, w, h);
            var badge = Paper("SelectionBadge", selection, w - 25, -15, 38, 38);
            Art("SelectionCheck", badge.transform, "ui/glyphs/check.png", 8, 8, 22, 22);
            selection.gameObject.SetActive(false);
            return selection.gameObject;
        }
        private RawImage Paper(string name, Transform parent, float x, float y, float w, float h)
            => FramedArt(name, parent, "ui/panel-cream.png", x, y, w, h, Mathf.Min(32, h * .2f), .2f);
        private ButtonPlateGraphic FramedArt(string name, Transform parent, string key, float x, float y, float w, float h, float corner = 0, float border = .35f)
        {
            var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<ButtonPlateGraphic>();
            image.texture = art.Find(key); image.uvRect = art.Uv(image.texture);
            image.CornerSize = corner; image.BorderFraction = border; image.raycastTarget = false;
            return image;
        }
        private Image PatienceTrack(string name, string fillName, Transform parent, float x, float y, float w, float h)
        {
            var track = FramedArt(name, parent, "ui/patience-track.png", x, y, w, h);
            // Insets follow the painted aperture at the same uniform scale as the end caps.
            var fill = Rect(fillName, track.transform, h * .3f, h * .29f, w - h * .6f, h * .43f)
                .gameObject.AddComponent<PatienceFillGraphic>();
            fill.color = Green; fill.raycastTarget = false; return fill;
        }
        private Text PaperLabel(string name, Transform parent, float x, float y, float w, float h, int size)
        {
            var paper = Paper(name + "Paper", parent, x, y, w, h);
            var label = Label(name, paper.transform, "", 12, 6, w - 24, h - 12, size);
            label.alignment = TextAnchor.MiddleCenter;
            return label;
        }
        private Button IconButton(string name, Transform parent, string key, float x, float y, float w, float h, Action action, bool background = true, bool preserveFrame = false)
        {
            var b = Button(name, parent, "", x, y, w, h, action);
            if (preserveFrame) ((ButtonPlateGraphic)b.targetGraphic).CornerSize = 24;
            if (!background) b.targetGraphic.color = Color.clear;
            float insetY = preserveFrame ? 20 : 12;
            Art("Icon", b.transform, key, background ? 20 : 0, background ? insetY : 0, background ? w - 40 : w, background ? h - insetY * 2 : h);
            return b;
        }
        private void Shadow(Transform parent, string key, float x, float y, float w, float h)
            => Art("Shadow", parent, "kitchen/shadows/" + key + ".png", x - .08f * w, y + .77f * h, w * 1.2f, h * .3f, false);
        private void ServiceBackground(Transform parent)
        {
            Picture("Room", parent, art.counterBackground, 0, 0, 1600, 900, false);
            Art("Chair", parent, "decor/chair_premium.png", 1025, 414, 160, 280);
            CafeTable("Table", parent, 846, 500, 320);
            Art("Stool", parent, "decor/stool_base.png", 1350, 580, 150, 180);
            CafeTable("SideTable", parent, 1270, 440, 270);
            Art("Flowers", parent, "decor/vase_flowers.png", 1000, 391, 100, 130);
            Art("Lamp", parent, "decor/lamp_brass.png", 1140, -38, 165, 240);
        }
        private void CafeTable(string name, Transform parent, float x, float y, float width)
        {
            var texture = art.Find("decor/table_base.png"); var uv = art.Uv(texture);
            float height = texture == null ? width : width * texture.height * uv.height / (texture.width * uv.width);
            cafeTables.Add(Picture(name, parent, texture, x, y, width, height, false));
        }
        private void RefreshCafeTables()
        {
            var texture = art.Find(controller.Profile.darkTablesUpgrade ? "decor/table_premium.png" : "decor/table_base.png");
            if (texture == null) return;
            var uv = art.Uv(texture);
            foreach (var table in cafeTables)
            {
                if (table.texture == texture) continue;
                var rect = table.rectTransform; float height = rect.sizeDelta.x * texture.height * uv.height / (texture.width * uv.width);
                // Keep the tabletop's upper-left position fixed when changing sprite proportions.
                rect.anchoredPosition += new Vector2(0, (rect.sizeDelta.y - height) * .5f);
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
                table.texture = texture; table.uvRect = uv;
            }
        }
        private GameObject Modal(string name)
        {
            var group = Group(name);
            Panel("Dim", group.transform, 0, 0, 1600, 900, new Color(.14f, .18f, .14f, .55f));
            return group;
        }
        private void OpenBook() { book.SetActive(true); controller.SetPause(PauseReason.RecipeBook, true); Audio.PlayUi(SoundCue.BookOpen); }
        private void CloseBook() { book.SetActive(false); controller.SetPause(PauseReason.RecipeBook, false); Audio.PlayUi(SoundCue.BookClose); }
        private void OpenShop()
        {
            if (!CanOpenShop) return;
            shop.transform.SetAsLastSibling(); shop.SetActive(true); dirty = true;
        }
        private void ShowMainMenu()
        {
            CloseCafeNameDialog();
            controller.SetPause(PauseReason.Menu, true);
            controller.SetPause(PauseReason.RecipeBook, false);
            ReleasePointer(); HidePortionGhost(); HideReadyGhost();
            book.SetActive(false); orderDetails.SetActive(false); resetConfirmation.SetActive(false);
            atMainMenu = true; settingsAtMainMenu = false; mainMenu.SetActive(true); shop.SetActive(false); pausePanel.SetActive(false);
            Audio.PlayUi(SoundCue.Back); dirty = true;
        }
        private void PlayFromMainMenu()
        {
            if (cafeNameDialog.activeSelf) return;
            if (controller.StorageBlocked) return;
            controller.SetPause(PauseReason.Menu, false);
            if (Session == null || Session.Phase == DayPhase.Result) StartNextDay();
            if (Session == null || Session.Phase == DayPhase.Result) { controller.SetPause(PauseReason.Menu, true); return; }
            atMainMenu = false; mainMenu.SetActive(false); shop.SetActive(false);
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
            BuildHud(); BuildResults(); BuildShop(); BuildBook(); BuildMainMenu(); BuildPause();
            BuildCafeNameDialog();
            storage = Label("StorageError", root, "", 270, 850, 1060, 38, 20, new Color(.65f, .14f, .1f));
            ghost = Picture("DraggedPortion", root, art.fillings[3], 0, 0, 70, 60, false); ghost.gameObject.SetActive(false);
            draggedReady = Display(root, "DraggedReadyPancake", 0, 0, 148, 126);
            draggedReady.Root.SetActive(false); displays.Add(draggedReady);
        }
        private void BuildHud()
        {
            Paper("TimePanel", root, 22, 18, 340, 78);
            Art("Day", root, "ui/day-sun.png", 35, 28, 50, 48);
            dayText = Label("DayNumber", root, "", 91, 28, 50, 51, 30);
            clock = Rect("WorkdayClock", root, 226, 21, 72, 72).gameObject.AddComponent<WorkdayClockGraphic>();
            clock.Show(null);
            Paper("WalletPanel", root, 1040, 18, 400, 78);
            Art("Coin", root, "ui/coin-pancake.png", 1058, 29, 56, 51);
            hud = Label("Wallet", root, "", 1120, 28, 130, 51, 29);
            Art("Embers", root, "ui/currency-honey.png", 1257, 26, 45, 55);
            embersText = Label("EmberAmount", root, "", 1310, 28, 112, 51, 30);
            IconButton("Pause", root, "ui/glyphs/pause.png", 1462, 18, 112, 78, OpenSettings);
            orderHud = Rect("KitchenOrderHud", root, 402, 18, 591, 78).gameObject;
            var receipt = IconButton("ShowOrder", orderHud.transform, "ui/receipt-order.png", 0, 0, 591, 78, () => orderDetails.SetActive(!orderDetails.activeSelf));
            var receiptPaper = receipt.GetComponent<RawImage>(); receiptPaper.texture = art.Find("ui/panel-cream.png"); receiptPaper.uvRect = art.Uv(receiptPaper.texture);
            var receiptFrame = (ButtonPlateGraphic)receiptPaper; receiptFrame.CornerSize = 15.6f; receiptFrame.BorderFraction = .2f;
            var icon = receipt.transform.Find("Icon").GetComponent<RectTransform>(); icon.anchoredPosition = new Vector2(42, -39); icon.sizeDelta = new Vector2(40, 52);
            progress = Label("Progress", orderHud.transform, "", 87, 10, 116, 56, 30);
            kitchenPatienceBar = PatienceTrack("OrderPatienceTrack", "OrderPatienceFill", orderHud.transform, 222, 20, 335, 37);
            orderDetails = Rect("OrderDetails", root, 402, 110, 591, 180).gameObject;
            Paper("Paper", orderDetails.transform, 0, 0, 591, 180).raycastTarget = true;
            orderStrip = Label("Order", orderDetails.transform, "", 30, 20, 531, 140, 25);
            orderDetails.SetActive(false);
        }
        private void BuildCounter()
        {
            ServiceBackground(counter.transform);
            visitor = Picture("Visitor", counter.transform, art.visitors[0], 94, 191, 470, 792);
            visitorHome = visitor.rectTransform.anchoredPosition;
            Picture("ServiceCounter", counter.transform, art.counter, -30, 723, 1660, 240, false);
            deliveryTray = Rect("DeliveryTray", counter.transform, 1020, 735, 420, 140);
            deliveryTrayHome = deliveryTray.anchoredPosition;
            Art("Delivery", deliveryTray, "decor/service_delivery.png", 0, 0, 420, 140, false);
            var trayHit = deliveryTray.gameObject.AddComponent<Image>(); trayHit.color = Color.clear;
            deliveryInput = deliveryTray.gameObject.AddComponent<TrayServeDrag>(); deliveryInput.View = this;
            for (int i = 0; i < 3; i++) {
                var portion = Display(deliveryTray, "CounterPancake" + i, 0, 0, 148, 126);
                Picture("PortionPlate", portion.Root.transform, art.plate, -3, 3, 154, 120, false).transform.SetAsFirstSibling();
                deliveryDisplays.Add(portion); displays.Add(portion);
            }
            deliveryCount = PaperLabel("DeliveryCount", deliveryTray, 330, -35, 80, 38, 22);
            var trayArrow = Art("ServeTrayArrow", deliveryTray, "ui/glyphs/arrow-right.png", 186, -36, 48, 38);
            trayArrow.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(TrayServeDirection.y, TrayServeDirection.x) * Mathf.Rad2Deg); deliveryArrow = trayArrow.gameObject;
            deliveryHint = PaperLabel("DeliveryHint", counter.transform, 805, 635, 620, 60, 25);
            deliveryHint.text = "Передвиньте поднос к клиенту";
            Art("CounterPitcher", counter.transform, "decor/pitcher.png", 1444, 658, 112, 183);
            speechBubble = Art("SpeechBubble", counter.transform, "ui/speech-bubble.png", 620, 145, 825, 285).gameObject;
            dialogue = Label("Request", counter.transform, "", 680, 168, 705, 144, 32, Ink, true); dialogue.alignment = TextAnchor.MiddleCenter;
            orderPayment = Rect("OrderPayment", counter.transform, 1238, 316, 145, 42).gameObject;
            orderPrice = Label("OrderPrice", orderPayment.transform, "", 0, 0, 101, 42, 27); orderPrice.alignment = TextAnchor.MiddleRight;
            Art("OrderCoin", orderPayment.transform, "ui/coin-pancake.png", 109, 5, 32, 32);
            reaction = Label("Reaction", counter.transform, "", 680, 168, 705, 180, 24); reaction.alignment = TextAnchor.MiddleCenter;
            clarify = IconButton("ClarifyOrder", counter.transform, "ui/glyphs/help.png", 945, 448, 176, 86, () => Command(ActionKind.Clarify));
            counterPatience = Rect("CounterPatience", counter.transform, 813, 554, 440, 32).gameObject;
            patienceBar = PatienceTrack("PatienceTrack", "PatienceFill", counterPatience.transform, 0, 0, 440, 32);
            IconButton("RecipeBook", counter.transform, "ui/recipe-book-closed.png", 28, 770, 108, 104, OpenBook, false);
            toKitchen = IconButton("KitchenTab", counter.transform, "ui/glyphs/arrow-right.png", 1448, 795, 120, 80, () => SwitchKitchen(true));
        }
        private void BuildKitchen()
        {
            Picture("KitchenRoom", kitchen.transform, art.kitchenBackground, 0, 0, 1600, 900, false);
            var batterSource = Panel("BatterSource", kitchen.transform, 20, 292, 260, 280, Color.clear);
            var batterInput = batterSource.gameObject.AddComponent<IngredientDrag>(); batterInput.View = this; batterInput.Ingredient = -1;
            batterSelection = CookingSelection("SelectedBatter", batterSource.transform, 0, 0, 260, 280);
            var batterLabel = PaperLabel("BatterName", batterSelection.transform, 65, 276, 130, 39, 20);
            batterLabel.text = "Тесто";
            // The approved overhead background includes the empty dishes and batter jug.
            // Food, ingredients and HUD remain independent interactive layers.
            const float panWidth = 364, panHeight = 310, plateWidth = 372, plateHeight = 298;
            var panSurface = Rect("PanSurface", kitchen.transform, 382, 300, panWidth, panHeight);
            var panHit = panSurface.gameObject.AddComponent<Image>(); panHit.color = Color.clear;
            var panRouter = panSurface.gameObject.AddComponent<KitchenInputRouter>(); panRouter.View = this; panRouter.Pan = true;
            panGuide = Art("BatterGuide", panSurface, "kitchen/guide-batter-circle.png", 0, 0, panWidth, panHeight, false); panGuide.color = new Color(1, .94f, .75f, .65f);
            panDisplay = Display(panSurface, "PancakeOnPan", 0, 0, panWidth, panHeight); displays.Add(panDisplay);
            plateSurface = Rect("PlateSurface", kitchen.transform, 1144, 404, plateWidth, plateHeight);
            var plateHit = plateSurface.gameObject.AddComponent<Image>(); plateHit.color = Color.clear;
            plateSurface.gameObject.AddComponent<KitchenInputRouter>().View = this;
            assemblyDisplay = Display(plateSurface, "PancakeOnPlate", 0, 0, plateWidth, plateHeight); displays.Add(assemblyDisplay);
            // The tray is painted into the background; its hit area sits behind the draggable portions.
            var trayHit = Panel("KitchenTray", kitchen.transform, 1155, 168, 377, 162, Color.clear);
            kitchenTray = trayHit.gameObject.AddComponent<Button>(); kitchenTray.targetGraphic = trayHit;
            kitchenTray.transition = Selectable.Transition.None;
            kitchenTray.onClick.AddListener(BeginServing);
            // Keep full-sized plates and page through larger prepared stocks.
            for (int i = 0; i < 2; i++)
            {
                var portion = Display(kitchen.transform, "ReadyPancake" + i, 1190 + i * 165, 190, 148, 126);
                Picture("PortionPlate", portion.Root.transform, art.plate, -3, 3, 154, 120, false).transform.SetAsFirstSibling();
                var selection = Art("SelectedReady", portion.Root.transform, "kitchen/guide-batter-circle.png", -6, 0, 160, 126, false);
                selection.color = new Color(.39f, .55f, .42f, .85f); portion.Selection = selection.gameObject;
                var hit = portion.Root.AddComponent<Image>(); hit.color = Color.clear;
                portion.Input = portion.Root.AddComponent<ReadyPancakeDrag>(); portion.Input.View = this;
                readyDisplays.Add(portion); displays.Add(portion);
            }
            trayCount = PaperLabel("TrayCount", kitchen.transform, 1180, 119, 335, 42, 22);
            trayPrevious = IconButton("TrayPrevious", kitchen.transform, "ui/glyphs/arrow-left.png", 1127, 116, 50, 48, () => ChangeTrayPage(-1), false);
            trayNext = IconButton("TrayNext", kitchen.transform, "ui/glyphs/arrow-right.png", 1518, 116, 50, 48, () => ChangeTrayPage(1), false);
            for (int i = 0; i < Recipes.PortionTargets.Length; i++)
            {
                var p = Recipes.PortionTargets[i]; var marker = Label("PortionGuide", plateSurface, "+", p.X * plateWidth / 1000 - 16, (1000 - p.Y) * plateHeight / 1000 - 24, 32, 48, 29, Green);
                // PT Sans has a taller line box than the glyph; truncating it hid the whole stencil.
                marker.verticalOverflow = VerticalWrapMode.Overflow;
                marker.alignment = TextAnchor.MiddleCenter; portionGuides.Add(marker);
            }
            var positions = new[] { new Rect(1010, 604, 108, 101), new Rect(844, 250, 149, 142), new Rect(1010, 267, 112, 133), new Rect(869, 422, 141, 133), new Rect(879, 584, 116, 104) };
            string[] names = { "butter", "jam", "honey", "curd", "mushrooms" };
            for (int i = 0; i < 5; i++)
            {
                var p = positions[i]; var card = Panel("Ingredient" + i, kitchen.transform, p.x, p.y, p.width, p.height, Color.clear); ingredientCards.Add(card);
                Shadow(card.transform, names[i], 0, 0, p.width, p.height);
                Picture("Container", card.transform, art.containers[i], 0, 0, p.width, p.height);
                ingredientSelections.Add(CookingSelection("SelectedIngredient", card.transform, 0, 0, p.width, p.height));
                var badge = Art("HelperBadge", card.transform, "ui/helper-badge.png", p.width - 19, p.height - 15, 27, 27); helperBadges.Add(badge.gameObject);
                var label = PaperLabel("Name", card.transform, 0, p.height - 20, p.width, 39, 18);
                label.text = Recipes.IngredientName((Ingredient)i); ingredientLabels.Add(label);
                var drag = card.gameObject.AddComponent<IngredientDrag>(); drag.View = this; drag.Ingredient = i;
            }
            Paper("HeatStatus", kitchen.transform, 410, 112, 330, 62);
            heatA = Label("SideA", kitchen.transform, "1", 430, 119, 24, 35, 22);
            heatB = Label("SideB", kitchen.transform, "2", 592, 119, 24, 35, 22);
            sideArtA = Picture("SideAImage", kitchen.transform, art.raw, 463, 126, 49, 34);
            sideArtB = Picture("SideBImage", kitchen.transform, art.raw, 625, 126, 49, 34);
            sideReadyA = Art("SideAReady", kitchen.transform, "ui/glyphs/check.png", 520, 132, 23, 23).gameObject;
            sideReadyB = Art("SideBReady", kitchen.transform, "ui/glyphs/check.png", 682, 132, 23, 23).gameObject;
            heatBarA = Panel("HeatA", kitchen.transform, 430, 159, 120, 4, Gold); heatBarA.raycastTarget = false;
            heatBarB = Panel("HeatB", kitchen.transform, 592, 159, 120, 4, Gold); heatBarB.raycastTarget = false;
            finishBatter = Button("FinishBatter", kitchen.transform, "Начать жарку", 423, 662, 282, 58, () => Command(ActionKind.FinishBatter));
            flip = Button("FlipHandle", kitchen.transform, "Перевернуть", 423, 662, 282, 58, () => Command(ActionKind.Flip));
            move = Button("MoveToPlate", kitchen.transform, "На тарелку →", 423, 662, 282, 58, () => Command(ActionKind.MoveToPlate));
            for (int i = 0; i < 3; i++)
            {
                int shape = i; string[] forms = { "triangle", "envelope", "roll" };
                var b = IconButton("Fold" + i, kitchen.transform, "kitchen/fold-" + forms[i] + ".png", 1135 + i * 145, 746, 135, 54, () => Command(ActionKind.BeginFold, shape)); foldButtons.Add(b);
            }
            foldGuide = PaperLabel("FoldGuide", kitchen.transform, 1130, 704, 430, 38, 20);
            finishFold = Button("FinishIncorrectFold", kitchen.transform, "Закончить как есть", 1170, 746, 365, 54, () => Command(ActionKind.FinalizeFold));
            IconButton("CounterTab", kitchen.transform, "ui/glyphs/arrow-left.png", 24, 804, 110, 76, () => SwitchKitchen(false));
            IconButton("KitchenRecipeBook", kitchen.transform, "ui/recipe-book-closed.png", 159, 797, 87, 90, OpenBook, false);
            remove = IconButton("Scraper", kitchen.transform, "kitchen/scraper.png", 1271, 805, 203, 75, () => Command(ActionKind.Remove, SelectedIngredient));
            var scraperIcon = remove.transform.Find("Icon").GetComponent<RectTransform>(); scraperIcon.anchoredPosition = new Vector2(48, -37); scraperIcon.sizeDelta = new Vector2(69, 61);
            scraperText = Label("ScraperCount", remove.transform, "", 110, 6, 82, 63, 30);
            trashZone = Rect("DiscardZone", kitchen.transform, 1495, 805, 87, 75);
            discard = IconButton("Discard", trashZone, "kitchen/discard-bowl.png", 0, 0, 87, 75, DiscardSelectedPancake);
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
            IconButton("ReturnToMainMenu", results.transform, "ui/glyphs/home.png", 1320, 707, 142, 95, ShowMainMenu);
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
                    FramedArt("Card", page.transform, "ui/card-frame.png", x, 265, 252, 418, 28, .12f);
                    Picture("IngredientArt", page.transform, art.containers[i], x + 36, 320, 180, 160);
                    if (helpers) Art("Helper", page.transform, "ui/helper-badge.png", x + 154, 431, 55, 55);
                    var title = Label("Name", page.transform, Recipes.IngredientName((Ingredient)i), x + 18, 285, 216, 38, 24); title.alignment = TextAnchor.MiddleCenter;
                    var price = Label("Price", page.transform, "", x + 25, 493, 202, 48, 26); price.alignment = TextAnchor.MiddleCenter;
                    var buy = Button((helpers ? "Helper" : "BuyIngredient") + i, page.transform, "", x + 25, 559, 202, 65,
                        () => Buy(helpers ? ((controller.Profile.helpersOwned & (1 << type)) != 0 ? ShopItem.ToggleHelper : ShopItem.Helper) : ShopItem.Ingredient, type));
                    shopRefreshers.Add(() => {
                        bool owned = controller.Profile.Owns((Ingredient)type), hasHelper = (controller.Profile.helpersOwned & (1 << type)) != 0;
                        price.text = helpers ? (hasHelper ? "Куплен" : Embers(art.balance.helperHoneyPrice)) : (owned ? "Открыто" : Money(art.balance.ingredientUnlockCost));
                        buy.GetComponentInChildren<Text>().text = helpers ? (hasHelper ? (controller.Profile.HelperEnabled((Ingredient)type) ? "Выключить" : "Включить") : "Купить") : (owned ? "✓" : "Купить");
                        buy.interactable = Session != null && Session.Success && (helpers ? owned && (hasHelper || controller.Profile.honey >= art.balance.helperHoneyPrice) : !owned && controller.Profile.coins >= art.balance.ingredientUnlockCost);
                    });
                }
                int other = group % 2 == 0 ? group + 1 : group - 1;
                IconButton("ShopPageArrow", page.transform, "ui/glyphs/" + (group % 2 == 0 ? "arrow-right" : "arrow-left") + ".png", group % 2 == 0 ? 1275 : 203, 702, 120, 65, () => SelectShopPage(other));
            }
            var upgrades = Rect("ShopUpgrades", shop.transform, 0, 0, 1600, 900).gameObject; shopPages.Add(upgrades);
            for (int i = 0; i < 3; i++)
            {
                bool scraper = i == 0, tables = i == 2; int x = 310 + i * 340;
                var item = scraper ? ShopItem.Scraper : tables ? ShopItem.DarkTables : ShopItem.Patience;
                int cost = scraper ? art.balance.scraperPrice : tables ? art.balance.darkTableUpgradeCost : art.balance.patienceUpgradeCost;
                FramedArt("Card", upgrades.transform, "ui/card-frame.png", x, 265, 252, 418, 28, .12f);
                Art("Upgrade", upgrades.transform, scraper ? "kitchen/scraper.png" : tables ? "decor/table_premium.png" : "ui/upgrade-patience.png", x + 36, 320, 180, 160);
                var title = Label("Title", upgrades.transform, scraper ? "Скребок" : tables ? "Тёмные столы" : "Терпение", x + 18, 285, 216, 38, 24); title.alignment = TextAnchor.MiddleCenter;
                var price = Label("Price", upgrades.transform, Money(cost), x + 25, 493, 202, 48, 26); price.alignment = TextAnchor.MiddleCenter;
                var buy = Button("Buy" + item, upgrades.transform, "Купить", x + 25, 559, 202, 65, () => Buy(item));
                if (tables) {
                    float before = art.balance.GetGuestArrivalIntervalTicks(0) * .05f;
                    float after = art.balance.GetGuestArrivalIntervalTicks(art.balance.darkTablesDecorationPoints) * .05f;
                    var effect = Label("TableGuestInterval", upgrades.transform, "Между гостями: " + before.ToString("0.##") + " → " + after.ToString("0.##") + " с", x + 18, 632, 216, 32, 16);
                    effect.alignment = TextAnchor.MiddleCenter;
                }
                shopRefreshers.Add(() => {
                    bool owned = !scraper && (tables ? controller.Profile.darkTablesUpgrade : controller.Profile.patienceUpgrade);
                    buy.interactable = Session != null && Session.Success && !owned && controller.Profile.coins >= cost;
                    buy.GetComponentInChildren<Text>().text = owned ? "Куплено" : "Купить";
                });
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
            book = Modal("RecipeBookPanel");
            var paper = Art("OpenBook", book.transform, "ui/recipe-book-open.png", 142, 100, 1320, 716).rectTransform;
            var size = paper.rect.size;
            string[] shapes = { "triangle", "envelope", "roll" };
            string[] names = { "Сладкий\nтреугольник", "Грибной\nконверт", "Сладкая\nтрубочка" };
            for (int page = 0; page < 2; page++)
            {
                var group = Rect("RecipePage" + page, paper, 0, 0, size.x, size.y); recipePages.Add(group.gameObject);
                group.anchorMin = Vector2.zero; group.anchorMax = Vector2.one;
                group.anchoredPosition = Vector2.zero; group.sizeDelta = Vector2.zero;
                for (int side = 0; side < 2; side++)
                {
                    int shape = page * 2 + side;
                    // The painted pages are inset from the cover. Use their centers in the fitted
                    // artwork, so the whole recipe follows the book instead of screen coordinates.
                    float center = side == 0 ? .28f : .72f, width = size.x * .36f;
                    var leaf = Rect("RecipeLeaf" + shape, group, 0, 0, width, size.y);
                    leaf.anchorMin = leaf.anchorMax = new Vector2(center, .5f);
                    leaf.anchoredPosition = Vector2.zero;
                    if (shape > 2)
                    { Label("CookingHelp", leaf, "Намажьте тесто на круг.\nНачните жарку.\nПереверните готовую сторону.\nПеренесите блин на тарелку.\nДобавьте начинку из заказа.\nВыберите форму и сложите.", 0, size.y * .2f, width, 330, 28); continue; }
                    var title = Label("RecipeName", leaf, names[shape], 0, size.y * .07f, width, 104, 34, Ink, true); title.alignment = TextAnchor.MiddleCenter;
                    Art("Fold", leaf, "kitchen/fold-" + shapes[shape] + ".png", (width - 363) * .5f, size.y * .233f, 363, 230);
                    var gesture = Label("Gestures", leaf, shape == 0 ? "↑   →" : shape == 1 ? "→   ←   ↓   ↑" : "↓   ↓   ↓", 0, size.y * .573f, width, 55, 34); gesture.alignment = TextAnchor.MiddleCenter;
                    Picture("Filling1", leaf, art.containers[shape == 1 ? 4 : 0], width * .5f - 175, size.y * .665f, 130, 118);
                    Art("Plus", leaf, "ui/glyphs/plus.png", (width - 45) * .5f, size.y * .665f + (118 - 45) * .5f, 45, 45);
                    Picture("Filling2", leaf, art.containers[shape == 1 ? 3 : 1], width * .5f + 45, size.y * .665f, 130, 118);
                }
            }
            IconButton("PreviousRecipe", paper, "ui/glyphs/arrow-left.png", 28, size.y - 90, 120, 70, () => ShowRecipePage(0));
            IconButton("NextRecipe", paper, "ui/glyphs/arrow-right.png", size.x - 148, size.y - 90, 120, 70, () => ShowRecipePage(1));
            IconButton("CloseBook", book.transform, "ui/glyphs/close.png", 1470, 29, 102, 78, CloseBook);
            ShowRecipePage(0); book.SetActive(false);
        }
        private void BuildMainMenu()
        {
            mainMenu = Group("MainMenu"); Art("MainMenuArt", mainMenu.transform, "decor/facade_base.png", 0, 0, 1600, 900, false);
            BuildSignboard();
            var play = Button("ContinueShift", mainMenu.transform, "", 610, 744, 380, 100, PlayFromMainMenu);
            mainMenuPlayLabel = play.GetComponentInChildren<Text>();
            mainMenuPlayLabel.fontSize = 30;
            mainMenuShop = IconButton("MainMenuShop", mainMenu.transform, "ui/glyphs/shop.png", 1028, 744, 120, 100, OpenShop);
            IconButton("MainMenuSettings", mainMenu.transform, "ui/glyphs/pause.png", 452, 744, 120, 100, OpenSettings);
            mainMenu.SetActive(false);
        }
        private void BuildPause()
        {
            pausePanel = Modal("PauseMenu"); Paper("Menu", pausePanel.transform, 420, 130, 760, 705);
            var title = Label("Title", pausePanel.transform, "Настройки", 520, 160, 560, 65, 40, Ink, true); title.alignment = TextAnchor.MiddleCenter;
            IconButton("Resume", pausePanel.transform, "ui/glyphs/play.png", 523, 250, 114, 114, CloseSettings, preserveFrame: true);
            var sound = IconButton("Sound", pausePanel.transform, "ui/settings-speaker.png", 743, 250, 114, 114, () => { Audio.SetMuted(!Audio.Muted); Audio.PlayUi(SoundCue.Toggle); }, preserveFrame: true);
            soundIcon = sound.transform.Find("Icon").GetComponent<RawImage>();
            IconButton("ExitToMainMenu", pausePanel.transform, "ui/glyphs/home.png", 963, 250, 114, 114, ShowMainMenu, preserveFrame: true);
            Label("ResumeHelp", pausePanel.transform, "Продолжить", 490, 370, 179, 45, 23).alignment = TextAnchor.MiddleCenter;
            Label("SoundHelp", pausePanel.transform, "Звук", 710, 370, 179, 45, 23).alignment = TextAnchor.MiddleCenter;
            Label("ExitHelp", pausePanel.transform, "Главное меню", 930, 370, 179, 45, 23).alignment = TextAnchor.MiddleCenter;
            restartDay = Button("RestartDay", pausePanel.transform, "Начать день заново", 585, 420, 430, 55, RestartCurrentDay, preserveCaps: true);
            BuildAudioControls();
            BuildResetControls();
            pausePanel.SetActive(false);
        }
    }
}
