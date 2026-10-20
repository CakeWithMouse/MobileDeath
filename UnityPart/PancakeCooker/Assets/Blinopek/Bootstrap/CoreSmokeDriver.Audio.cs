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
            System.Command(ActionKind.BeginStroke, -1, 500, 500); yield return new WaitForSecondsRealtime(.07f);
            Check(System.Audio.GestureActive, "Accepted batter gesture starts its loop");
            yield return new WaitForSecondsRealtime(.4f);
            Check(!System.Audio.GestureActive, "Stationary finger stops gesture sound");
            System.Command(ActionKind.Stroke, 700, 500); yield return new WaitForSecondsRealtime(.07f);
            Check(System.Audio.GestureActive, "Moving held finger resumes gesture sound");
            System.Command(ActionKind.EndStroke); yield return new WaitForSecondsRealtime(.2f);
            Check(!System.Audio.GestureActive, "Releasing finger ends gesture sound");
        }
        private IEnumerator VerifyAudio()
        {
            Check(System.Audio.HasLibrary, "Game audio library is linked");
            Check(FindObjectsByType<AudioListener>().Length == 1, "One audio listener");
            yield return new WaitForSecondsRealtime(.3f);
            Check(System.Audio.FryingActive, "Cooking plays frying loop");
            var samples = new float[1024]; float peak = 0;
            for (int attempt = 0; attempt < 8; attempt++) {
                AudioListener.GetOutputData(samples, 0); foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
                yield return new WaitForSecondsRealtime(.05f);
            }
            Check(peak > .00001f, "Unity audio output contains a non-silent mix");
            Click("KitchenRecipeBook"); yield return null;
            Check(!System.Audio.FryingActive && System.Audio.PlayedCount(SoundCue.BookOpen) == 1, "Recipe book stops cooking loop and plays page opening");
            Click("CloseBook"); yield return new WaitForSecondsRealtime(.2f);
            Check(System.Audio.FryingActive, "Closing recipe book resumes cooking loop");
            Click("Pause"); yield return null;
            Check(!System.Audio.FryingActive, "Pause stops cooking audio");
            var slider = GameObject.Find("MusicVolume").GetComponent<Slider>();
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = Position((RectTransform)slider.transform, .75f, .5f) };
            ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            Check(System.Audio.MusicVolume > .65f && System.Audio.MusicVolume < .85f, "Music slider responds to pointer input");
            Click("CookingAlerts"); Check(System.Audio.CookingAlerts, "Readiness cue toggle works");
            yield return Shot("audio-settings");
            Click("Sound"); Check(System.Audio.Muted, "Mute button works");
            yield return new WaitForSecondsRealtime(.2f);
            Check(System.GetComponentsInChildren<AudioSource>().All(s => !s.isPlaying || s.volume == 0), "Mute silences all audio buses");
            Click("Sound"); Click("Resume"); yield return new WaitForSecondsRealtime(.2f);
            Check(!System.Audio.Muted && System.Audio.FryingActive, "Unmute and resume restore cooking sound");
            System.SetPause(PauseReason.Background, true); System.SetPause(PauseReason.Focus, true);
            yield return null;
            Check(System.GetComponentsInChildren<AudioSource>().All(s => !s.isPlaying), "Background and focus suspend all sources");
            System.SetPause(PauseReason.Background, false); yield return null;
            Check(!System.Audio.FryingActive, "Remaining focus pause keeps audio suspended");
            System.SetPause(PauseReason.Focus, false); yield return new WaitForSecondsRealtime(.2f);
            Check(System.Audio.FryingActive, "Clearing final pause resumes frying");
            slider.value = .35f; System.Audio.SetCookingAlerts(false);
        }
    }
}
#endif
