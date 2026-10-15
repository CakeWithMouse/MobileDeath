using System;
using System.Collections.Generic;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;

namespace Blinopek.Tests
{
    public static partial class CoreScenarios
    {
        private static void Prepare(DaySession s, PortionSpec recipe)
        { Plate(s); Fill(s, recipe); FoldPancake(s, recipe.Shape); }

        private static void CookBeforeOrder()
        {
            var s = Session();
            Check(s.CanCook && !s.HasActiveOrder, "Kitchen requires an order");
            Prepare(s, s.Order.Portions[0]);
            Check(s.ReadyPancakes.Count == 1 && s.TrayCount == 1 && s.Pancake == null, "Prepared pancake lost");
            Check(s.Ledger.Products > 0 && s.Ledger.Revenue == 0 && s.Reviews.Count == 0 && s.Order.Phase == OrderPhase.Offered, "Preparation paid for or served an unaccepted order");
            Check(Do(s, ActionKind.Accept) && s.Order.Phase == OrderPhase.Accepted && s.CanServeOrder && s.Reviews.Count == 0, "Acceptance served stock automatically");
            Check(Do(s, ActionKind.ServeOrder) && s.Order.Report.Total >= 95, "Prepared order was not evaluated normally");
            Check(s.ReadyPancakes.Count == 0 && s.TrayCount == 0 && s.Ledger.Revenue == s.Order.Price, "Prepared order paid twice or remained in stock");
            Check(Do(s, ActionKind.BeginStroke, -1, 500, 500), "Cannot pour between guests");
            Do(s, ActionKind.EndStroke); Do(s, ActionKind.FinishBatter); Do(s, ActionKind.Tick, 1);
            var p = s.Pancake;
            Check(Do(s, ActionKind.NextGuest) && s.Pancake == p && p.HeatA == 1, "Next visitor replaced the frying pancake");
            Do(s, ActionKind.Tick, 2);
            Check(p.HeatA == 3 && !s.HasActiveOrder, "Pan stops frying while the next visitor waits");
            Check(Do(s, ActionKind.Discard) && s.Pancake == null, "Cannot discard without an order");
        }

        private static void ServePreparedStock()
        {
            var s = Session(); var recipe = s.Order.Portions[0];
            for (int i = 0; i < 3; i++) Prepare(s, recipe);
            var first = s.ReadyPancakes[0]; var second = s.ReadyPancakes[1];
            Plate(s); var active = s.Pancake; int products = s.Ledger.Products;
            Do(s, ActionKind.Accept);
            Check(Do(s, ActionKind.ServeOrder), "Cannot serve prepared stock explicitly");
            Check(s.Order.Phase == OrderPhase.Served && s.Order.Finished[0] == first && s.Reviews.Count == 1, "Stock is not served once in preparation order");
            Check(s.ReadyPancakes.Count == 0 && s.Order.Finished.Count == 3 && s.Order.Finished[1] == second && s.Pancake == active, "Serving did not hand over the whole tray or erased active cooking");
            Check(s.Ledger.Products == products && !Do(s, ActionKind.Accept), "Serving spent ingredients or accepted twice");
            Fill(s, recipe); FoldPancake(s, recipe.Shape);
            Check(s.TrayCount == 1 && s.Reviews.Count == 1, "Cooking after serving duplicated a review or returned delivered stock");
        }

        private static void KeepCancelledCooking()
        {
            var profile = Profile(); profile.day = 3;
            var rules = Rules(); rules.dayTicks = 10000;
            DaySession s = null;
            for (int seed = 1; seed < 100; seed++) { s = Session(profile, rules, seed); if (s.Order.Portions.Count > 1) break; }
            Check(s.Order.Portions.Count > 1, "No multi order");
            Do(s, ActionKind.Accept); Prepare(s, s.Order.Portions[0]);
            var ready = s.Order.Finished[0];
            Do(s, ActionKind.BeginStroke, -1, 200, 500); var active = s.Pancake;
            Do(s, ActionKind.Tick, s.Order.PatienceLimit);
            Check(s.Order.Phase == OrderPhase.Cancelled && s.Pancake == active && s.Order.Finished.Count == 0 && s.ReadyPancakes.Count == 1 && s.ReadyPancakes[0] == ready, "Cancellation lost or duplicated portions");
            int cost = s.Ledger.Products, refunds = s.Ledger.Refunds;
            Check(Do(s, ActionKind.Stroke, 800, 500) && s.Ledger.Products > cost, "Cancellation interrupted the held pour");
            Do(s, ActionKind.EndStroke); Do(s, ActionKind.FinishBatter); Do(s, ActionKind.Tick, 1);
            Check(active.HeatA == 1 && s.Ledger.Refunds == refunds, "Frying stopped or refund repeated");
            Check(Do(s, ActionKind.NextGuest) && Do(s, ActionKind.Accept), "Next guest cannot receive retained stock");
            Check(s.Pancake == active && s.Order.Finished.Count == 1 && s.Order.Finished[0] == ready && s.ReadyPancakes.Count == 0, "Next guest lost the returned portion or the pan");
        }

