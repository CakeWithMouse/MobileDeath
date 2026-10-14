using System;
using System.Collections.Generic;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;

namespace Blinopek.Tests
{
    public static partial class CoreScenarios
    {
        private static int Count(List<SoundSignal> sounds, SoundCue cue) => sounds.FindAll(s => s.Cue == cue).Count;
        private static void AudioCommitted()
        {
            var store = new MemoryStore { Data = new SavedGame { profile = Profile(), rules = Rules() } };
            var game = new GameController(store, new LocalDevelopmentGateway(), Rules()); game.StartDay(7);
            var feedback = new AudioFeedback(game.Session); var sounds = new List<SoundSignal>();
            game.CommandApplied += c => sounds.AddRange(feedback.AfterCommand(game.Session, c));
            Check(!game.Dispatch(new GameCommand(ActionKind.Flip)) && sounds.Count == 0, "Rejected action made sound");
            game.Dispatch(new GameCommand(ActionKind.Accept));
            Check(Count(sounds, SoundCue.Payment) == 1 && Count(sounds, SoundCue.OrderAccept) == 1, "Acceptance audio missing");
            game.Dispatch(new GameCommand(ActionKind.Accept));
            Check(Count(sounds, SoundCue.Payment) == 1, "Duplicate acceptance audio");
            game.SetPause(PauseReason.Menu, true); int count = sounds.Count;
            game.Dispatch(new GameCommand(ActionKind.Tick, 1000)); Check(sounds.Count == count, "Paused command made sound");
            var restored = new GameController(store, new LocalDevelopmentGateway(), Rules());
            var restoredAudio = new AudioFeedback(restored.Session); sounds.Clear();
            restored.CommandApplied += c => sounds.AddRange(restoredAudio.AfterCommand(restored.Session, c));
            restored.Dispatch(new GameCommand(ActionKind.Tick, 1));
            Check(Count(sounds, SoundCue.Payment) == 0 && Count(sounds, SoundCue.OrderAccept) == 0, "Load replayed payment");
        }
        private static void AudioReceipts()
        {
            var s = Session(); Do(s, ActionKind.Accept); Plate(s); Fill(s, s.Order.Portions[0]);
            var audio = new AudioFeedback(s); var sounds = new List<SoundSignal>();
            Action<GameCommand> send = c => { if (s.Execute(c)) sounds.AddRange(audio.AfterCommand(s, c)); };
            send(new GameCommand(ActionKind.BeginFold, (int)s.Order.Portions[0].Shape));
            foreach (var d in Recipes.Gestures(s.Order.Portions[0].Shape))
                send(new GameCommand(ActionKind.FoldGesture, d == Direction.Left ? -400 : d == Direction.Right ? 400 : 0, d == Direction.Down ? -400 : d == Direction.Up ? 400 : 0));
            Check(Count(sounds, SoundCue.Serve) == 0 && Count(sounds, SoundCue.Tray) == 1, "Finishing must only play tray audio");
            send(new GameCommand(ActionKind.ServeOrder));
            Check(Count(sounds, SoundCue.Serve) == 1 && Count(sounds, SoundCue.Tip) == 1 && Count(sounds, SoundCue.Positive) == 1, "Successful serving audio");
            send(new GameCommand(ActionKind.FinalizeFold)); send(new GameCommand(ActionKind.ServeOrder)); send(new GameCommand(ActionKind.Tick, 1));
            Check(Count(sounds, SoundCue.Serve) == 1 && Count(sounds, SoundCue.Tip) == 1, "Repeated serving audio");
            var expired = Session(); Do(expired, ActionKind.Accept); audio.Reset(expired);
            var tick = new GameCommand(ActionKind.Tick, expired.Order.PatienceLimit);
            expired.Execute(tick); sounds = audio.AfterCommand(expired, tick);
            Check(Count(sounds, SoundCue.Refund) == 1 && Count(sounds, SoundCue.Negative) == 1, "Timeout refund audio");
            expired.Execute(new GameCommand(ActionKind.Tick, 1));
            Check(Count(audio.AfterCommand(expired, tick), SoundCue.Refund) == 0, "Duplicate refund audio");
        }
        private static void AudioThresholds()
        {
            var s = Session(); Do(s, ActionKind.Accept); Paint(c => s.Execute(c), -1); Do(s, ActionKind.FinishBatter);
            var audio = new AudioFeedback(s); var sounds = new List<SoundSignal>();
            foreach (int ticks in new[] { 2, 1, 3, 1, 793, 1 }) {
                var c = new GameCommand(ActionKind.Tick, ticks); s.Execute(c); sounds.AddRange(audio.AfterCommand(s, c));
            }
            Check(Count(sounds, SoundCue.Ready) == 1 && Count(sounds, SoundCue.Burnt) == 1 && Count(sounds, SoundCue.Patience) == 1, "Repeated/missing threshold sound");
            Check(audio.AfterCommand(s, new GameCommand(ActionKind.Tick, 1)).Count == 0, "Same revision emitted sound");
        }
        private static void AudioHelper()
        {
            var profile = Profile(); profile.helpersOwned = profile.helpersEnabled = 31;
            var s = Session(profile); Do(s, ActionKind.Accept); Plate(s); var audio = new AudioFeedback(s);
            var c = new GameCommand(ActionKind.ApplyHelper, 0); Check(s.Execute(c), "Helper rejected");
            var sounds = audio.AfterCommand(s, c);
            Check(sounds.Count == 3 && sounds[0].Cue == SoundCue.ButterStroke && sounds[1].Delay > 0 && sounds[2].Delay > sounds[1].Delay, "Unbounded or simultaneous helper sounds");
        }
    }
}
