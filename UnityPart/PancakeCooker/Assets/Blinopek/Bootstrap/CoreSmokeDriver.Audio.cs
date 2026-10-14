#if UNITY_EDITOR || DEVELOPMENT_BUILD || BLINOPEK_QA
using System.Collections;
using System.Linq;
using Blinopek.Application;
using Blinopek.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Blinopek.Bootstrap
{
    public sealed partial class CoreSmokeDriver
    {
        private IEnumerator VerifyGestureAudio()
        {
            View.Command(ActionKind.BeginStroke, -1, 500, 500); yield return new WaitForSecondsRealtime(.07f);
            Check(View.Audio.GestureActive, "Accepted batter gesture starts its loop");
            yield return new WaitForSecondsRealtime(.4f);
            Check(!View.Audio.GestureActive, "Stationary finger stops gesture sound");
            View.Command(ActionKind.Stroke, 700, 500); yield return new WaitForSecondsRealtime(.07f);
            Check(View.Audio.GestureActive, "Moving held finger resumes gesture sound");
            View.Command(ActionKind.EndStroke); yield return new WaitForSecondsRealtime(.2f);
            Check(!View.Audio.GestureActive, "Releasing finger ends gesture sound");
        }
        private IEnumerator VerifyAudio()
        {
            Check(View.Audio.HasLibrary, "Game audio library is linked");
            Check(FindObjectsByType<AudioListener>().Length == 1, "One audio listener");
            yield return new WaitForSecondsRealtime(.3f);
            Check(View.Audio.FryingActive, "Cooking plays frying loop");
            var samples = new float[1024]; float peak = 0;
            for (int attempt = 0; attempt < 8; attempt++) {
                AudioListener.GetOutputData(samples, 0); foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
                yield return new WaitForSecondsRealtime(.05f);
            }
            Check(peak > .00001f, "Unity audio output contains a non-silent mix");
            Click("KitchenRecipeBook"); yield return null;
            Check(!View.Audio.FryingActive && View.Audio.PlayedCount(SoundCue.BookOpen) == 1, "Recipe book stops cooking loop and plays page opening");
            Click("CloseBook"); yield return new WaitForSecondsRealtime(.2f);
            Check(View.Audio.FryingActive, "Closing recipe book resumes cooking loop");
            Click("Pause"); yield return null;
            Check(!View.Audio.FryingActive, "Pause stops cooking audio");
            var slider = GameObject.Find("MusicVolume").GetComponent<Slider>();
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = Position((RectTransform)slider.transform, .75f, .5f) };
            ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            Check(View.Audio.MusicVolume > .65f && View.Audio.MusicVolume < .85f, "Music slider responds to pointer input");
            Click("CookingAlerts"); Check(View.Audio.CookingAlerts, "Readiness cue toggle works");
            yield return Shot("audio-settings");
            Click("Sound"); Check(View.Audio.Muted, "Mute button works");
            yield return new WaitForSecondsRealtime(.2f);
            Check(View.GetComponentsInChildren<AudioSource>().All(s => !s.isPlaying || s.volume == 0), "Mute silences all audio buses");
            Click("Sound"); Click("Resume"); yield return new WaitForSecondsRealtime(.2f);
            Check(!View.Audio.Muted && View.Audio.FryingActive, "Unmute and resume restore cooking sound");
            View.Controller.SetPause(PauseReason.Background, true); View.Controller.SetPause(PauseReason.Focus, true);
            yield return null;
            Check(View.GetComponentsInChildren<AudioSource>().All(s => !s.isPlaying), "Background and focus suspend all sources");
            View.Controller.SetPause(PauseReason.Background, false); yield return null;
            Check(!View.Audio.FryingActive, "Remaining focus pause keeps audio suspended");
            View.Controller.SetPause(PauseReason.Focus, false); yield return new WaitForSecondsRealtime(.2f);
            Check(View.Audio.FryingActive, "Clearing final pause resumes frying");
            slider.value = .35f; View.Audio.SetCookingAlerts(false);
        }
    }
}
#endif