        private static void FreeCookingBeforeLesson()
        {
            var s = Session(Profile(true));
            Paint(c => s.Execute(c), -1); Do(s, ActionKind.FinishBatter); Do(s, ActionKind.Tick, 2);
            var p = s.Pancake;
            Check(!p.IsLesson && p.HeatA == 2 && s.Ledger.Products > 0 && s.Ledger.Revenue == 0, "Free preparation inherited tutorial subsidies or heat");
            Do(s, ActionKind.Accept); Do(s, ActionKind.Tick, 6);
            Check(s.Pancake == p && !s.IsCookingLesson && p.HeatA == 8, "Acceptance turned an existing pancake into a protected lesson");
            Check(Do(s, ActionKind.Flip), "Free pancake blocked by demonstration");
            Do(s, ActionKind.Tick, 2); Do(s, ActionKind.MoveToPlate);
            Check(Do(s, ActionKind.BeginFold, (int)Fold.Roll) && Do(s, ActionKind.FinalizeFold), "Free pancake forced to use tutorial recipe");
            Check(Do(s, ActionKind.ServeOrder), "Free pancake cannot be served to the tutorial guest");
            Check(s.TutorialCompleted && s.Order.Report.Total < 95, "Free pancake escaped normal quality evaluation");
            var lesson = Session(Profile(true)); Do(lesson, ActionKind.Accept);
            Check(Do(lesson, ActionKind.BeginStroke, -1, 500, 500) && lesson.IsCookingLesson && lesson.Ledger.Products == 0, "Accepted lesson lost its protections");
        }

        private static void FreeCookingReplay()
        {
            var store = new MemoryStore { Data = new SavedGame { profile = Profile(), rules = Rules() } };
            var game = new GameController(store, new LocalDevelopmentGateway(), Rules()); game.StartDay(7);
            Action<GameCommand> send = c => Check(game.Dispatch(c), "Live command failed: " + c.kind);
            Paint(send, -1); send(new GameCommand(ActionKind.FinishBatter)); send(new GameCommand(ActionKind.Tick, 2));
            send(new GameCommand(ActionKind.Flip)); send(new GameCommand(ActionKind.Tick, 2)); send(new GameCommand(ActionKind.MoveToPlate));
            send(new GameCommand(ActionKind.BeginFold)); send(new GameCommand(ActionKind.FoldGesture, 0, 400)); send(new GameCommand(ActionKind.FoldGesture, 400, 0));
            Paint(send, -1); send(new GameCommand(ActionKind.FinishBatter)); send(new GameCommand(ActionKind.Tick, 3)); game.Save();
            var restored = new GameController(store, new LocalDevelopmentGateway(), Rules());
            Check(restored.Session.FreeCooking && restored.Session.ReadyPancakes.Count == 1 && restored.Session.Pancake.HeatA == 3, "Replay lost stock or heat");
            Check(restored.Session.Balance == game.Session.Balance && restored.Session.Order.Phase == OrderPhase.Offered && restored.Session.FinishedCount == 1, "Replay changed costs or served an order");
            Check(store.Data.commands.FindAll(c => c.kind == ActionKind.EnableFreeCooking).Count == 1, "Replay inserted duplicate transitions");
        }

