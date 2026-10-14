using System;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;

namespace Blinopek.Tests
{
    public static partial class CoreScenarios
    {
        private static void FryWhileFolding()
        {
            var s = Session(); Do(s, ActionKind.Accept); Plate(s); Fill(s, s.Order.Portions[0]);
            var first = s.PlatePancake; int heatA = first.HeatA, heatB = first.HeatB;
            Do(s, ActionKind.BeginFold); Do(s, ActionKind.FoldGesture, 0, 400);
            Check(s.PanPancake == null && first.Folds.Count == 1, "Moving to the plate did not free the pan");
            Paint(c => s.Execute(c), -1); var second = s.PanPancake;
            Check(second != null && second != first && second.Id != first.Id, "Second pour reused the plate pancake");
            Check(Do(s, ActionKind.FinishBatter), "Second pancake cannot start frying");
            var audio = new AudioFeedback(s); var tick = new GameCommand(ActionKind.Tick, s.Rules.readyTicks);
            s.Execute(tick); Check(Count(audio.AfterCommand(s, tick), SoundCue.Ready) == 1, "Folding hid the second pancake's ready sound");
            Do(s, ActionKind.Flip); Do(s, ActionKind.Tick, s.Rules.readyTicks);
            Check(!Do(s, ActionKind.MoveToPlate) && s.PanPancake == second && s.PlatePancake == first, "A cooked pancake overwrote the occupied plate");
            Check(first.HeatA == heatA && first.HeatB == heatB && first.Folds.Count == 1, "Pan heat or gestures modified the plate");
            Check(Do(s, ActionKind.FoldGesture, 400, 0), "Cannot finish folding while pan is occupied");
            Check(Do(s, ActionKind.ServeOrder), "Cannot serve while the next pancake fries");
            Check(s.Order.Phase == OrderPhase.Served && s.Order.Finished[0] == first && s.PanPancake == second && s.PlatePancake == null, "Serving first pancake erased or served the second");
            Check(Do(s, ActionKind.MoveToPlate) && s.PlatePancake == second && s.PanPancake == null, "Freed plate is unusable");
            Check(Do(s, ActionKind.BeginStroke, -1, 500, 500) && s.PanPancake.Id != second.Id, "Cannot pour third pancake immediately");
        }

        private static void FillWhileFrying()
        {
            var profile = Profile(); profile.helpersOwned = profile.helpersEnabled = 1;
            var s = Session(profile); Plate(s); var first = s.PlatePancake;
            Paint(c => s.Execute(c), -1); Do(s, ActionKind.FinishBatter); var second = s.PanPancake;
            int batter = second.Batter.Count;
            Paint(c => s.Execute(c), 1); Check(Do(s, ActionKind.ApplyHelper, 0), "Plate helper blocked by frying");
            Check(Do(s, ActionKind.Portion, 3, 320, 320) && Do(s, ActionKind.Remove, 1), "Plate tools blocked by frying");
            Check(first.IngredientBits == 9 && second.IngredientBits == 0 && second.Batter.Count == batter, "Fillings or scraper affected the pan");
            int firstHeat = first.HeatA; Do(s, ActionKind.Tick, s.Rules.burnTicks + 2);
            Check(second.HeatA == s.Rules.burnTicks + 2 && first.HeatA == firstHeat, "Heat did not stay with the frying pancake");
        }

        private static void DiscardOneStation()
        {
            var s = Session(); Plate(s); var first = s.PlatePancake;
            Do(s, ActionKind.BeginStroke, -1, 200, 500); var second = s.PanPancake;
            Check(Do(s, ActionKind.Discard, first.Id) && s.PlatePancake == null && s.PanPancake == second, "Discarding plate removed pan");
            Check(Do(s, ActionKind.Stroke, 800, 500), "Discarding plate interrupted pouring on pan");
            Check(!Do(s, ActionKind.Discard, first.Id) && s.PanPancake == second, "Stale drop discarded another pancake");
            Do(s, ActionKind.EndStroke); Do(s, ActionKind.FinishBatter); Do(s, ActionKind.Tick, 2); Do(s, ActionKind.Flip); Do(s, ActionKind.Tick, 2); Do(s, ActionKind.MoveToPlate);
            Do(s, ActionKind.BeginStroke, -1, 500, 500); var third = s.PanPancake;
            Check(Do(s, ActionKind.Discard, third.Id) && s.PanPancake == null && s.PlatePancake == second, "Discarding pan removed plate");
            Check(!Do(s, ActionKind.Stroke, 700, 500), "Discarded pan retained its gesture");
        }

