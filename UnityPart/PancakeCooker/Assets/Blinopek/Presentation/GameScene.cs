using System;
using System.Collections.Generic;
using Blinopek.Application;
using Blinopek.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    // Владеет интерфейсом и визуальными ресурсами. Игровые действия передаёт в GameSystem.
    public sealed partial class GameScene : IDisposable
    {
        // 1) Владеет объектом.
        // Создаёт элементы интерфейса под GameObject системы; Unity уничтожает их с родителем.
        // Создаёт CookingAtlas и освобождает его в Dispose().
        private Canvas canvas;
        private RectTransform root, plateSurface, trashZone;
        private Text hud, orderStrip, hint, dialogue, reaction, resultText, shopBalance, progress, foldGuide, storage;
        private WorkdayClockGraphic clock;
        private Text heatA, heatB, trayCount;
        private Image patienceBar, heatBarA, heatBarB;
        private RawImage visitor, ghost;
        private Button clarify, finishBatter, flip, move, remove, discard, finishFold, toKitchen;
        private PancakeDisplay draggedReady;
        private PancakeDisplay panDisplay, assemblyDisplay;
        private RectTransform deliveryTray;
        private TrayServeDrag deliveryInput;
        private Text deliveryCount, deliveryHint;
        private GameObject deliveryArrow;
        private Button kitchenTray, trayPrevious, trayNext;
        private GameObject kitchen, counter, results, shop, pausePanel, book;
        private GameObject speechBubble;
        private CookingAtlas cookingAtlas;

        // 2) Общается через API.
        // Внешние зависимости: команды идут в GameSystem, данные доступны через контракт IGameState без команд.
        // Ресурс art, родительский Transform и игровая сессия не принадлежат GameScene.
        private readonly GameSystem system;
        private readonly GameContent art;
        private readonly Transform parent;
        private readonly IGameState state;
        private DaySession Session => state.Session;
        private GameAudio Audio => system.Audio;

        // 3) Внутренние поля и объекты класса.
        // Выбор, позиции, состояние анимаций, оформление и рабочие коллекции.
        private Vector2 deliveryTrayHome, visitorHome;
        private int selectedReadyId, trayPage;
        private readonly Dictionary<PancakeState, CookingMotion> motions = new Dictionary<PancakeState, CookingMotion>();
        private bool showKitchen => system.InKitchen;
        private bool atMainMenu => system.AtMainMenu;
        private bool audioUiReady;

        private static readonly Color Ink = new Color(60 / 255f, 73 / 255f, 62 / 255f);
        private static readonly Color Cream = new Color(.98f, .94f, .83f, .97f);
        private static readonly Color Green = new Color(87 / 255f, 111 / 255f, 89 / 255f);
        private static readonly Color Gold = new Color(.75f, .44f, .18f);
        private readonly List<Button> foldButtons = new List<Button>();
        private readonly List<Image> ingredientCards = new List<Image>();
        private readonly List<Text> ingredientLabels = new List<Text>();
        private readonly List<Text> portionGuides = new List<Text>();
        private readonly List<Action> shopRefreshers = new List<Action>();
        private readonly List<PancakeDisplay> displays = new List<PancakeDisplay>();
        private readonly List<PancakeDisplay> readyDisplays = new List<PancakeDisplay>();
        // Буфер ссылок на блины из DaySession; сами игровые блины принадлежат сессии.
        private readonly List<PancakeState> trayPancakes = new List<PancakeState>();
        private readonly List<PancakeDisplay> deliveryDisplays = new List<PancakeDisplay>();

        internal bool IsEditingCafeName => cafeNameDialog.activeSelf;
        public bool CanBeginServing => showKitchen && kitchen.activeInHierarchy;
        public bool CanServeTray => system.CanPrepareServing && !showKitchen && counter.activeInHierarchy;
        public Vector2 TrayServeDirection => (visitorHome - deliveryTrayHome).normalized;
        public bool IsFinishingPancake => FinishingPancake() != null;

        public GameScene(GameSystem system, IGameState state, GameContent content)
        {
            this.system = system;
            this.state = state;
            art = content;
            parent = system.transform;
        }

        internal void Initialize()
        {
            cookingAtlas = new CookingAtlas(art);
            Build();
            audioUiReady = true;
        }

        internal void ObserveCooking()
        {
            if (Session?.PanPancake != null) Motion(Session.PanPancake);
            if (Session?.PlatePancake != null) Motion(Session.PlatePancake);
        }

        internal void FitSafeArea()
        {
            var safe = Screen.safeArea;
            float scale = canvas.scaleFactor;
            root.localScale = Vector3.one * Mathf.Min(safe.width / scale / 1600, safe.height / scale / 900);
            root.anchoredPosition = (safe.center - new Vector2(Screen.width, Screen.height) * .5f) / scale;
        }

        internal void AdvanceAnimations(float seconds)
        {
            if (state.Paused) return;
            foreach (var motion in motions.Values)
            {
                bool busy = motion.Busy;
                motion.Advance(seconds);
                if (busy && !motion.Busy) system.Invalidate();
            }
        }

        internal void DrawAnimatedPancakes()
        {
            foreach (var display in displays)
                if (display.Root.activeInHierarchy && display.Base.Motion != null)
                {
                    display.Base.Show(display.Base.Motion);
                    display.Effects.Show(display.Base.Motion, Session.Rules);
                }
        }

        internal void ClearReadySelection()
        {
            selectedReadyId = 0;
            system.Invalidate();
        }

        internal void ResetDayPresentation()
        {
            HidePortionGhost();
            HideReadyGhost();
            motions.Clear();
            trayPancakes.Clear();
            selectedReadyId = trayPage = 0;
            book.SetActive(false);
            orderDetails.SetActive(false);
            resetConfirmation.SetActive(false);
            shop.SetActive(false);
            pausePanel.SetActive(false);
            system.Invalidate();
        }

        public void Dispose()
        {
            cookingAtlas?.Dispose();
            cookingAtlas = null;
        }

        private sealed class PancakeDisplay
        {
            // 2) Общается через API: хранит ссылки на элементы, созданные и управляемые GameScene.
            public GameObject Root;
            public PancakeGraphic Base;
            public CookingEffectsGraphic Effects;
            public ReadyPancakeDrag Input;
            public GameObject Selection;
        }

        private RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform)); var r = go.GetComponent<RectTransform>(); r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = new Vector2(x + w / 2, -y - h / 2); r.sizeDelta = new Vector2(w, h); return r;
        }
        private Image Panel(string name, Transform parent, float x, float y, float w, float h, Color color)
        { var r = Rect(name, parent, x, y, w, h); var image = r.gameObject.AddComponent<Image>(); image.color = color; return image; }
        private Text Label(string name, Transform parent, string value, float x, float y, float w, float h, int size = 26, Color? color = null, bool title = false)
        {
            var r = Rect(name, parent, x, y, w, h); var t = r.gameObject.AddComponent<Text>();
            t.font = title ? art.titleFont : art.bodyFont; t.fontSize = size; t.text = value; t.color = color ?? Ink; t.raycastTarget = false;
            t.alignment = TextAnchor.MiddleLeft; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; return t;
        }
        private RawImage Picture(string name, Transform parent, Texture tex, float x, float y, float w, float h, bool preserve = true)
        {
            var r = Rect(name, parent, x, y, w, h); var image = r.gameObject.AddComponent<RawImage>(); image.texture = tex; image.raycastTarget = false;
            image.uvRect = art.Uv(tex);
            if (preserve && tex != null) { float tw = tex.width * image.uvRect.width, th = tex.height * image.uvRect.height; float fit = Mathf.Min(w / tw, h / th); r.sizeDelta = new Vector2(tw * fit, th * fit); }
            return image;
        }
        private Button Button(string name, Transform parent, string value, float x, float y, float w, float h, Action action, bool secondary = false, bool preserveCaps = true)
        {
            var r = Rect(name, parent, x, y, w, h);
            var image = preserveCaps ? (RawImage)r.gameObject.AddComponent<ButtonPlateGraphic>() : r.gameObject.AddComponent<RawImage>();
            image.texture = art.Find("ui/button-cream.png"); image.uvRect = art.Uv(image.texture);
            if (image is ButtonPlateGraphic plate && w < h * 2) plate.CornerSize = Mathf.Min(24, h * .35f);
            image.raycastTarget = true; var b = image.gameObject.AddComponent<Button>();
            b.targetGraphic = image; var colors = b.colors; colors.highlightedColor = new Color(1.05f, 1.05f, 1.05f); colors.pressedColor = new Color(.82f, .87f, .78f); colors.disabledColor = new Color(.75f, .75f, .72f, .65f); b.colors = colors;
            var label = Label("Label", image.transform, value, 12, 2, w - 24, h - 4, 24, Ink); label.alignment = TextAnchor.MiddleCenter;
            b.onClick.AddListener(() => {
                if (system.HasCapturedPointer) return;
                int before = Audio.FeedbackVersion; action();
                if (audioUiReady && before == Audio.FeedbackVersion) Audio.PlayUi(SoundCue.Button);
                system.Invalidate();
            }); return b;
        }
        private GameObject Group(string name) => Rect(name, root, 0, 0, 1600, 900).gameObject;
        private PancakeDisplay Display(Transform parent, string name, float x, float y, float w, float h)
        {
            var r = Rect(name, parent, x, y, w, h);
            var display = new PancakeDisplay { Root = r.gameObject };
            var surface = Rect("Surface", r, 0, 0, w, h);
            display.Base = surface.gameObject.AddComponent<PancakeGraphic>(); display.Base.Initialize(cookingAtlas, art.balance);
            display.Effects = Rect("CookingEffects", r, 0, 0, w, h).gameObject.AddComponent<CookingEffectsGraphic>();
            return display;
        }
        private CookingMotion Motion(PancakeState p)
        {
            if (!motions.TryGetValue(p, out var motion)) { motion = new CookingMotion(p); motions.Add(p, motion); }
            motion.Observe(); return motion;
        }
        private PancakeState FinishingPancake()
        {
            if (Session == null || Session.PlatePancake != null || Session.LastFinished == null) return null;
            var last = Session.LastFinished;
            if (Session.FindTrayPancake(last.Id) == null) return null;
            return motions.TryGetValue(last, out var motion) && motion.Folding ? last : null;
        }
        private void ShowDisplay(PancakeDisplay d, PancakeState p)
        {
            d.Root.SetActive(p != null); if (p == null) return;
            var motion = Motion(p); d.Base.Show(motion, Session.Rules); d.Effects.Show(motion, Session.Rules);
        }
        public void SelectReadyPancake(int id)
        {
            if (Session.FindTrayPancake(id) == null) return;
            selectedReadyId = id; system.Invalidate();
        }
        public void MoveReadyGhost(PancakeState pancake, Vector2 position)
        {
            ShowDisplay(draggedReady, pancake);
            draggedReady.Root.transform.position = position;
        }
        public void HideReadyGhost() { if (draggedReady != null) draggedReady.Root.SetActive(false); }
        private void DiscardSelectedPancake()
        {
            int id = Session.FindTrayPancake(selectedReadyId)?.Id ?? Session.Pancake?.Id ?? 0;
            if (id != 0 && system.Command(ActionKind.Discard, id)) selectedReadyId = 0;
        }
        private void ChangeTrayPage(int direction)
        { trayPage += direction; selectedReadyId = 0; system.Invalidate(); }
        public bool OverTrash(Vector2 p) => RectTransformUtility.RectangleContainsScreenPoint(trashZone, p);
        public bool OverPlate(Vector2 p) => RectTransformUtility.RectangleContainsScreenPoint(plateSurface, p);
        public Vector2 PlateCoordinates(Vector2 p)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(plateSurface, p, null, out var local);
            return new Vector2((local.x / plateSurface.rect.width + .5f) * 1000, (local.y / plateSurface.rect.height + .5f) * 1000);
        }
        public void MovePortionGhost(int type, Vector2 position)
        {
            if (!Session.StartProfile.Owns((Ingredient)type)) return;
            ghost.gameObject.SetActive(true); ghost.texture = art.fillings[type]; ghost.uvRect = art.Uv(ghost.texture); ghost.rectTransform.position = position;
        }
        public void HidePortionGhost() { if (ghost != null) ghost.gameObject.SetActive(false); }
        internal void ClearKitchenInteraction()
        {
            HidePortionGhost(); HideReadyGhost(); selectedReadyId = 0; orderDetails.SetActive(false);
        }
        public static string Money(int cents) => (cents / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        private static string Embers(int amount)
        {
            int lastTwo = amount % 100, last = amount % 10;
            string name = lastTwo >= 11 && lastTwo <= 14 ? "жаринок" : last == 1 ? "жаринка" : last >= 2 && last <= 4 ? "жаринки" : "жаринок";
            return amount + " " + name;
        }
        private static void FillBar(Image image, float fraction)
        {
            if (image is PatienceFillGraphic patience) { patience.Fraction = fraction; return; }
            var rect = image.rectTransform;
            if (rect.pivot.x != 0) { rect.anchoredPosition -= new Vector2(rect.pivot.x * rect.rect.width, 0); rect.pivot = new Vector2(0, rect.pivot.y); }
            rect.localScale = new Vector3(Mathf.Clamp01(fraction), 1, 1);
        }
        public void Render()
        {
            RefreshSignboard();
            RefreshCafeTables();
            RefreshAudioControls();
            bool continuingDay = Session != null && Session.Phase != DayPhase.Result;
            restartDay.gameObject.SetActive(!atMainMenu && continuingDay && !state.StorageBlocked);
            mainMenuPlayLabel.text = continuingDay ? "Продолжить день " + Session.StartProfile.day : "Начать день " + state.Profile.day;
            mainMenuShop.gameObject.SetActive(CanOpenShop);
            if (!CanOpenShop) shop.SetActive(false);
            if (shop.activeSelf) { shopBalance.text = "Златники: " + Money(state.Profile.coins) + " · Жаринки: " + state.Profile.honey; foreach (var update in shopRefreshers) update(); }
            pausePanel.SetActive((state.Pauses & PauseReason.Menu) != 0 && (!atMainMenu || system.SettingsOpen));
            if (state.StorageBlocked || Session == null) {
                kitchen.SetActive(false); counter.SetActive(false); results.SetActive(false); orderHud.SetActive(false); tutorialPanel.SetActive(false);
                dayText.text = state.Profile.day.ToString(); hud.text = Money(state.Profile.coins);
                embersText.text = state.Profile.honey.ToString(); clock.Show(null);
                storage.text = state.StorageError ?? ""; return;
            }
            var s = Session; var o = s.Order; var p = s.PanPancake; var plate = s.PlatePancake;
            PancakeState finishing = showKitchen ? FinishingPancake() : null;
            bool terminal = o.Phase == OrderPhase.Served || o.Phase == OrderPhase.Cancelled;
            bool showingResult = s.Phase == DayPhase.Result && finishing == null;
            kitchen.SetActive(showKitchen && !showingResult && !atMainMenu); counter.SetActive(!showKitchen && !showingResult && !atMainMenu);
            results.SetActive(showingResult && !atMainMenu);
            orderHud.SetActive(showKitchen && s.HasActiveOrder && s.Phase != DayPhase.Result && !atMainMenu);
            if (!orderHud.activeSelf) orderDetails.SetActive(false);
            bool showLesson = s.IsTutorial && s.HasActiveOrder && ((p == null && plate == null) || s.IsLessonPancake(p) || s.IsLessonPancake(plate));
            tutorialPanel.SetActive(showLesson);
            panGuide.gameObject.SetActive(p == null || p.Stage == PancakeStage.Batter);
            int balance = s.Phase == DayPhase.Result ? state.Profile.coins : s.Balance;
            dayText.text = s.StartProfile.day.ToString(); hud.text = Money(balance); embersText.text = state.Profile.honey.ToString();
            clock.Show(s);
            progress.text = o.Finished.Count + "/" + o.Portions.Count; scraperText.text = "×" + s.Scrapers;
            toKitchen.interactable = s.CanCook;
            orderStrip.text = string.Join("\n", Describe(o));
            hint.text = TutorialHint(s);
            dialogue.text = o.Request;
            bool leavingWithoutOrder = s.GuestLeaving && o.Phase == OrderPhase.Offered;
            dialogue.gameObject.SetActive(!terminal && !leavingWithoutOrder);
            orderPrice.text = (o.Price / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            orderPayment.SetActive(!terminal && !leavingWithoutOrder);
            visitor.texture = !terminal ? art.visitors[o.Visitor] : o.Report.Total >= 65 ? art.positiveVisitors[o.Visitor] : art.negativeVisitors[o.Visitor];
            visitor.uvRect = art.Uv(visitor.texture);
            if (visitor.texture != null) { float w = visitor.texture.width * visitor.uvRect.width, h = visitor.texture.height * visitor.uvRect.height; float fit = Mathf.Min(470 / w, 792 / h); visitor.rectTransform.sizeDelta = new Vector2(w * fit, h * fit); }
            float departure = Mathf.SmoothStep(0, 1, Mathf.Clamp01((s.GuestDepartureProgress - .45f) / .55f));
            visitor.rectTransform.anchoredPosition = visitorHome + Vector2.left * (750 * departure);
            visitor.color = new Color(1, 1, 1, 1 - departure);
            visitor.gameObject.SetActive(!s.GuestHasLeft);
            speechBubble.SetActive(!s.GuestHasLeft);
            float patience = 1 - (float)o.WaitTicks / o.PatienceLimit;
            FillBar(patienceBar, patience);
            FillBar(kitchenPatienceBar, patience);
            reaction.text = leavingWithoutOrder ? "До завтра!" : terminal ? Reaction(o) : "";
            reaction.gameObject.SetActive(s.GuestLeaving && !s.GuestHasLeft);
            counterPatience.SetActive(!s.GuestLeaving);
            clarify.gameObject.SetActive(!s.GuestLeaving); clarify.interactable = !o.Clarified;
            kitchenTray.interactable = system.CanPrepareServing;
            finishBatter.gameObject.SetActive(p != null && p.Stage == PancakeStage.Batter);
            flip.gameObject.SetActive(p != null && p.Stage == PancakeStage.SideA && (!s.IsLessonPancake(p) || (s.Tutorial != TutorialStep.Demonstration && s.Tutorial != TutorialStep.Discard)));
            move.gameObject.SetActive(p != null && p.Stage == PancakeStage.SideB);
            move.interactable = p != null && p.HeatB >= s.Rules.readyTicks && plate == null && finishing == null;
            move.GetComponentInChildren<Text>().text = plate != null || finishing != null ? "Освободите тарелку" : "На тарелку →";
            bool onPlate = plate != null && plate.Stage == PancakeStage.Plate;
            bool selectingBatter = system.BatterSelected;
            batterSelection.SetActive(selectingBatter);
            bool showPortionGuides = onPlate && !selectingBatter && system.SelectedIngredient >= 3;
            int filledTargets = showPortionGuides
                ? QualityEvaluator.MatchedPortionTargets(plate.Fillings[system.SelectedIngredient]?.Portions, s.Rules.portionTolerance) : 0;
            for (int i = 0; i < portionGuides.Count; i++)
                portionGuides[i].gameObject.SetActive(showPortionGuides && (filledTargets & (1 << i)) == 0);
            for (int i = 0; i < 3; i++) foldButtons[i].gameObject.SetActive(onPlate);
            if (s.FindTrayPancake(selectedReadyId) == null) selectedReadyId = 0;
            discard.interactable = p != null || plate != null || selectedReadyId != 0;
            finishFold.gameObject.SetActive(plate != null && plate.Stage == PancakeStage.Folding && !s.IsLessonPancake(plate));
            for (int i = 0; i < 5; i++)
            {
                ingredientCards[i].gameObject.SetActive(s.StartProfile.Owns((Ingredient)i));
                ingredientCards[i].raycastTarget = s.CanCook;
                bool selected = !selectingBatter && i == system.SelectedIngredient;
                ingredientSelections[i].SetActive(selected);
                helperBadges[i].SetActive(s.StartProfile.HelperEnabled((Ingredient)i));
                ingredientLabels[i].transform.parent.gameObject.SetActive(showLesson || selected);
            }
            remove.interactable = onPlate && !selectingBatter && s.Scrapers > 0 && plate.Fillings[system.SelectedIngredient] != null;
            if (onPlate && !selectingBatter && plate.Fillings[system.SelectedIngredient] != null) {
                var filling = plate.Fillings[system.SelectedIngredient];
                hint.text = Recipes.IngredientName((Ingredient)system.SelectedIngredient) + ": " + (system.SelectedIngredient < 3 ? filling.Mask.Percent + "% покрытия" : filling.Portions.Count + " порций") + ". Выберите форму, когда начинка готова.";
            }
            foldGuide.text = plate != null && plate.Stage == PancakeStage.Folding ? FoldHint(plate) : "";
            foldGuide.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(foldGuide.text));
            SetHeat(heatA, heatBarA, "I", p == null ? 0 : p.HeatA);
            SetHeat(heatB, heatBarB, "II", p == null ? 0 : p.HeatB);
            SetSideImage(sideArtA, p == null ? 0 : p.HeatA); SetSideImage(sideArtB, p == null ? 0 : p.HeatB);
            sideReadyA.SetActive(p != null && p.HeatA >= s.Rules.readyTicks && p.HeatA < s.Rules.burnTicks);
            sideReadyB.SetActive(p != null && p.HeatB >= s.Rules.readyTicks && p.HeatB < s.Rules.burnTicks);
            ShowDisplay(panDisplay, p);
            ShowDisplay(assemblyDisplay, finishing ?? plate);
            bool delivered = o.Phase == OrderPhase.Served && !s.GuestHasLeft;
            int deliveryTotal = delivered ? o.Finished.Count : s.TrayCount;
            int shownOnDelivery = Math.Min(deliveryTotal, deliveryDisplays.Count);
            for (int i = 0; i < deliveryDisplays.Count; i++) {
                var display = deliveryDisplays[i];
                var ready = i < shownOnDelivery ? delivered ? o.Finished[i] : s.TrayPancakeAt(i) : null;
                ShowDisplay(display, ready == finishing ? null : ready);
                var rect = (RectTransform)display.Root.transform;
                rect.anchoredPosition = new Vector2(210 + (i - (shownOnDelivery - 1) * .5f) * (shownOnDelivery == 3 ? 128 : 164), -73);
                rect.localScale = Vector3.one * (shownOnDelivery == 3 ? .78f : 1);
            }
            deliveryCount.text = "×" + deliveryTotal;
            deliveryCount.transform.parent.gameObject.SetActive(deliveryTotal > deliveryDisplays.Count);
            deliveryArrow.SetActive(CanServeTray);
            // Reviews count guests from previous days; order IDs count guests in this day.
            deliveryHint.transform.parent.gameObject.SetActive(CanServeTray && s.StartProfile.ratingCount + o.Id <= 3);
            if (!deliveryInput.IsDragging) deliveryTray.anchoredPosition = deliveryTrayHome + (delivered ? TrayServeDirection * 70 : Vector2.zero);
            trayPancakes.Clear();
            if (s.HasActiveOrder) foreach (var ready in o.Finished) if (ready != finishing) trayPancakes.Add(ready);
            foreach (var ready in s.ReadyPancakes) if (ready != finishing) trayPancakes.Add(ready);
            int lastPage = Math.Max(0, (trayPancakes.Count - 1) / readyDisplays.Count);
            trayPage = Mathf.Clamp(trayPage, 0, lastPage);
            for (int i = 0; i < readyDisplays.Count; i++) {
                int index = trayPage * readyDisplays.Count + i;
                var ready = index < trayPancakes.Count ? trayPancakes[index] : null;
                var display = readyDisplays[i];
                display.Input.Pancake = ready;
                ShowDisplay(display, ready);
                display.Selection.SetActive(ready != null && ready.Id == selectedReadyId);
            }
            trayPrevious.gameObject.SetActive(lastPage > 0); trayNext.gameObject.SetActive(lastPage > 0);
            trayPrevious.interactable = trayPage > 0; trayNext.interactable = trayPage < lastPage;
            trayCount.text = "Готово: " + s.TrayCount;
            trayCount.transform.parent.gameObject.SetActive(s.TrayCount > 0);
            if (s.Phase == DayPhase.Result)
            {
                var l = s.Ledger;
                resultDay.text = s.StartProfile.day.ToString();
                int[] amounts = { l.Revenue, l.Tips, -l.Refunds, -l.Products, -l.Rent };
                for (int i = 0; i < amounts.Length; i++) resultRows[i].text = (amounts[i] > 0 ? "+" : "") + Money(amounts[i]);
                profitText.text = (l.Profit > 0 ? "+" : "") + Money(l.Profit);
                resultText.text = s.Success ? "Смена завершена" : "Повторите день с прежним запасом";
            }
            storage.text = state.StorageError ?? "";
        }
        private static string[] Describe(OrderState order)
        { var lines = new string[order.Portions.Count]; for (int i = 0; i < lines.Length; i++) lines[i] = (i + 1) + ". " + order.Portions[i]; return lines; }
        private string Reaction(OrderState order)
        {
            if (order.Report == null) return "";
            return "Оценка " + order.Report.Total + "/100 · Чаевые " + Money(order.Report.Tips) + (order.Report.Refund > 0 ? " · Возврат " + Money(order.Report.Refund) : "") + "\n" + (order.Report.Reasons.Count > 0 ? string.Join(" ", order.Report.Reasons) : "Очень вкусно! Загляну ещё.");
        }
        private void SetHeat(Text text, Image bar, string side, int heat)
        {
            text.text = side == "I" ? "1" : "2";
            FillBar(bar, (float)heat / Session.Rules.burnTicks); bar.color = heat < Session.Rules.readyTicks ? Gold : heat < Session.Rules.burnTicks ? new Color(.49f, .75f, .39f) : new Color(.75f, .22f, .12f);
        }
        private void SetSideImage(RawImage image, int heat)
        {
            image.texture = heat < Session.Rules.readyTicks ? art.raw : heat < Session.Rules.burnTicks ? art.ready : art.burnt;
            image.uvRect = art.Uv(image.texture);
        }
        private static string FoldHint(PancakeState p)
        {
            var steps = Recipes.Gestures(p.IntendedFold); int i = Math.Min(p.Folds.Count, steps.Length - 1);
            string arrow = steps[i] == Direction.Up ? "↑ вверх" : steps[i] == Direction.Right ? "→ вправо" : steps[i] == Direction.Down ? "↓ вниз" : "← влево";
            return "Сгиб " + (i + 1) + "/" + steps.Length + "    " + arrow;
        }
        private static string TutorialHint(DaySession s)
        {
            var p = s.IsLessonPancake(s.PlatePancake) ? s.PlatePancake : s.Pancake;
            if (p != null && p.Stage == PancakeStage.Batter) return "Намажьте круг тестом. Покрытие: " + p.Batter.Percent + "%. Затем «Начать жарку».";
            if (s.IsLessonPancake(p) && p.Stage == PancakeStage.SideA && s.Tutorial == TutorialStep.SideA && p.HeatA >= s.Rules.readyTicks) return "Пора! Коснитесь блина или нажмите «Перевернуть». В этом уроке блин подождёт вас.";
            if (p != null && p.Stage == PancakeStage.Folding) return s.Feedback + " " + FoldHint(p);
            return s.Feedback;
        }
    }
}
