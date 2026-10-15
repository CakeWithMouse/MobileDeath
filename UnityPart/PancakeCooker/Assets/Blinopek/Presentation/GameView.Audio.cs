using System;
using Blinopek.Application;
using UnityEngine;
using UnityEngine.UI;

namespace Blinopek.Presentation
{
    public sealed partial class GameView
    {
        private RawImage soundIcon;
        private Text cookingAlertsLabel;
        private void BuildAudioControls()
        {
            AudioSlider("MusicVolume", "Музыка", 495, Audio.MusicVolume, Audio.SetMusicVolume);
            AudioSlider("EffectsVolume", "Эффекты", 550, Audio.EffectsVolume, Audio.SetEffectsVolume);
            AudioSlider("AmbienceVolume", "Фон кафе", 605, Audio.AmbienceVolume, Audio.SetAmbienceVolume);
            var alerts = Button("CookingAlerts", pausePanel.transform, "", 585, 659, 430, 64,
                () => { Audio.SetCookingAlerts(!Audio.CookingAlerts); Audio.PlayUi(SoundCue.Toggle); }, preserveCaps: true);
            cookingAlertsLabel = alerts.GetComponentInChildren<Text>(); RefreshAudioControls();
        }
        private void AudioSlider(string name, string title, float y, float initial, Action<float> change)
        {
            Label(name + "Title", pausePanel.transform, title, 495, y, 185, 40, 25);
            var value = Label(name + "Value", pausePanel.transform, Mathf.RoundToInt(initial * 100) + "%", 1045, y, 75, 40, 23);
            var r = Rect(name, pausePanel.transform, 700, y, 310, 40);
            r.gameObject.AddComponent<Image>().color = Color.clear; // Whole row is a touch target.
            Panel("Track", r, 0, 14, 310, 12, new Color(.7f, .69f, .59f));
            var fillArea = Rect("FillArea", r, 0, 14, 310, 12);
            var fill = Panel("Fill", fillArea, 0, 0, 0, 0, Green);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.anchoredPosition = Vector2.zero; fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Rect("HandleArea", r, 0, 20, 310, 0);
            var handle = FramedArt("Handle", handleArea, "ui/button-cream.png", 0, 0, 30, 38, 10);
            handle.rectTransform.anchorMin = handle.rectTransform.anchorMax = new Vector2(.5f, .5f);
            handle.rectTransform.anchoredPosition = Vector2.zero;
            var slider = r.gameObject.AddComponent<Slider>(); slider.targetGraphic = handle;
            slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform;
            slider.minValue = 0; slider.maxValue = 1; slider.value = initial;
            slider.onValueChanged.AddListener(v => { change(v); value.text = Mathf.RoundToInt(v * 100) + "%"; });
        }
        private void RefreshAudioControls()
        {
            if (soundIcon != null)
            {
                soundIcon.texture = art.Find(Audio.Muted ? "ui/glyphs/mute.png" : "ui/settings-speaker.png");
                soundIcon.uvRect = art.Uv(soundIcon.texture);
            }
            if (cookingAlertsLabel != null) cookingAlertsLabel.text = "Сигнал готовности: " + (Audio.CookingAlerts ? "вкл." : "выкл.");
        }
    }
}
