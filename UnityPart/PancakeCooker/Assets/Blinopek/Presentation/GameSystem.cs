using System;
using Blinopek.Application;
using Blinopek.Domain;
using UnityEngine;

namespace Blinopek.Presentation
{
    // Управляет жизненным циклом игры и действиями игрока. Отрисовкой владеет GameScene.
    public sealed partial class GameSystem : MonoBehaviour
    {
        // 1) Владеет объектом.
        // Создаёт GameScene и освобождает её ресурсы при уничтожении.
        // Создаёт GameAudio на своём GameObject; компонент уничтожается вместе с ним.
        public GameScene Scene { get; private set; }
        public GameAudio Audio { get; private set; }

        // 2) Общается через API.
        // Контроллер приходит из GameStartup; текущая сессия принадлежит контроллеру.
        private GameController controller;
        public IGameState State => controller;
        public DaySession Session => controller.Session;

        // 3) Внутренние поля и объекты класса.
        // Состояние ввода, таймеры и признак необходимости обновить отображение.
        private bool dirty = true;
        private int capturedPointer = int.MinValue;
        private double tickRemainder, saveTimer;
        public int SelectedIngredient { get; private set; }
        public bool BatterSelected { get; private set; } = true;
        public bool HasCapturedPointer => capturedPointer != int.MinValue;
        public bool CanPrepareServing => Session != null && Session.CanServeOrder && !controller.Paused;
        public bool CanServeTray => CanPrepareServing && Scene.CanServeTray;
        public Vector2 TrayServeDirection => Scene.TrayServeDirection;

        public void Initialize(GameController game, GameContent content)
        {
            controller = game;
            Audio = gameObject.AddComponent<GameAudio>();
            Audio.Initialize(State, content.audio);
            Scene = new GameScene(this, State, content);
            Scene.Initialize();
            controller.CommandApplied += Audio.OnCommand;
            controller.DayStarted += Audio.OnDay;
            controller.PurchaseApplied += Audio.OnPurchase;
            controller.Changed += Audio.SyncPause;
            controller.Changed += Invalidate;
            ShowMainMenu();
            Refresh();
        }

        internal void Invalidate() => dirty = true;

        public bool Command(ActionKind kind, int a = 0, int b = 0, int c = 0, int d = 0)
        {
            if (kind == ActionKind.MoveToPlate && Scene.IsFinishingPancake) return false;
            // Observe before dispatch: the final gesture moves this same state to Finished.
            Scene.ObserveCooking();
            bool accepted = controller.Dispatch(new GameCommand(kind, a, b, c, d));
            if (!accepted && !controller.Paused && kind != ActionKind.Tick && kind != ActionKind.Stroke &&
                kind != ActionKind.EndStroke && kind != ActionKind.BeginStroke)
                Audio.PlayUi(SoundCue.Error);
            return accepted;
        }

        public void SetPause(PauseReason reason, bool enabled) => controller.SetPause(reason, enabled);
        public bool RenameCafe(string name, out string error) => controller.RenameCafe(name, out error);
        internal const int CafeNameMaxLength = GameController.CafeNameMaxLength;
        internal static bool ValidateCafeName(string name, out string normalized, out string error)
            => GameController.ValidateCafeName(name, out normalized, out error);

        internal void Buy(ShopItem item, int ingredient = 0)
        {
            if (!controller.Buy(item, ingredient)) Audio.PlayUi(SoundCue.Error);
        }

        internal void StartNextDay()
        {
            if (controller.StartDay(Environment.TickCount)) ResetDayPresentation();
        }

        internal void RestartCurrentDay()
        {
            if (AtMainMenu) return;
            if (!controller.RestartDay()) { Audio.PlayUi(SoundCue.Error); return; }
            ResetDayPresentation();
        }

        internal bool ResetProgress()
        {
            if (!controller.ResetProgress()) { Audio.PlayUi(SoundCue.Error); return false; }
            ResetDayPresentation();
            ShowMainMenu();
            return true;
        }

        private void ResetDayPresentation()
        {
            ReleasePointer();
            SelectedIngredient = 0;
            BatterSelected = true;
            tickRemainder = saveTimer = 0;
            InKitchen = SettingsOpen = false;
            previousOrderPhase = OrderPhase.Offered;
            Scene.ResetDayPresentation();
        }

        public bool Capture(int pointer, bool pan = false, bool ready = false)
        {
            if (controller.Paused || HasCapturedPointer || Session == null || !Session.CanCook ||
                (!pan && Scene.IsFinishingPancake)) return false;
            if (!ready) Scene.ClearReadySelection();
            capturedPointer = pointer;
            return true;
        }

