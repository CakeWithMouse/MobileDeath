using System;
using UnityEngine;
using UnityEngine.UI;

namespace Blinopek.Bootstrap
{
    // A resource-free overlay is available before the heavy content catalog starts loading.
    public sealed class LoadingScreen : MonoBehaviour
    {
        private GameObject blocker;
        private string stage, error;
        private float progress, actualProgress;
        private float minimumVisibleSeconds = 1f;
        private double completedAt;
        private float completedDisplay;
        private bool workComplete;
        public float ActualProgress => actualProgress;
        public float DisplayedProgress => progress;
        public bool ReadyToClose { get; private set; }
        public int DisplayedPercent => ReadyToClose ? 100 : Mathf.Min(99, Mathf.FloorToInt(progress * 100));
        public void SetMinimumVisibleTime(float seconds) { minimumVisibleSeconds = Mathf.Max(0, seconds); }
        public System.Collections.IEnumerator Complete(float seconds)
        {
            workComplete = true; actualProgress = 1; SetMinimumVisibleTime(seconds);
            completedAt = Time.realtimeSinceStartupAsDouble; completedDisplay = progress;
            yield return WaitMinimumVisibleTime(seconds);
            progress = 1; ReadyToClose = true; stage = "Готово";
            // Render the completed state for one frame, then the coordinator closes the screen.
            yield return null;
        }
        private void Update()
        {
            if (!Visible || error != null || ReadyToClose) return;
            float elapsed = (float)(Time.realtimeSinceStartupAsDouble - ShownAt);
            float timeline = minimumVisibleSeconds > 0 ? Mathf.Clamp01(elapsed / minimumVisibleSeconds) : 1;
            float target;
            if (workComplete)
            {
                double remaining = ShownAt + minimumVisibleSeconds - completedAt;
                float fraction = remaining > 0 ? Mathf.Clamp01((float)((Time.realtimeSinceStartupAsDouble - completedAt) / remaining)) : 1;
                target = Mathf.Lerp(completedDisplay, 1, fraction);
                progress = Mathf.Max(progress, Mathf.Min(.9999f, target));
            }
            else
            {
                // A stalled real operation must not drift towards 99% just because time passes.
                target = Mathf.Min(timeline, actualProgress * .99f);
                float blend = 1 - Mathf.Exp(-8 * Time.unscaledDeltaTime);
                progress = Mathf.Lerp(progress, Mathf.Max(progress, target), blend);
            }
        }
        private Action retry, back;
        public double ShownAt { get; private set; }
        public System.Collections.IEnumerator WaitMinimumVisibleTime(float seconds)
        {
            while (Time.realtimeSinceStartupAsDouble - ShownAt < seconds) yield return null;
        }
        public bool Visible => blocker != null && blocker.activeSelf;
        private void Awake()
        {
            blocker = new GameObject("LoadingOverlay", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            blocker.transform.SetParent(transform, false);
            var canvas = blocker.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = short.MaxValue;
            var panel = new GameObject("InputBlocker", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(blocker.transform, false);
            var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(.13f, .18f, .14f);
            blocker.SetActive(false);
        }
        public void Show(string value, float amount = 0) { blocker.SetActive(true); ShownAt = Time.realtimeSinceStartupAsDouble; stage = value; actualProgress = Mathf.Clamp01(amount); progress = 0; workComplete = ReadyToClose = false; error = null; retry = back = null; }
        public void Report(string value, float amount) { stage = value; actualProgress = Mathf.Max(actualProgress, Mathf.Clamp01(amount)); }
        public void Hide() { blocker.SetActive(false); retry = back = null; }
        public void Fail(Exception exception, Action again, Action close = null)
        { error = exception.Message; retry = again; back = close; Debug.LogException(exception); }
        private void OnGUI()
        {
            if (!Visible) return;
            float width = Mathf.Min(Screen.width - 40, 800), x = (Screen.width - width) / 2, y = Screen.height * .35f;
            var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Clamp(Screen.height / 30, 18, 36), alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUI.Label(new Rect(x, y, width, 90), error == null ? stage : "Не удалось загрузить\n" + error, style);
            var previousColor = GUI.color;
            GUI.color = new Color(.25f, .32f, .25f);
            GUI.DrawTexture(new Rect(x, y + 105, width, 24), Texture2D.whiteTexture);
            GUI.color = new Color(.84f, .69f, .37f);
            GUI.DrawTexture(new Rect(x, y + 105, width * progress, 24), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.Label(new Rect(x, y + 140, width, 45), DisplayedPercent + "%", style);
            if (error != null && GUI.Button(new Rect(x, y + 200, width, 50), "Повторить")) { var action = retry; retry = null; action?.Invoke(); }
            if (error != null && back != null && GUI.Button(new Rect(x, y + 260, width, 50), "Вернуться")) { var action = back; back = null; action(); }
        }
    }
}
