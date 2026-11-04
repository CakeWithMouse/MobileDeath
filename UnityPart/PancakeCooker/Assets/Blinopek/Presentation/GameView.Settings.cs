using Blinopek.Application;
using Blinopek.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    public sealed partial class GameView
    {
        private bool settingsAtMainMenu;
        private Button restartDay;
        private GameObject resetConfirmation;
        private Text resetError;

        private void OpenSettings()
        {
            settingsAtMainMenu = atMainMenu;
            pausePanel.transform.SetAsLastSibling();
            controller.SetPause(PauseReason.Menu, true);
        }
        private void CloseSettings()
        {
            settingsAtMainMenu = false;
            if (!atMainMenu) controller.SetPause(PauseReason.Menu, false);
            dirty = true;
            Audio.PlayUi(SoundCue.Back);
        }
        private void RestartCurrentDay() => Transition(RestartCurrentDayImmediate, "Начинаем смену заново");
        private void RestartCurrentDayImmediate()
        {
            if (atMainMenu) return;
            if (!controller.RestartDay()) { Audio.PlayUi(SoundCue.Error); return; }
            ReleasePointer(); HidePortionGhost(); HideReadyGhost();
            motions.Clear(); trayPancakes.Clear(); showKitchen = false;
            SelectedIngredient = 0; batterSelected = true; selectedReadyId = trayPage = 0;
            previousPhase = OrderPhase.Offered; tickRemainder = saveTimer = 0;
            book.SetActive(false); orderDetails.SetActive(false); resetConfirmation.SetActive(false);
            settingsAtMainMenu = false; pausePanel.SetActive(false); dirty = true;
        }
        private void BuildResetControls()
        {
            var reset = Button("ResetProgress", pausePanel.transform, "Сбросить прогресс", 585, 735, 430, 64, () => {
                resetError.text = "";
                resetConfirmation.transform.SetAsLastSibling(); resetConfirmation.SetActive(true);
            }, preserveCaps: true);
            reset.interactable = controller.CanResetProgress;
            var danger = new Color(.65f, .18f, .12f);
            reset.GetComponentInChildren<Text>().color = danger;
            resetConfirmation = Modal("ResetProgressConfirmation");
            Paper("ResetPaper", resetConfirmation.transform, 390, 220, 820, 470);
            Label("ResetTitle", resetConfirmation.transform, "Сбросить прогресс?", 460, 255, 680, 60, 38, Ink, true).alignment = TextAnchor.MiddleCenter;
            Label("ResetDescription", resetConfirmation.transform,
                "Дни, покупки, златники, жаринки и текущая смена будут сброшены. Игра начнётся с первого дня и обучения.\n\nОтменить сброс нельзя. Настройки звука сохранятся.",
                460, 325, 680, 160, 25).alignment = TextAnchor.MiddleCenter;
            resetError = Label("ResetError", resetConfirmation.transform, "", 460, 485, 680, 65, 21, danger);
            resetError.alignment = TextAnchor.MiddleCenter;
            Button("CancelResetProgress", resetConfirmation.transform, "Отмена", 460, 575, 310, 64, () => resetConfirmation.SetActive(false));
            Button("ConfirmResetProgress", resetConfirmation.transform, "Сбросить прогресс", 830, 575, 310, 64, ConfirmResetProgress)
                .GetComponentInChildren<Text>().color = danger;
            resetConfirmation.SetActive(false);
        }
        private void ConfirmResetProgress()
        {
            if (!controller.ResetProgress()) {
                resetError.text = "Не удалось сбросить прогресс. Проверьте свободное место и повторите попытку.";
                Audio.PlayUi(SoundCue.Error); return;
            }
            resetConfirmation.SetActive(false); book.SetActive(false); orderDetails.SetActive(false);
            ReleasePointer(); HidePortionGhost(); HideReadyGhost();
            motions.Clear(); trayPancakes.Clear(); showKitchen = false;
            SelectedIngredient = 0; batterSelected = true; selectedReadyId = trayPage = 0;
            previousPhase = OrderPhase.Offered; tickRemainder = saveTimer = 0;
            ShowMainMenu();
        }
    }
}