        private static void ConcurrentTimeout()
        {
            var rules = Rules(); rules.dayTicks = 1200;
            var s = Session(null, rules); Do(s, ActionKind.Accept); Plate(s); var first = s.PlatePancake;
            Paint(c => s.Execute(c), -1); Do(s, ActionKind.FinishBatter); var second = s.PanPancake;
            Do(s, ActionKind.Tick, s.Order.PatienceLimit);
            Check(s.Order.Phase == OrderPhase.Cancelled && s.PlatePancake == first && s.PanPancake == second && second.HeatA >= s.Rules.burnTicks, "Timeout lost a pancake or stopped frying");
            int refund = s.Ledger.Refunds;
            Do(s, ActionKind.Tick, rules.dayTicks);
            Check(s.Phase == DayPhase.Result && s.PlatePancake == null && s.PanPancake == null && s.Ledger.Refunds == refund, "Closing left a station active or refunded twice");
        }

        private static void ConcurrentReplay()
        {
            var store = new MemoryStore { Data = new SavedGame { profile = Profile(), rules = Rules() } };
            var game = new GameController(store, new LocalDevelopmentGateway(), Rules()); game.StartDay(17);
            Action<GameCommand> send = c => Check(game.Dispatch(c), "Parallel command failed: " + c.kind);
            Paint(send, -1); send(new GameCommand(ActionKind.FinishBatter)); send(new GameCommand(ActionKind.Tick, 2));
            send(new GameCommand(ActionKind.Flip)); send(new GameCommand(ActionKind.Tick, 2)); send(new GameCommand(ActionKind.MoveToPlate));
            send(new GameCommand(ActionKind.BeginFold)); send(new GameCommand(ActionKind.FoldGesture, 0, 400));
            send(new GameCommand(ActionKind.BeginStroke, -1, 200, 500)); send(new GameCommand(ActionKind.Stroke, 800, 500)); game.Save();
            var restored = new GameController(store, new LocalDevelopmentGateway(), Rules());
            Check(restored.Session.PlatePancake.Folds.Count == 1 && restored.Session.PlatePancake.HeatB == 2 && restored.Session.PanPancake.Batter.Count == game.Session.PanPancake.Batter.Count && restored.Session.Balance == game.Session.Balance, "Replay mixed the two pancakes");
            Check(!restored.Dispatch(new GameCommand(ActionKind.Stroke, 500, 500)), "Restore retained pointer capture");
            Check(restored.Dispatch(new GameCommand(ActionKind.FinishBatter)), "Restored pan cannot fry");
            restored.Dispatch(new GameCommand(ActionKind.Tick, 3));
            Check(restored.Dispatch(new GameCommand(ActionKind.FoldGesture, 400, 0)) && restored.Session.PanPancake.HeatA == 3 && restored.Session.ReadyPancakes.Count == 1, "Completing restored plate cleared the pan");
            var again = new GameController(store, new LocalDevelopmentGateway(), Rules());
            Check(again.Session.PanPancake.HeatA == 3 && again.Session.ReadyPancakes.Count == 1 && again.Session.PlatePancake == null, "Second replay lost independent station state");
        }

        private static void ConcurrentLesson()
        {
            var profile = Profile(true); profile.demonstrationSeen = true;
            var s = Session(profile); Do(s, ActionKind.Accept); Plate(s); var lesson = s.PlatePancake;
            Paint(c => s.Execute(c), -1); var ordinary = s.PanPancake; int products = s.Ledger.Products;
            Check(lesson.IsLesson && !ordinary.IsLesson && products > 0, "Second pancake inherited tutorial subsidy");
            Fill(s, s.Order.Portions[0]); Check(s.Ledger.Products == products && ordinary.IngredientBits == 0, "Lesson filling charged money or landed on second pancake");
            Do(s, ActionKind.FinishBatter); Do(s, ActionKind.Tick, 20); Do(s, ActionKind.BeginFold);
            Check(ordinary.HeatA == 20 && lesson.HeatA == s.Rules.readyTicks && !Do(s, ActionKind.FoldGesture, -400, 0), "Parallel frying changed tutorial guards or heat");
            Do(s, ActionKind.FoldGesture, 0, 400); Do(s, ActionKind.FoldGesture, 400, 0);
            Check(Do(s, ActionKind.ServeOrder), "Cannot serve the lesson while the next pancake fries");
            Check(s.TutorialCompleted && s.Order.Phase == OrderPhase.Served && s.PanPancake == ordinary && s.Ledger.Products == products, "Lesson completion erased or subsidized second pancake");
        }
    }
}
