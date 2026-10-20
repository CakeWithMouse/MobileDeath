using Blinopek.Application;
using Blinopek.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    public sealed partial class GameScene
    {
        // 1) Владеет объектом.
        // Вывеска и элементы диалога, которые создаёт эта часть GameScene.
        private SignboardText cafeNameText;
        private Button renameCafe, saveCafeName;
        private InputField cafeNameInput;
        private GameObject cafeNameDialog;
        private Text cafeNameError, cafeNameCount;

        // 2) Общается через API.
        // Читает имя из state, проверяет через GameSystem.ValidateCafeName,
        // сохраняет через system.RenameCafe. Ссылки объявлены в GameScene.cs.

        // 3) Внутренние поля и объекты класса.
        // Отдельного состояния нет: черновик хранится в cafeNameInput, ошибки — в надписях.

        private void BuildSignboard()
        {
            var rect = Rect("CafeName", mainMenu.transform, 280, 46, 1040, 110);
            cafeNameText = rect.gameObject.AddComponent<SignboardText>();
            cafeNameText.font = art.signboardFont != null ? art.signboardFont : art.titleFont;
            cafeNameText.text = state.CafeName; cafeNameText.fontSize = 110;
            cafeNameText.supportRichText = false; cafeNameText.raycastTarget = true;
            cafeNameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            cafeNameText.verticalOverflow = VerticalWrapMode.Overflow;
            cafeNameText.alignment = TextAnchor.MiddleCenter;
            cafeNameText.color = new Color(56/255f, 84/255f, 72/255f, .94f);
            cafeNameText.SetPaint(art.signboardShader);
            renameCafe = rect.gameObject.AddComponent<Button>();
            renameCafe.targetGraphic = cafeNameText; renameCafe.transition = Selectable.Transition.None;
            renameCafe.onClick.AddListener(OpenCafeNameDialog);
        }

        private void BuildCafeNameDialog()
        {
            cafeNameDialog = Modal("CafeNameDialog");
            Paper("NamePaper", cafeNameDialog.transform, 390, 225, 820, 435);
            Label("NameTitle", cafeNameDialog.transform, "Название блинной", 460, 260, 680, 60, 38, Ink, true).alignment = TextAnchor.MiddleCenter;
            var field = Panel("CafeNameInput", cafeNameDialog.transform, 465, 355, 670, 76, Color.clear);
            cafeNameInput = field.gameObject.AddComponent<InputField>(); cafeNameInput.targetGraphic = field;
            var inputText = Label("NameInputText", field.transform, "", 18, 8, 634, 60, 32);
            inputText.supportRichText = false; inputText.horizontalOverflow = HorizontalWrapMode.Overflow;
            cafeNameInput.textComponent = inputText;
            cafeNameInput.lineType = InputField.LineType.SingleLine;
            cafeNameInput.characterLimit = GameSystem.CafeNameMaxLength;
            cafeNameInput.customCaretColor = true; cafeNameInput.caretColor = Ink;
            cafeNameInput.selectionColor = new Color(.47f, .62f, .49f, .35f);
            cafeNameInput.onValueChanged.AddListener(_ => UpdateCafeNameDraft());
            cafeNameInput.onSubmit.AddListener(_ => SaveCafeName());
            cafeNameInput.onEndEdit.AddListener(_ => { if (cafeNameInput.wasCanceled) CloseCafeNameDialog(); });
            cafeNameCount = Label("NameLength", cafeNameDialog.transform, "", 985, 440, 150, 34, 20);
            cafeNameCount.alignment = TextAnchor.MiddleRight;
            cafeNameError = Label("NameError", cafeNameDialog.transform, "", 465, 475, 670, 58, 21, new Color(.65f, .18f, .12f));
            Button("CancelCafeName", cafeNameDialog.transform, "Отмена", 465, 555, 290, 66, CloseCafeNameDialog, preserveCaps: true);
            saveCafeName = Button("SaveCafeName", cafeNameDialog.transform, "Сохранить", 845, 555, 290, 66, SaveCafeName, preserveCaps: true);
            cafeNameDialog.SetActive(false);
        }
        private void OpenCafeNameDialog()
        {
            if (!atMainMenu || state.StorageBlocked || system.HasCapturedPointer) return;
            cafeNameInput.SetTextWithoutNotify(state.CafeName);
            UpdateCafeNameDraft(); cafeNameDialog.transform.SetAsLastSibling(); cafeNameDialog.SetActive(true);
            cafeNameInput.Select(); cafeNameInput.ActivateInputField(); Audio.PlayUi(SoundCue.Button);
        }
        private void UpdateCafeNameDraft()
        {
            bool valid = GameSystem.ValidateCafeName(cafeNameInput.text, out _, out string error);
            cafeNameCount.text = cafeNameInput.text.Length + " / " + GameSystem.CafeNameMaxLength;
            cafeNameError.text = error ?? ""; saveCafeName.interactable = valid && !state.StorageBlocked;
        }
        private void CloseCafeNameDialog()
        {
            if (cafeNameDialog == null || !cafeNameDialog.activeSelf) return;
            // Hide before deactivation, since InputField may raise onEndEdit synchronously.
            cafeNameDialog.SetActive(false); system.Invalidate();
        }
        private void SaveCafeName()
        {
            if (!cafeNameDialog.activeSelf) return;
            if (!system.RenameCafe(cafeNameInput.text, out string error)) {
                cafeNameError.text = error; Audio.PlayUi(SoundCue.Error); return;
            }
            cafeNameText.text = state.CafeName;
            CloseCafeNameDialog();
        }
        private void RefreshSignboard()
        {
            if (cafeNameText.text != state.CafeName) cafeNameText.text = state.CafeName;
            renameCafe.interactable = !state.StorageBlocked;
        }
    }
}
