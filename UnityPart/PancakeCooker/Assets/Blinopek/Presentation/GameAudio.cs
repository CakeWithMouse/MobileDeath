using System;
using System.Collections.Generic;
using Blinopek.Application;
using Blinopek.Domain;
using UnityEngine;

namespace Blinopek.Presentation
{
    // Local presentation only: no sound state is written into the deterministic gameplay save.
    public sealed class GameAudio : MonoBehaviour
    {
        private const string Pref = "Blinopek.Audio.";
        private GameController controller;
        private GameAudioLibrary library;
        private AudioFeedback feedback;
        private AudioSource ui, fry, gesture, ambience, dayMusic, menuMusic;
        private readonly List<Pending> pending = new List<Pending>();
        private readonly Dictionary<SoundCue, int> variants = new Dictionary<SoundCue, int>();
        private readonly Dictionary<SoundCue, int> played = new Dictionary<SoundCue, int>();
        private AudioSource[] voices;
        private readonly float[] voiceGains = new float[8];
        private bool kitchen, facade, worldPaused, suspended, settingsDirty, persistSettings;
        private float clock, gestureUntil, changedAt, lastError = -10;
        private int gestureType = -2, lastX, lastY;
        public float MusicVolume { get; private set; } = .35f;
        public float EffectsVolume { get; private set; } = .8f;
        public float AmbienceVolume { get; private set; } = .3f;
        public bool Muted { get; private set; }
        public bool CookingAlerts { get; private set; }
        public int FeedbackVersion { get; private set; }
        public bool FryingActive => fry != null && fry.isPlaying && fry.volume > .001f;
        public bool GestureActive => gesture != null && gesture.isPlaying && gesture.volume > .001f;
        public bool HasLibrary => library != null;
        public int PlayedCount(SoundCue cue) => played.TryGetValue(cue, out int count) ? count : 0;

        private struct Pending { public SoundCue Cue; public float At; }