        private static void LegacyCookingReplay()
        {
            var rules = Rules(); rules.dayTicks = 10000;
            var profile = Profile(); var legacy = new DaySession(profile, rules, 7, false, false);
            var commands = new List<GameCommand>();
            Action<GameCommand> send = c => { Check(legacy.Execute(c), "Legacy command failed: " + c.kind); commands.Add(c.Copy()); };
            send(new GameCommand(ActionKind.Accept)); Paint(send, -1); send(new GameCommand(ActionKind.FinishBatter));
            send(new GameCommand(ActionKind.Tick, legacy.Order.PatienceLimit));
            Check(legacy.Pancake == null, "Legacy timeout must clear the pan");
            send(new GameCommand(ActionKind.Tick, 1)); send(new GameCommand(ActionKind.NextGuest)); send(new GameCommand(ActionKind.Accept));
            Paint(send, -1); send(new GameCommand(ActionKind.FinishBatter)); send(new GameCommand(ActionKind.Tick, 1));
            var store = new MemoryStore { Data = new SavedGame { profile = profile.Copy(), sessionStart = profile.Copy(), seed = 7, rules = rules, commands = commands } };
            var restored = new GameController(store, new LocalDevelopmentGateway(), rules);
            Check(restored.Session.FreeCooking && restored.Session.Balance == legacy.Balance && restored.Session.Pancake.HeatA == 1 && restored.Session.Reviews.Count == 1, "Old history changed during migration");
            restored.Dispatch(new GameCommand(ActionKind.Tick, restored.Session.Order.PatienceLimit - restored.Session.Order.WaitTicks));
            Check(restored.Session.Pancake != null && restored.Session.Order.Phase == OrderPhase.Cancelled, "Migration did not preserve new cooking on timeout");
            restored.Dispatch(new GameCommand(ActionKind.Tick, rules.guestIntervalTicks));
            Check(restored.Session.Order.Id == 3 && restored.Session.Order.Phase == OrderPhase.Offered && restored.Session.Pancake != null, "Migrated session did not continue to the next guest automatically");
            var again = new GameController(store, new LocalDevelopmentGateway(), rules);
            Check(again.Session.Pancake.HeatA == restored.Session.Pancake.HeatA && again.Session.Balance == restored.Session.Balance && again.Session.Reviews.Count == 2, "Mixed old/new history replays differently");
            Check(store.Data.commands.FindAll(c => c.kind == ActionKind.EnableFreeCooking).Count == 1, "Migration repeated");
        }

        private static void ClosePreparedKitchen()
        {
            var rules = Rules(); rules.dayTicks = 20; var s = Session(null, rules);
            Prepare(s, s.Order.Portions[0]); Do(s, ActionKind.BeginStroke, -1, 500, 500);
            int products = s.Ledger.Products; Do(s, ActionKind.Tick, 20);
            Check(s.Phase == DayPhase.Result && s.ReadyPancakes.Count == 0 && s.Pancake == null && !s.CanCook, "Closing left the kitchen active");
            Check(s.Ledger.Products == products && s.Ledger.Revenue == 0 && s.Reviews.Count == 0, "Unserved stock was refunded or paid for");
            Check(!Do(s, ActionKind.BeginStroke, -1, 500, 500) && !Do(s, ActionKind.Accept), "Cooking continues past the end of the shift");
        }

        private static void FreeCookingAudio()
        {
            var s = Session(Profile(true)); Paint(c => s.Execute(c), -1); Do(s, ActionKind.FinishBatter);
            var audio = new AudioFeedback(s); var tick = new GameCommand(ActionKind.Tick, 2);
            s.Execute(tick); Check(Count(audio.AfterCommand(s, tick), SoundCue.Ready) == 1, "Free cooking has no ready sound before the lesson");
            Do(s, ActionKind.Flip); Do(s, ActionKind.Tick, 2); Do(s, ActionKind.MoveToPlate); Do(s, ActionKind.BeginFold);
            Do(s, ActionKind.FoldGesture, 0, 400); audio.Reset(s);
            var fold = new GameCommand(ActionKind.FoldGesture, 400, 0); s.Execute(fold);
            var sounds = audio.AfterCommand(s, fold);
            Check(Count(sounds, SoundCue.Tray) == 1 && Count(sounds, SoundCue.Serve) == 0 && Count(sounds, SoundCue.Payment) == 0, "Free cooking played payment or omitted tray sound");
        }
    }
}
