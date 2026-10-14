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
    public sealed partial class GameView : MonoBehaviour
    {
        private static readonly Color Ink = new Color(60 / 255f, 73 / 255f, 62 / 255f);
        private static readonly Color Cream = new Color(.98f, .94f, .83f, .97f);
        private static readonly Color Green = new Color(87 / 255f, 111 / 255f, 89 / 255f);
        private static readonly Color Gold = new Color(.75f, .44f, .18f);
        private GameController controller;
        private GameContent art;
        private Canvas canvas;
        private RectTransform root, plateSurface, trashZone;
        private GameObject kitchen, counter, results, shop, pausePanel, book;
        private Text hud, clock, orderStrip, hint, patienceText, dialogue, reaction, resultText, shopBalance, progress, foldGuide, storage;
        private Text heatA, heatB;
        private Image patienceBar, heatBarA, heatBarB;
        private RawImage visitor, ghost;
        private Button accept, clarify, nextGuest, finishBatter, flip, move, remove, discard, finishFold, toKitchen;
        private readonly List<Button> foldButtons = new List<Button>();
        private readonly List<Image> ingredientCards = new List<Image>();
        private readonly List<Text> ingredientLabels = new List<Text>();
        private readonly List<Text> portionGuides = new List<Text>();
        private readonly List<Action> shopRefreshers = new List<Action>();
        private readonly List<PancakeDisplay> displays = new List<PancakeDisplay>();
        private readonly Dictionary<PancakeState, CookingMotion> motions = new Dictionary<PancakeState, CookingMotion>();
        private CookingAtlas cookingAtlas;
        private bool dirty = true, showKitchen;
        private int capturedPointer = int.MinValue;
        private OrderPhase previousPhase;
        private double tickRemainder, saveTimer;
        public DaySession Session => controller.Session;
        public int SelectedIngredient { get; private set; }
        public GameController Controller => controller;
        public GameAudio Audio { get; private set; }
        private bool audioUiReady;

        private sealed class PancakeDisplay
        {
            public GameObject Root;
            public PancakeGraphic Base;
            public CookingEffectsGraphic Effects;
        }

        public void Initialize(GameController game, GameContent content)
        {
            controller = game; art = content;
            cookingAtlas = new CookingAtlas(art);
            Audio = gameObject.AddComponent<GameAudio>(); Audio.Initialize(game, content.audio);
            Build(); audioUiReady = true; controller.Changed += Invalidate;
            if (Session == null && !controller.StorageBlocked) controller.StartDay(Environment.TickCount);
            ShowFacade();
            Refresh();
        }
        private void Invalidate() { dirty = true; }
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
        private Button Button(string name, Transform parent, string value, float x, float y, float w, float h, Action action, bool secondary = false)
        {
            var image = Art(name, parent, "ui/button-cream.png", x, y, w, h, false); image.raycastTarget = true; var b = image.gameObject.AddComponent<Button>();
            b.targetGraphic = image; var colors = b.colors; colors.highlightedColor = new Color(1.05f, 1.05f, 1.05f); colors.pressedColor = new Color(.82f, .87f, .78f); colors.disabledColor = new Color(.75f, .75f, .72f, .65f); b.colors = colors;
            var label = Label("Label", image.transform, value, 12, 2, w - 24, h - 4, 24, Ink); label.alignment = TextAnchor.MiddleCenter;
            b.onClick.AddListener(() => {
                if (capturedPointer != int.MinValue) return;
                int before = Audio.FeedbackVersion; action();
                if (audioUiReady && before == Audio.FeedbackVersion) Audio.PlayUi(SoundCue.Button);
                dirty = true;
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
            if (Session == null || Session.Pancake != null || Session.Order.Finished.Count == 0) return null;
            var last = Session.Order.Finished[Session.Order.Finished.Count - 1];
            return motions.TryGetValue(last, out var motion) && motion.Folding ? last : null;
        }
        private void ShowDisplay(PancakeDisplay d, PancakeState p)
        {
            d.Root.SetActive(p != null); if (p == null) return;
            var motion = Motion(p); d.Base.Show(motion, Session.Rules); d.Effects.Show(motion, Session.Rules);
        }
        public bool Command(ActionKind kind, int a = 0, int b = 0, int c = 0, int d = 0)
        {
            // Observe before dispatch: the final gesture moves this same state to Finished.
            var p = Session?.Pancake;
            if (kind == ActionKind.BeginStroke && FinishingPancake() != null) return false;
            if (p != null) Motion(p);
            bool accepted = controller.Dispatch(new GameCommand(kind, a, b, c, d));
            if (!accepted && !controller.Paused && kind != ActionKind.Tick && kind != ActionKind.Stroke &&
                kind != ActionKind.EndStroke && kind != ActionKind.BeginStroke) Audio.PlayUi(SoundCue.Error);
            return accepted;
        }
        private void Buy(ShopItem item, int ingredient = 0)
        { if (!controller.Buy(item, ingredient)) Audio.PlayUi(SoundCue.Error); }
        public bool Capture(int pointer)
        {
            if (controller.Paused || capturedPointer != int.MinValue || Session == null || !Session.HasActiveOrder || FinishingPancake() != null) return false;
            capturedPointer = pointer; return true;
        }
        public bool OwnsPointer(int pointer) => capturedPointer == pointer;
        public void ReleasePointer() { capturedPointer = int.MinValue; }
        public bool OverTrash(Vector2 p) => RectTransformUtility.RectangleContainsScreenPoint(trashZone, p);
        public bool OverPlate(Vector2 p) => RectTransformUtility.RectangleContainsScreenPoint(plateSurface, p);
        public Vector2 PlateCoordinates(Vector2 p)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(plateSurface, p, null, out var local);
            return new Vector2((local.x / plateSurface.rect.width + .5f) * 1000, (local.y / plateSurface.rect.height + .5f) * 1000);
        }
        public void SelectIngredient(int type)
        {
            if (Session.Pancake == null || Session.Pancake.Stage != PancakeStage.Plate || !Session.StartProfile.Owns((Ingredient)type)) return;
            if (SelectedIngredient != type) Audio.PlayUi(SoundCue.Select);
            SelectedIngredient = type;
            if (Session.StartProfile.HelperEnabled((Ingredient)type)) Command(ActionKind.ApplyHelper, type);
            dirty = true;
        }
        public void MovePortionGhost(int type, Vector2 position)
        {
            if (!Session.StartProfile.Owns((Ingredient)type)) return;
            ghost.gameObject.SetActive(true); ghost.texture = art.fillings[type]; ghost.uvRect = art.Uv(ghost.texture); ghost.rectTransform.position = position;
        }
        public void HidePortionGhost() { if (ghost != null) ghost.gameObject.SetActive(false); }
        private void SwitchKitchen(bool show)
        {
            Command(ActionKind.EndStroke); ReleasePointer(); HidePortionGhost(); showKitchen = show; orderDetails.SetActive(false); dirty = true;
        }
        private void StartNextDay()
        {
            if (controller.StartDay(Environment.TickCount)) { motions.Clear(); showKitchen = false; shop.SetActive(false); SelectedIngredient = 0; dirty = true; }
        }
        public static string Money(int cents) => (cents / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        private static void FillBar(Image image, float fraction)
        {
            var rect = image.rectTransform;
            if (rect.pivot.x != 0) { rect.anchoredPosition -= new Vector2(rect.pivot.x * rect.rect.width, 0); rect.pivot = new Vector2(0, rect.pivot.y); }
            rect.localScale = new Vector3(Mathf.Clamp01(fraction), 1, 1);
        }
        public void Refresh()
        {
            dirty = false;
            Audio.SetContext(showKitchen, atFacade); RefreshAudioControls();
            if (controller.StorageBlocked || Session == null) { storage.text = controller.StorageError ?? "Смена не загружена."; return; }
            var s = Session; var o = s.Order; var p = s.Pancake;
            PancakeState finishing = showKitchen ? FinishingPancake() : null;
            bool terminal = o.Phase == OrderPhase.Served || o.Phase == OrderPhase.Cancelled;
            if (terminal && previousPhase != o.Phase && finishing == null) showKitchen = false;
            if (finishing == null) previousPhase = o.Phase;
            bool showingResult = s.Phase == DayPhase.Result && finishing == null;
            Audio.SetContext(showKitchen, atFacade);
            kitchen.SetActive(showKitchen && !showingResult && !atFacade); counter.SetActive(!showKitchen && !showingResult && !atFacade);
            results.SetActive(showingResult && !atFacade);
            pausePanel.SetActive((controller.Pauses & PauseReason.Menu) != 0 && !atFacade);
            orderHud.SetActive(showKitchen && s.Phase != DayPhase.Result && !atFacade);
            if (!orderHud.activeSelf) orderDetails.SetActive(false);
            tutorialPanel.SetActive(s.IsTutorial);
            panGuide.gameObject.SetActive(p == null || p.Stage == PancakeStage.Batter);
            int balance = s.Phase == DayPhase.Result ? controller.Profile.coins : s.Balance;
            dayText.text = s.StartProfile.day.ToString(); hud.text = Money(balance); honeyText.text = controller.Profile.honey.ToString();
            int minutes = 9 * 60 + Math.Min(s.ElapsedTicks, s.Rules.dayTicks) * 9 * 60 / s.Rules.dayTicks;
            clock.text = (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
            progress.text = o.Finished.Count + "/" + o.Portions.Count; scraperText.text = "×" + s.Scrapers;
            toKitchen.interactable = s.HasActiveOrder;
            orderStrip.text = string.Join("\n", Describe(o));
            hint.text = TutorialHint(s);
            dialogue.text = o.Request;
            visitor.texture = !terminal ? art.visitors[o.Visitor] : o.Report.Total >= 65 ? art.positiveVisitors[o.Visitor] : art.negativeVisitors[o.Visitor];
            visitor.uvRect = art.Uv(visitor.texture);
            if (visitor.texture != null) { float w = visitor.texture.width * visitor.uvRect.width, h = visitor.texture.height * visitor.uvRect.height; float fit = Mathf.Min(470 / w, 792 / h); visitor.rectTransform.sizeDelta = new Vector2(w * fit, h * fit); }
            float patience = 1 - (float)o.WaitTicks / o.PatienceLimit;
            patienceText.text = s.IsTutorial ? "Первый заказ: учимся без потерь и спешки" : "Терпение " + Mathf.CeilToInt(Mathf.Clamp01(patience) * 100) + "%";
            FillBar(patienceBar, patience);
            FillBar(kitchenPatienceBar, patience);
            reaction.text = terminal ? Reaction(o) : o.Clarified ? "Соберите все порции. Начинки добавляются на тарелке. Готовые блины выдаются вместе." : (s.IsTutorial ? "Оплата после успешного урока: " : "Оплата при принятии: ") + Money(o.Price) + " монет";
            accept.gameObject.SetActive(o.Phase == OrderPhase.Offered); clarify.gameObject.SetActive(!terminal); clarify.interactable = !o.Clarified;
            nextGuest.gameObject.SetActive(terminal); nextGuest.interactable = s.CanOfferNext;
            nextGuest.GetComponentInChildren<Text>().text = "";
            finishBatter.gameObject.SetActive(p != null && p.Stage == PancakeStage.Batter);
            flip.gameObject.SetActive(p != null && p.Stage == PancakeStage.SideA && s.Tutorial != TutorialStep.Demonstration && s.Tutorial != TutorialStep.Discard);
            move.gameObject.SetActive(p != null && p.Stage == PancakeStage.SideB); move.interactable = p != null && p.HeatB >= s.Rules.readyTicks;
            bool onPlate = p != null && p.Stage == PancakeStage.Plate;
            foreach (var guide in portionGuides) guide.gameObject.SetActive(onPlate && SelectedIngredient >= 3);
            for (int i = 0; i < 3; i++) foldButtons[i].gameObject.SetActive(onPlate);
            discard.interactable = p != null;
            finishFold.gameObject.SetActive(p != null && p.Stage == PancakeStage.Folding && !s.IsTutorial);
            for (int i = 0; i < 5; i++)
            {
                ingredientCards[i].gameObject.SetActive(s.StartProfile.Owns((Ingredient)i));
                ingredientCards[i].raycastTarget = onPlate;
                ingredientSelections[i].SetActive(onPlate && i == SelectedIngredient);
                helperBadges[i].SetActive(s.StartProfile.HelperEnabled((Ingredient)i));
                ingredientLabels[i].gameObject.SetActive(s.IsTutorial);
            }
            remove.interactable = onPlate && s.Scrapers > 0 && p.Fillings[SelectedIngredient] != null;
            if (onPlate && p.Fillings[SelectedIngredient] != null) {
                var filling = p.Fillings[SelectedIngredient];
                hint.text = Recipes.IngredientName((Ingredient)SelectedIngredient) + ": " + (SelectedIngredient < 3 ? filling.Mask.Percent + "% покрытия" : filling.Portions.Count + " порций") + ". Выберите форму, когда начинка готова.";
            }
            foldGuide.text = p != null && p.Stage == PancakeStage.Folding ? FoldHint(p) : "";
            SetHeat(heatA, heatBarA, "I", p == null ? 0 : p.HeatA);
            SetHeat(heatB, heatBarB, "II", p == null ? 0 : p.HeatB);
            SetSideImage(sideArtA, p == null ? 0 : p.HeatA); SetSideImage(sideArtB, p == null ? 0 : p.HeatB);
            sideReadyA.SetActive(p != null && p.HeatA >= s.Rules.readyTicks && p.HeatA < s.Rules.burnTicks);
            sideReadyB.SetActive(p != null && p.HeatB >= s.Rules.readyTicks && p.HeatB < s.Rules.burnTicks);
            ShowDisplay(displays[0], terminal && o.Finished.Count > 0 ? o.Finished[o.Finished.Count - 1] : null);
            ShowDisplay(displays[1], p != null && p.Stage <= PancakeStage.SideB ? p : null);
            ShowDisplay(displays[2], finishing ?? (p != null && p.Stage >= PancakeStage.Plate ? p : null));
            int trayIndex = o.Finished.Count - (finishing != null ? 2 : 1);
            ShowDisplay(displays[3], trayIndex >= 0 && !terminal ? o.Finished[trayIndex] : null);
            if (s.Phase == DayPhase.Result)
            {
                var l = s.Ledger;
                resultDay.text = s.StartProfile.day.ToString();
                int[] amounts = { l.Revenue, l.Tips, -l.Refunds, -l.Products, -l.Rent };
                for (int i = 0; i < amounts.Length; i++) resultRows[i].text = (amounts[i] > 0 ? "+" : "") + Money(amounts[i]);
                profitText.text = (l.Profit > 0 ? "+" : "") + Money(l.Profit);
                resultText.text = s.Success ? "Смена завершена" : "Повторите день с прежним запасом";
            }
            if (shop.activeSelf) { shopBalance.text = Money(controller.Profile.coins) + " монет · " + controller.Profile.honey + " мёда"; foreach (var update in shopRefreshers) update(); }
            storage.text = controller.StorageError ?? "";
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
            string arrow = steps[i] == Direction.Up ? "↑ снизу вверх" : steps[i] == Direction.Right ? "→ слева направо" : steps[i] == Direction.Down ? "↓ сверху вниз" : "← справа налево";
            return "Сгиб " + (i + 1) + "/" + steps.Length + "    " + arrow;
        }
        private static string TutorialHint(DaySession s)
        {
            var p = s.Pancake;
            if (p != null && p.Stage == PancakeStage.Batter) return "Намажьте круг тестом. Покрытие: " + p.Batter.Percent + "%. Затем «Начать жарку».";
            if (s.IsTutorial && p != null && p.Stage == PancakeStage.SideA && s.Tutorial == TutorialStep.SideA && p.HeatA >= s.Rules.readyTicks) return "Пора! Коснитесь блина или нажмите «Перевернуть». В этом уроке блин подождёт вас.";
            if (p != null && p.Stage == PancakeStage.Folding) return s.Feedback + " " + FoldHint(p);
            return s.Feedback;
        }
        private void Update()
        {
            if (controller == null) return;
            var safe = Screen.safeArea; float scale = canvas.scaleFactor;
            root.localScale = Vector3.one * Mathf.Min(safe.width / scale / 1600, safe.height / scale / 900);
            root.anchoredPosition = (safe.center - new Vector2(Screen.width, Screen.height) * .5f) / scale;
            if (controller.Paused) { tickRemainder = 0; return; }
            tickRemainder += Math.Min(Time.unscaledDeltaTime, .25f);
            int ticks = (int)(tickRemainder / .05); tickRemainder -= ticks * .05;
            if (ticks > 0) Command(ActionKind.Tick, ticks);
            saveTimer += Time.unscaledDeltaTime;
            if (saveTimer >= 2) { saveTimer = 0; controller.Save(); }
        }
        private void LateUpdate()
        {
            if (controller == null) return;
            if (!controller.Paused)
            {
                foreach (var motion in motions.Values)
                {
                    bool busy = motion.Busy; motion.Advance(Time.unscaledDeltaTime);
                    if (busy && !motion.Busy) dirty = true;
                }
            }
            if (dirty) Refresh();
            foreach (var display in displays) if (display.Root.activeInHierarchy && display.Base.Motion != null)
            { display.Base.Show(display.Base.Motion); display.Effects.Show(display.Base.Motion, Session.Rules); }
        }
        private void OnApplicationPause(bool paused) { ReleasePointer(); HidePortionGhost(); controller?.SetPause(PauseReason.Background, paused); tickRemainder = 0; }
        private void OnApplicationFocus(bool focused) { if (!focused) { ReleasePointer(); HidePortionGhost(); } controller?.SetPause(PauseReason.Focus, !focused); tickRemainder = 0; }
        private void OnApplicationQuit() { controller?.Save(); }
        private void OnDestroy() { cookingAtlas?.Dispose(); if (controller != null) { controller.Changed -= Invalidate; controller.Save(); } }
    }
}
