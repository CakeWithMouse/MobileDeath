using Blinopek.Application;
using Blinopek.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    public sealed partial class GameScene
    {
        // 1) Владеет объектом.
        // Элементы настроек и подтверждения сброса, созданные GameScene.
        private Button restartDay;
        private GameObject resetConfirmation;
        private Text resetError;

        // 2) Общается через API.
        // Через system запрашивает паузу, перезапуск и сброс; зависимости объявлены в GameScene.cs.

        // 3) Внутренние поля и объекты класса.
        // Ошибка сброса хранится в resetError; навигацией и паузами владеет GameSystem.
        internal void RaiseSettings() => pausePanel.transform.SetAsLastSibling();

        private void BuildResetControls()
        {
            var reset = Button("ResetProgress", pausePanel.transform, "Сбросить прогресс", 585, 735, 430, 64, () => {
                resetError.text = "";
                resetConfirmation.transform.SetAsLastSibling(); resetConfirmation.SetActive(true);
            }, preserveCaps: true);
            reset.interactable = state.CanResetProgress;
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
            if (!system.ResetProgress())
                resetError.text = "Не удалось сбросить прогресс. Проверьте свободное место и повторите попытку.";
        }
    }
}
