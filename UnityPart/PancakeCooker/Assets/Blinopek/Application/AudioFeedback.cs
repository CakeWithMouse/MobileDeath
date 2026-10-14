using System.Collections.Generic;
using Blinopek.Domain;

namespace Blinopek.Application
{
    public enum SoundCue
    {
        Button, Select, Back, Toggle, Error, BookOpen, BookClose, BookPage,
        GuestEnter, OrderAccept, Clarify, Flip, Plate, Curd, Mushrooms, Scrape, Fold,
        Discard, Tray, Serve, Positive, Negative, Payment, Tip, Refund,
        DayOpen, DayClose, DaySuccess, DayFailure, Purchase,
        ButterStroke, JamStroke, HoneyStroke, Ready, Burnt, Patience
    }

    public struct SoundSignal
    {
        public readonly SoundCue Cue;
        public readonly float Delay;
        public SoundSignal(SoundCue cue, float delay = 0) { Cue = cue; Delay = delay; }
    }

    // Compares committed states, not refresh notifications. Kept free of Unity for replay/edge tests.
    public sealed class AudioFeedback
    {
        private DaySession observed;
        private int revision, pancakeId, heatA, heatB, revenue, tips, refunds, finished;
        private bool lowPatience;
        private OrderPhase orderPhase;
        private DayPhase dayPhase;

        public AudioFeedback(DaySession session) { Reset(session); }
        public void Reset(DaySession s)
        {
            observed = s;
            if (s == null) return;
            revision = s.Revision; pancakeId = s.PanPancake?.Id ?? -1;
            heatA = s.PanPancake?.HeatA ?? 0; heatB = s.PanPancake?.HeatB ?? 0;
            revenue = s.Ledger.Revenue; tips = s.Ledger.Tips; refunds = s.Ledger.Refunds;
            finished = s.FinishedCount; orderPhase = s.Order.Phase; dayPhase = s.Phase;
            lowPatience = IsLow(s);
        }
        private static bool IsLow(DaySession s) => !s.IsTutorial && s.HasActiveOrder &&
            (long)s.Order.WaitTicks * 5 >= (long)s.Order.PatienceLimit * 4;

        public List<SoundSignal> AfterCommand(DaySession s, GameCommand command)
        {
            var signals = new List<SoundSignal>();
            if (s == null || s != observed) { Reset(s); return signals; }
            if (s.Revision == revision) return signals;
            switch (command.kind)
            {
                case ActionKind.Accept: signals.Add(new SoundSignal(SoundCue.OrderAccept)); break;
                case ActionKind.Clarify: signals.Add(new SoundSignal(SoundCue.Clarify)); break;
                case ActionKind.Flip: signals.Add(new SoundSignal(SoundCue.Flip)); break;
                case ActionKind.MoveToPlate: signals.Add(new SoundSignal(SoundCue.Plate)); break;
                case ActionKind.Portion: signals.Add(new SoundSignal(command.a == 3 ? SoundCue.Curd : SoundCue.Mushrooms)); break;
                case ActionKind.Remove: signals.Add(new SoundSignal(SoundCue.Scrape)); break;
                case ActionKind.Discard: signals.Add(new SoundSignal(SoundCue.Discard)); break;
                case ActionKind.BeginFold: signals.Add(new SoundSignal(SoundCue.Select)); break;
                case ActionKind.FoldGesture:
                case ActionKind.FinalizeFold: signals.Add(new SoundSignal(SoundCue.Fold)); break;
                case ActionKind.NextGuest: signals.Add(new SoundSignal(SoundCue.GuestEnter)); break;
                case ActionKind.ApplyHelper:
                    // One short sequence, not one simultaneous sound per simulation stamp/portion.
                    var cue = command.a == 0 ? SoundCue.ButterStroke : command.a == 1 ? SoundCue.JamStroke :
                        command.a == 2 ? SoundCue.HoneyStroke : command.a == 3 ? SoundCue.Curd : SoundCue.Mushrooms;
                    signals.Add(new SoundSignal(cue)); signals.Add(new SoundSignal(cue, .13f));
                    signals.Add(new SoundSignal(cue, .27f)); break;
            }
            bool served = s.Order.Phase == OrderPhase.Served && orderPhase != OrderPhase.Served;
            bool cancelled = s.Order.Phase == OrderPhase.Cancelled && orderPhase != OrderPhase.Cancelled;
            if (served)
            {
                signals.Add(new SoundSignal(SoundCue.Serve, .65f));
                signals.Add(new SoundSignal(s.Order.Report.Total >= 65 ? SoundCue.Positive : SoundCue.Negative, 1.15f));
            }
            else if (cancelled) signals.Add(new SoundSignal(SoundCue.Negative));
            else if (s.FinishedCount > finished) signals.Add(new SoundSignal(SoundCue.Tray, .65f));
            if (s.Ledger.Revenue > revenue) signals.Add(new SoundSignal(SoundCue.Payment, served ? 1.4f : .18f));
            if (s.Ledger.Tips > tips) signals.Add(new SoundSignal(SoundCue.Tip, 1.55f));
            if (s.Ledger.Refunds > refunds) signals.Add(new SoundSignal(SoundCue.Refund, served ? 1.4f : .3f));
            if (s.Phase != dayPhase)
            {
                if (s.Phase == DayPhase.Closing) signals.Add(new SoundSignal(SoundCue.DayClose));
                else if (s.Phase == DayPhase.Result)
                    signals.Add(new SoundSignal(s.Success ? SoundCue.DaySuccess : SoundCue.DayFailure, served ? 2.1f : .4f));
            }
            var p = s.PanPancake;
            if (p != null && p.Id == pancakeId && command.kind == ActionKind.Tick)
            {
                int oldHeat = p.Stage == PancakeStage.SideA ? heatA : heatB;
                int heat = p.Stage == PancakeStage.SideA ? p.HeatA : p.HeatB;
                if (p.Stage == PancakeStage.SideA || p.Stage == PancakeStage.SideB)
                {
                    if (oldHeat < s.Rules.burnTicks && heat >= s.Rules.burnTicks) signals.Add(new SoundSignal(SoundCue.Burnt));
                    else if (oldHeat < s.Rules.readyTicks && heat >= s.Rules.readyTicks && (!s.IsLessonPancake(p) || s.Tutorial != TutorialStep.Demonstration))
                        signals.Add(new SoundSignal(SoundCue.Ready));
                }
            }
            if (!lowPatience && IsLow(s)) signals.Add(new SoundSignal(SoundCue.Patience));
            Reset(s); return signals;
        }
    }
}