        public bool OwnsPointer(int pointer) => capturedPointer == pointer;
        public void ReleasePointer() => capturedPointer = int.MinValue;

        public void SelectBatter()
        {
            if (controller.Paused || Session == null || !Session.CanCook) return;
            if (!BatterSelected) Audio.PlayUi(SoundCue.Select);
            BatterSelected = true;
            Invalidate();
        }

        public void SelectIngredient(int type)
        {
            if (controller.Paused || Session == null || !Session.CanCook || type < 0 || type >= 5 ||
                !Session.StartProfile.Owns((Ingredient)type)) return;
            if (BatterSelected || SelectedIngredient != type) Audio.PlayUi(SoundCue.Select);
            SelectedIngredient = type;
            BatterSelected = false;
            if (Session.PlatePancake?.Stage == PancakeStage.Plate && Session.StartProfile.HelperEnabled((Ingredient)type))
                Command(ActionKind.ApplyHelper, type);
            Invalidate();
        }

        public void BeginServing()
        {
            if (!CanPrepareServing || !Scene.CanBeginServing || HasCapturedPointer) return;
            SwitchKitchen(false);
            Audio.PlayUi(SoundCue.Button);
            Refresh();
        }

        // Input handlers use scene geometry without owning or constructing UI elements.
        public bool OverTrash(Vector2 point) => Scene.OverTrash(point);
        public bool OverPlate(Vector2 point) => Scene.OverPlate(point);
        public Vector2 PlateCoordinates(Vector2 point) => Scene.PlateCoordinates(point);
        public void SelectReadyPancake(int id) => Scene.SelectReadyPancake(id);
        public void MoveReadyGhost(PancakeState pancake, Vector2 point) => Scene.MoveReadyGhost(pancake, point);
        public void HideReadyGhost() => Scene?.HideReadyGhost();
        public void MovePortionGhost(int type, Vector2 point) => Scene.MovePortionGhost(type, point);
        public void HidePortionGhost() => Scene?.HidePortionGhost();

        private void AcceptOfferedOrder()
        {
            if (!AtMainMenu && !controller.StorageBlocked && !controller.Paused &&
                Session != null && Session.Phase == DayPhase.Serving && Session.Order.Phase == OrderPhase.Offered)
                Command(ActionKind.Accept);
        }

        public void Refresh()
        {
            // Orchestration precedes drawing; rendering GameScene alone never accepts an order.
            AcceptOfferedOrder();
            UpdateNavigation();
            Audio.SetContext(InKitchen, AtMainMenu);
            dirty = false;
            Scene.Render();
        }

        private void Update()
        {
            if (controller == null) return;
            Scene.FitSafeArea();
            if (controller.Paused) { tickRemainder = 0; return; }
            tickRemainder += Math.Min(Time.unscaledDeltaTime, .25f);
            int ticks = (int)(tickRemainder / .05);
            tickRemainder -= ticks * .05;
            if (ticks > 0) Command(ActionKind.Tick, ticks);
            saveTimer += Time.unscaledDeltaTime;
            if (saveTimer >= 2) { saveTimer = 0; controller.Save(); }
        }

        private void LateUpdate()
        {
            if (controller == null) return;
            Scene.AdvanceAnimations(Time.unscaledDeltaTime);
            if (dirty) Refresh();
            Scene.DrawAnimatedPancakes();
        }

        private void OnApplicationPause(bool paused)
        {
            ReleasePointer(); HidePortionGhost(); HideReadyGhost();
            controller?.SetPause(PauseReason.Background, paused);
            tickRemainder = 0;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) { ReleasePointer(); HidePortionGhost(); HideReadyGhost(); }
            controller?.SetPause(PauseReason.Focus, !focused);
            tickRemainder = 0;
        }

        private void OnApplicationQuit() => controller?.Save();

        private void OnDestroy()
        {
            Scene?.Dispose();
            if (controller == null) return;
            // ReferenceEquals учитывает и уже уничтоженный Unity-компонент: подписку всё равно снимаем.
            if (!ReferenceEquals(Audio, null))
            {
                controller.CommandApplied -= Audio.OnCommand;
                controller.DayStarted -= Audio.OnDay;
                controller.PurchaseApplied -= Audio.OnPurchase;
                controller.Changed -= Audio.SyncPause;
            }
            controller.Changed -= Invalidate;
            controller.Save();
        }
    }
}