        public void Initialize(GameController game, GameAudioLibrary content)
        {
            controller = game; library = content; feedback = new AudioFeedback(game.Session);
            persistSettings = Array.IndexOf(Environment.GetCommandLineArgs(), "-blinopekSmoke") < 0;
            if (persistSettings)
            {
                MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Pref + "Music", .35f));
                EffectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Pref + "Effects", .8f));
                AmbienceVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Pref + "Ambience", .3f));
                Muted = PlayerPrefs.GetInt(Pref + "Muted", 0) != 0;
                CookingAlerts = PlayerPrefs.GetInt(Pref + "CookingAlerts", 0) != 0;
            }
            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            ui = Source("Audio UI"); fry = Source("Audio Frying", true); gesture = Source("Audio Gesture", true);
            ambience = Source("Audio Cafe", true); dayMusic = Source("Audio Day Music", true); menuMusic = Source("Audio Menu Music", true);
            voices = new AudioSource[8];
            for (int i = 0; i < voices.Length; i++) voices[i] = Source("Audio Effect " + i);
            controller.CommandApplied += OnCommand; controller.DayStarted += OnDay;
            controller.PurchaseApplied += OnPurchase; controller.Changed += SyncPause;
            if (library == null) Debug.LogWarning("Blinopek audio library is missing. Run Prepare gameplay scenes.");
            SyncPause();
        }
        private AudioSource Source(string name, bool loop = false)
        {
            var child = new GameObject(name); child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>(); source.playOnAwake = false;
            source.spatialBlend = 0; source.loop = loop; source.volume = 0; return source;
        }
        public void SetContext(bool inKitchen, bool atFacade) { kitchen = inKitchen; facade = atFacade; }
        public void SetMusicVolume(float value) { MusicVolume = Mathf.Clamp01(value); SettingsChanged(); }
        public void SetEffectsVolume(float value) { EffectsVolume = Mathf.Clamp01(value); SettingsChanged(); }
        public void SetAmbienceVolume(float value) { AmbienceVolume = Mathf.Clamp01(value); SettingsChanged(); }
        public void SetMuted(bool value)
        {
            Muted = value; SettingsChanged();
            if (value) { ui.Stop(); StopWorld(); dayMusic.volume = menuMusic.volume = ambience.volume = 0; }
        }
        public void SetCookingAlerts(bool value) { CookingAlerts = value; SettingsChanged(); }
        private void SettingsChanged() { settingsDirty = true; changedAt = Time.unscaledTime; }
        public void SavePreferences()
        {
            if (!settingsDirty || !persistSettings) return;
            PlayerPrefs.SetFloat(Pref + "Music", MusicVolume); PlayerPrefs.SetFloat(Pref + "Effects", EffectsVolume);
            PlayerPrefs.SetFloat(Pref + "Ambience", AmbienceVolume); PlayerPrefs.SetInt(Pref + "Muted", Muted ? 1 : 0);
            PlayerPrefs.SetInt(Pref + "CookingAlerts", CookingAlerts ? 1 : 0); PlayerPrefs.Save(); settingsDirty = false;
        }
        private void SyncPause()
        {
            bool wasPaused = worldPaused, wasSuspended = suspended;
            worldPaused = controller.Paused;
            suspended = (controller.Pauses & (PauseReason.Focus | PauseReason.Background)) != 0;
            if (worldPaused && !wasPaused) StopWorld();
            if (suspended && !wasSuspended)
            {
                ui.Stop(); dayMusic.Pause(); menuMusic.Pause(); ambience.Pause(); SavePreferences();
            }
            else if (!suspended && wasSuspended) { dayMusic.UnPause(); menuMusic.UnPause(); ambience.UnPause(); }
        }
        private void StopWorld()
        {
            pending.Clear(); gestureUntil = 0; gestureType = -2;
            fry.Stop(); gesture.Stop(); fry.volume = gesture.volume = 0;
            foreach (var voice in voices) voice.Stop();
        }
        private void OnDay()
        {
            StopWorld(); feedback.Reset(controller.Session);
            Queue(SoundCue.DayOpen); Queue(SoundCue.GuestEnter, .7f);
        }
        private void OnPurchase(ShopItem item)
        { PlayUi(item == ShopItem.ToggleHelper ? SoundCue.Toggle : SoundCue.Purchase); }
        private void OnCommand(GameCommand command)
        {
            foreach (var signal in feedback.AfterCommand(controller.Session, command))
            {
                if (signal.Cue == SoundCue.Ready && !CookingAlerts) continue;
                Queue(signal.Cue, signal.Delay);
            }
            if (command.kind == ActionKind.BeginStroke)
            {
                gestureType = command.a; lastX = command.b; lastY = command.c;
                gestureUntil = Time.unscaledTime + .14f;
            }
            else if (command.kind == ActionKind.Stroke)
            {
                int dx = command.a - lastX, dy = command.b - lastY;
                if (dx * dx + dy * dy >= 9 && Point.InCircle(command.a, command.b)) gestureUntil = Time.unscaledTime + .14f;
                lastX = command.a; lastY = command.b;
            }
            else if (command.kind == ActionKind.EndStroke || command.kind == ActionKind.Discard || command.kind == ActionKind.BeginFold)
            { gestureUntil = 0; gestureType = -2; }
        }
        private void Queue(SoundCue cue, float delay = 0)
        {
            FeedbackVersion++;
            if (Muted || worldPaused || suspended) return;
            pending.Add(new Pending { Cue = cue, At = clock + delay });
        }
        public void PlayUi(SoundCue cue)
        {
            FeedbackVersion++;
            if (Muted || suspended || library == null) return;
            if (cue == SoundCue.Error && Time.unscaledTime - lastError < .15f) return;
            if (cue == SoundCue.Error) lastError = Time.unscaledTime;
            var entry = library.Find(cue); var clip = Choose(entry);
            if (clip == null) return;
            ui.volume = EffectsVolume; ui.PlayOneShot(clip, entry.gain); Count(cue);
        }
        private AudioClip Choose(SoundEntry entry)
        {
            if (entry == null || entry.clips == null || entry.clips.Length == 0) return null;
            int index = variants.TryGetValue(entry.cue, out int previous) ? (previous + 1) % entry.clips.Length : 0;
            variants[entry.cue] = index; return entry.clips[index];
        }
        private void PlayWorld(SoundCue cue)
        {
            if (library == null || EffectsVolume <= 0) return;
            var entry = library.Find(cue); var clip = Choose(entry); if (clip == null) return;
            int slot = Array.FindIndex(voices, source => !source.isPlaying);
            if (slot < 0) return; // Preserve existing feedback instead of an unbounded pile of overlapping effects.
            var voice = voices[slot]; voice.clip = clip; voiceGains[slot] = entry.gain;
            voice.volume = entry.gain * EffectsVolume; voice.Play(); Count(cue);
        }
        private void Count(SoundCue cue) { played[cue] = PlayedCount(cue) + 1; }
        private static void Loop(AudioSource source, AudioClip clip, float target, float speed, float dt)
        {
            if (source.clip != clip) { source.Stop(); source.clip = clip; source.volume = 0; }
            if (clip == null) return;
            if (target > .001f && !source.isPlaying) source.Play();
            source.volume = Mathf.MoveTowards(source.volume, target, dt * speed);
            if (target <= .001f && source.volume <= .001f && source.isPlaying) source.Stop();
        }
        private void Update()
        {
            if (controller == null || library == null) return;
            if (settingsDirty && Time.unscaledTime - changedAt > .4f) SavePreferences();
            if (suspended) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .25f);
            if (!worldPaused)
            {
                clock += dt;
                for (int i = 0; i < pending.Count;)
                {
                    if (pending[i].At > clock) { i++; continue; }
                    var item = pending[i]; pending.RemoveAt(i); if (!Muted) PlayWorld(item.Cue);
                }
            }
            ui.volume = Muted ? 0 : EffectsVolume;
            for (int i = 0; i < voices.Length; i++) voices[i].volume = Muted ? 0 : voiceGains[i] * EffectsVolume;
            var s = controller.Session; var p = s?.PanPancake;
            bool frying = p != null && (p.Stage == PancakeStage.SideA || p.Stage == PancakeStage.SideB) && s.CanCook;
            float world = Muted || worldPaused ? 0 : EffectsVolume;
            Loop(fry, library.frying, frying ? world * (kitchen ? .6f : .22f) : 0, 2.5f, dt);
            AudioClip stroke = gestureType == -1 ? library.batter : gestureType == 0 ? library.butter : gestureType == 1 ? library.jam : gestureType == 2 ? library.honey : null;
            var painting = gestureType == -1 ? s?.PanPancake : s?.PlatePancake;
            bool moving = painting != null && s.CanCook && Time.unscaledTime < gestureUntil &&
                (painting.Stage == PancakeStage.Batter || painting.Stage == PancakeStage.Plate);
            Loop(gesture, stroke, moving ? world * .8f : 0, 6, dt);
            bool menu = facade || s == null || s.Phase == DayPhase.Result;
            float music = Muted ? 0 : MusicVolume * (worldPaused && !facade ? .4f : 1);
            Loop(dayMusic, library.musicDay, menu ? 0 : music, .45f, dt);
            Loop(menuMusic, library.musicMenu, menu ? music : 0, .45f, dt);
            float background = Muted || menu ? 0 : AmbienceVolume * (kitchen ? .22f : .55f) * (worldPaused ? .2f : 1);
            Loop(ambience, library.cafe, background, .5f, dt);
        }
        private void OnApplicationQuit() { SavePreferences(); }
        private void OnDestroy()
        {
            SavePreferences();
            if (controller == null) return;
            controller.CommandApplied -= OnCommand; controller.DayStarted -= OnDay;
            controller.PurchaseApplied -= OnPurchase; controller.Changed -= SyncPause;
        }
    }
}
