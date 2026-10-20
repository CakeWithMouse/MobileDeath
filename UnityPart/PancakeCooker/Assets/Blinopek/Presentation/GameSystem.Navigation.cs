using Blinopek.Application;
using Blinopek.Domain;

namespace Blinopek.Presentation
{
    public sealed partial class GameSystem
    {
        // 1) Владеет объектом.
        // GameScene и GameAudio объявлены в GameSystem.cs.

        // 2) Общается через API.
        // controller меняет смену/паузы; Scene применяет визуальную часть перехода.

        // 3) Внутренние поля и объекты класса.
        // Навигация принадлежит системе и не меняется при отрисовке сцены.
        public bool AtMainMenu { get; private set; }
        public bool InKitchen { get; private set; }
        public bool SettingsOpen { get; private set; }
        private OrderPhase previousOrderPhase;
        internal bool CanOpenShop => AtMainMenu && !controller.StorageBlocked &&
            (Session == null || Session.Phase == DayPhase.Result);

        internal void ShowMainMenu()
        {
            SetPause(PauseReason.Menu, true);
            SetPause(PauseReason.RecipeBook, false);
            ReleasePointer();
            AtMainMenu = true;
            SettingsOpen = false;
            Scene.PresentMainMenu();
            Audio.PlayUi(SoundCue.Back);
            Invalidate();
        }

        internal void PlayFromMainMenu()
        {
            if (!AtMainMenu || Scene.IsEditingCafeName || controller.StorageBlocked) return;
            SetPause(PauseReason.Menu, false);
            if (Session == null || Session.Phase == DayPhase.Result) StartNextDay();
            if (Session == null || Session.Phase == DayPhase.Result)
            {
                SetPause(PauseReason.Menu, true);
                return;
            }
            AtMainMenu = SettingsOpen = false;
            Scene.PresentGame();
            Invalidate();
        }

        internal void OpenSettings()
        {
            SettingsOpen = true;
            SetPause(PauseReason.Menu, true);
            Scene.RaiseSettings();
        }

        internal void CloseSettings()
        {
            SettingsOpen = false;
            if (!AtMainMenu) SetPause(PauseReason.Menu, false);
            Invalidate();
            Audio.PlayUi(SoundCue.Back);
        }

        internal void OpenBook()
        {
            SetPause(PauseReason.RecipeBook, true);
            Scene.PresentBook(true);
            Audio.PlayUi(SoundCue.BookOpen);
        }

        internal void CloseBook()
        {
            SetPause(PauseReason.RecipeBook, false);
            Scene.PresentBook(false);
            Audio.PlayUi(SoundCue.BookClose);
        }

        internal void OpenShop()
        {
            if (!CanOpenShop) return;
            Scene.PresentShop();
            Invalidate();
        }

        internal void SwitchKitchen(bool show)
        {
            Command(ActionKind.EndStroke);
            ReleasePointer();
            InKitchen = show;
            Scene.ClearKitchenInteraction();
            Invalidate();
        }

        private void UpdateNavigation()
        {
            var s = Session;
            if (controller.StorageBlocked || s == null) return;
            bool finishing = InKitchen && Scene.IsFinishingPancake;
            if (s.Order.Phase == OrderPhase.Served && previousOrderPhase != s.Order.Phase &&
                !finishing && s.PanPancake == null && s.PlatePancake == null && s.ReadyPancakes.Count == 0)
                InKitchen = false;
            if (s.Phase == DayPhase.Closing && s.GuestLeaving) InKitchen = false;
            if (!finishing) previousOrderPhase = s.Order.Phase;
        }
    }
}
