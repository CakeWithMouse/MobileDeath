using System;
using System.Collections.Generic;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;

namespace Blinopek.Tests
{
    public static partial class CoreScenarios
    {
        private static void DiscardReadyPancake()
        {
            var s = Session(); var recipe = s.Order.Portions[0];
            Prepare(s, recipe); var first = s.ReadyPancakes[0];
            Prepare(s, recipe); var second = s.LastFinished;
            Plate(s); var plate = s.PlatePancake;
            Do(s, ActionKind.BeginStroke, -1, 200, 500); var pan = s.PanPancake;
            int products = s.Ledger.Products;
            Check(Do(s, ActionKind.Discard, first.Id), "Cannot discard a finished pancake before acceptance");
            Check(s.ReadyPancakes.Count == 1 && s.ReadyPancakes[0] == second && s.PlatePancake == plate && s.PanPancake == pan, "Discard removed the wrong pancake or station");
            Check(s.Ledger.Products == products && s.Ledger.Revenue == 0, "Discard refunded ingredients or paid for stock");
            Check(Do(s, ActionKind.Stroke, 800, 500), "Discard interrupted the active pour");
            Check(!Do(s, ActionKind.Discard, first.Id) && s.ReadyPancakes.Count == 1, "Stale discard removed another pancake");
            Check(Do(s, ActionKind.Discard, second.Id) && s.LastFinished == null && s.TrayCount == 0, "Last discarded pancake can reappear in its animation");
        }

        private static void ReplaceReadyPortion()
        {
            var s = Session();
            Check(!Do(s, ActionKind.ServeOrder), "Served before acceptance");
            Do(s, ActionKind.Accept); Check(s.CanServeOrder, "Empty accepted order cannot be handed over");
            Prepare(s, s.Order.Portions[0]); var unwanted = s.Order.Finished[0];
            Check(s.CanServeOrder && s.Reviews.Count == 0 && s.Ledger.Tips == 0, "Ready order completed automatically");
            int paid = s.Ledger.Revenue, products = s.Ledger.Products;
            Check(Do(s, ActionKind.Discard, unwanted.Id) && s.CanServeOrder && s.Order.Finished.Count == 0, "Discard blocked serving or still counts towards the order");
            Check(s.Ledger.Revenue == paid && s.Ledger.Products == products, "Discard changed the receipt");
            Prepare(s, s.Order.Portions[0]); var replacement = s.Order.Finished[0];
            Check(replacement.Id != unwanted.Id && Do(s, ActionKind.ServeOrder), "Replacement could not be served");
            Check(!Do(s, ActionKind.Discard, replacement.Id) && s.Order.Finished[0] == replacement, "Already delivered food was discarded");
        }

        private static void IncompleteOrderServing()
        {
            foreach (int portions in new[] { 0, 1 }) {
                var profile = Profile(); profile.day = 3;
                var rules = Rules(); int seed = 1;
                while (Session(profile, rules, seed).Order.Portions.Count < 2) seed++;
                var store = new MemoryStore { Data = new SavedGame { profile = profile, rules = rules } };
                var game = new GameController(store, new LocalDevelopmentGateway(), rules); game.StartDay(seed);
                Check(game.Dispatch(new GameCommand(ActionKind.Accept)), "Cannot accept the partial-order fixture");
                if (portions > 0) PrepareWithCommands(c => Check(game.Dispatch(c), "Partial-order cooking failed: " + c.kind));
                var s = game.Session;
                Check(s.CanServeOrder && s.TrayCount == portions && s.Order.Portions.Count > portions, "Incomplete order is blocked");
                Check(game.Dispatch(new GameCommand(ActionKind.ServeOrder)), "Cannot intentionally hand over an incomplete order");
                var report = s.Order.Report;
                Check(s.Order.Phase == OrderPhase.Served && s.Order.Finished.Count == portions && s.TrayCount == 0, "Serving fabricated or retained food");
                Check(report.Quantity == portions * 100 / s.Order.Portions.Count && report.Total < 65 && report.Refund == s.Order.Price && report.Tips == 0, "Missing portions escaped quality evaluation");
                if (portions == 0) Check(report.Total == 0, "Empty tray received a positive score");
                var restored = new GameController(store, new LocalDevelopmentGateway(), rules);
                Check(restored.Session.Order.Phase == OrderPhase.Served && restored.Session.Order.Report.Total == report.Total && restored.Session.Balance == s.Balance && restored.Session.Reviews.Count == 1, "Incomplete delivery did not replay exactly once");
                Check(!restored.Dispatch(new GameCommand(ActionKind.ServeOrder)) && restored.Session.Reviews.Count == 1 && restored.Session.Ledger.Refunds == report.Refund, "Incomplete delivery refunded twice");
            }
        }

        private static void ReadyOrderTimeout()
        {
            var s = Session(); Do(s, ActionKind.Accept); Prepare(s, s.Order.Portions[0]);
            var ready = s.Order.Finished[0];
            Do(s, ActionKind.Tick, s.Order.PatienceLimit);
            Check(s.Order.Phase == OrderPhase.Cancelled && !s.CanServeOrder && !Do(s, ActionKind.ServeOrder), "Ready order ignored patience or served after cancellation");
            Check(s.ReadyPancakes.Count == 1 && s.ReadyPancakes[0] == ready && s.Reviews.Count == 1 && s.Ledger.Refunds == s.Order.Price, "Timeout lost prepared food or duplicated the refund");
            Check(Do(s, ActionKind.Discard, ready.Id) && s.TrayCount == 0, "Cannot discard after the guest leaves");
        }

        private static void PrepareWithCommands(Action<GameCommand> send)
        {
            Paint(send, -1); send(new GameCommand(ActionKind.FinishBatter)); send(new GameCommand(ActionKind.Tick, 2));
            send(new GameCommand(ActionKind.Flip)); send(new GameCommand(ActionKind.Tick, 2)); send(new GameCommand(ActionKind.MoveToPlate));
            Paint(send, 0); send(new GameCommand(ActionKind.BeginFold));
            send(new GameCommand(ActionKind.FoldGesture, 0, 400)); send(new GameCommand(ActionKind.FoldGesture, 400, 0));
        }

        private static void ManualServingReplay()
        {
            var store = new MemoryStore { Data = new SavedGame { profile = Profile(), rules = Rules() } };
            var game = new GameController(store, new LocalDevelopmentGateway(), Rules()); game.StartDay(7);
            Action<GameCommand> send = c => Check(game.Dispatch(c), "Manual serving command failed: " + c.kind);
            send(new GameCommand(ActionKind.Accept)); PrepareWithCommands(send);
            int first = game.Session.Order.Finished[0].Id;
            send(new GameCommand(ActionKind.Discard, first)); PrepareWithCommands(send);
            int replacement = game.Session.Order.Finished[0].Id, balance = game.Session.Balance;
            var restored = new GameController(store, new LocalDevelopmentGateway(), Rules());
            Check(restored.Session.ManualServing && restored.Session.CanServeOrder && restored.Session.Order.Phase == OrderPhase.Accepted && restored.Session.Reviews.Count == 0, "Restoring served an order");
            Check(restored.Session.Order.Finished[0].Id == replacement && restored.Session.FindTrayPancake(first) == null && restored.Session.Balance == balance, "Replay restored discarded food or changed costs");
            restored.SetPause(PauseReason.Menu, true);
            Check(!restored.Dispatch(new GameCommand(ActionKind.ServeOrder)), "Paused game delivered the order");
            restored.SetPause(PauseReason.Menu, false);
            Check(restored.Dispatch(new GameCommand(ActionKind.ServeOrder)), "Restored order cannot be served");
            var again = new GameController(store, new LocalDevelopmentGateway(), Rules());
            Check(again.Session.Order.Phase == OrderPhase.Served && again.Session.Reviews.Count == 1 && again.Session.Balance == restored.Session.Balance, "Serving reward changed during replay");
            Check(!again.Dispatch(new GameCommand(ActionKind.ServeOrder)) && !again.Dispatch(new GameCommand(ActionKind.Discard, replacement)), "Replay allowed a second serve or discard of delivered food");
            Check(store.Data.commands.FindAll(c => c.kind == ActionKind.EnableManualServing).Count == 1, "Migration marker repeated");
        }

        private static void AutomaticGuestArrival()
        {
            foreach (bool tutorial in new[] { false, true }) {
                var rules = Rules(); rules.guestIntervalTicks = 40;
                var profile = Profile(tutorial); profile.demonstrationSeen = true; profile.ownedIngredients = 1;
                var store = new MemoryStore { Data = new SavedGame { profile = profile, rules = rules } };
                var game = new GameController(store, new LocalDevelopmentGateway(), rules); game.StartDay(7);
                var audio = new AudioFeedback(game.Session); var sounds = new List<SoundSignal>();
                game.CommandApplied += c => sounds.AddRange(audio.AfterCommand(game.Session, c));
                Action<GameCommand> send = c => Check(game.Dispatch(c), "Arrival command failed: " + c.kind);
                for (int id = 1; id <= 2; id++) {
                    send(new GameCommand(ActionKind.Accept)); PrepareWithCommands(send); send(new GameCommand(ActionKind.ServeOrder));
                    int balance = game.Session.Balance;
                    send(new GameCommand(ActionKind.Tick, 39));
                    Check(game.Session.Order.Id == id && game.Session.GuestLeaving, "Guest arrived before departure finished");
                    send(new GameCommand(ActionKind.Tick, 1));
                    Check(game.Session.Order.Id == id + 1 && game.Session.Order.Phase == OrderPhase.Offered && !game.Session.GuestLeaving, "Next guest requires a button or was accepted automatically");
                    Check(game.Session.Balance == balance && game.Session.Reviews.Count == id && Count(sounds, SoundCue.GuestEnter) == id, "Arrival changed rewards or repeated its sound");
                    send(new GameCommand(ActionKind.Tick, 40));
                    Check(game.Session.Order.Id == id + 1, "An offered guest was replaced");
                }
                var restored = new GameController(store, new LocalDevelopmentGateway(), rules);
                Check(restored.Session.Order.Id == 3 && restored.Session.Order.Request == game.Session.Order.Request && restored.Session.Balance == game.Session.Balance && restored.Session.Reviews.Count == 2, "Arrival replay changed order or rewards");
                Check(store.Data.commands.FindAll(c => c.kind == ActionKind.NextGuest).Count == 2, "Automatic arrivals were not journaled exactly once");
            }
        }

        private static void AutomaticGuestTiming()
        {
            foreach (bool closing in new[] { false, true }) {
                var rules = Rules(); rules.guestIntervalTicks = 40; rules.patiencePerPancakeTicks = 4;
                if (closing) rules.dayTicks = 44;
                var store = new MemoryStore { Data = new SavedGame { profile = Profile(), rules = rules } };
                var game = new GameController(store, new LocalDevelopmentGateway(), rules); game.StartDay(7);
                game.Dispatch(new GameCommand(ActionKind.BeginStroke, -1, 500, 500)); game.Dispatch(new GameCommand(ActionKind.EndStroke));
                game.Dispatch(new GameCommand(ActionKind.FinishBatter)); var pan = game.Session.PanPancake;
                game.Dispatch(new GameCommand(ActionKind.Accept)); game.Dispatch(new GameCommand(ActionKind.Tick, 4));
                game.Dispatch(new GameCommand(ActionKind.Tick, 39));
                Check(game.Session.Order.Id == 1 && game.Session.Order.Phase == OrderPhase.Cancelled, "Timeout did not leave time for departure");
                int heat = pan.HeatA, elapsed = game.Session.ElapsedTicks, refund = game.Session.Ledger.Refunds;
                foreach (var reason in new[] { PauseReason.Menu, PauseReason.RecipeBook, PauseReason.Background, PauseReason.Focus }) {
                    game.SetPause(reason, true);
                    Check(!game.Dispatch(new GameCommand(ActionKind.Tick, 100)), "Arrival timer ran while paused");
                    Check(game.Session.Order.Id == 1 && game.Session.ElapsedTicks == elapsed && pan.HeatA == heat, "Paused arrival changed the session");
                    game.SetPause(reason, false);
                }
                Check(game.Dispatch(new GameCommand(ActionKind.Tick, 1)), "Arrival tick failed");
                Check(game.Session.Ledger.Refunds == refund && game.Session.Reviews.Count == 1, "Arrival refunded twice");
                if (closing) Check(game.Session.Phase == DayPhase.Result && game.Session.Order.Id == 1, "A guest arrived at closing");
                else Check(game.Session.Order.Id == 2 && game.Session.Order.Phase == OrderPhase.Offered && game.Session.PanPancake == pan && pan.HeatA == heat + 1, "Arrival lost food or interrupted frying");
            }
        }

        private static void AutomaticGuestLegacyWait()
        {
            foreach (int waited in new[] { 17, 80 }) {
                var rules = Rules(); rules.guestIntervalTicks = 40;
                var profile = Profile(); var legacy = new DaySession(profile, rules, 7, false, false);
                var commands = new List<GameCommand>();
                Action<GameCommand> send = c => { Check(legacy.Execute(c), "Waiting fixture command failed: " + c.kind); commands.Add(c.Copy()); };
                send(new GameCommand(ActionKind.EnableFreeCooking)); send(new GameCommand(ActionKind.EnableManualServing));
                send(new GameCommand(ActionKind.Accept)); PrepareWithCommands(send); send(new GameCommand(ActionKind.ServeOrder));
                send(new GameCommand(ActionKind.Tick, waited));
                var store = new MemoryStore { Data = new SavedGame { profile = profile.Copy(), sessionStart = profile.Copy(), seed = 7, rules = rules, commands = commands } };
                var restored = new GameController(store, new LocalDevelopmentGateway(), rules);
                Check(restored.Session.Order.Id == 1 && restored.Session.Balance == legacy.Balance, "Loading rewrote an old wait");
                if (waited < 40) {
                    restored.Dispatch(new GameCommand(ActionKind.Tick, 39 - waited));
                    Check(restored.Session.Order.Id == 1, "Loading skipped the remaining interval");
                }
                Check(restored.Dispatch(new GameCommand(ActionKind.Tick, 1)) && restored.Session.Order.Id == 2, "Waiting save did not resume arrivals");
                var again = new GameController(store, new LocalDevelopmentGateway(), rules);
                var audio = new AudioFeedback(again.Session); var sounds = new List<SoundSignal>();
                again.CommandApplied += c => sounds.AddRange(audio.AfterCommand(again.Session, c));
                again.Dispatch(new GameCommand(ActionKind.Tick, 40));
                Check(again.Session.Order.Id == 2 && again.Session.Balance == legacy.Balance && sounds.Count == 0 && store.Data.commands.FindAll(c => c.kind == ActionKind.NextGuest).Count == 1, "Loading duplicated an arrival, reward or audio");
            }
        }

        private static void LegacyServingReplay()
        {
            var profile = Profile(); var rules = Rules();
            var legacy = new DaySession(profile, rules, 7, false, false);
            var commands = new List<GameCommand>();
            Action<GameCommand> send = c => { Check(legacy.Execute(c), "Legacy serving command failed: " + c.kind); commands.Add(c.Copy()); };
            send(new GameCommand(ActionKind.EnableFreeCooking)); send(new GameCommand(ActionKind.Accept)); PrepareWithCommands(send);
            Check(legacy.Order.Phase == OrderPhase.Served, "Legacy fixture must use automatic delivery");
            send(new GameCommand(ActionKind.Tick, 1)); send(new GameCommand(ActionKind.NextGuest)); send(new GameCommand(ActionKind.Accept));
            var store = new MemoryStore { Data = new SavedGame { profile = profile.Copy(), sessionStart = profile.Copy(), seed = 7, rules = rules, commands = commands } };
            var restored = new GameController(store, new LocalDevelopmentGateway(), rules);
            Check(restored.Session.ManualServing && restored.Session.Balance == legacy.Balance && restored.Session.Reviews.Count == 1, "Migration rewrote automatic delivery history");
            PrepareWithCommands(c => Check(restored.Dispatch(c), "Migrated command failed: " + c.kind));
            Check(restored.Session.CanServeOrder && restored.Session.Order.Phase == OrderPhase.Accepted && restored.Session.Reviews.Count == 1, "Migrated order still auto-serves");
            var again = new GameController(store, new LocalDevelopmentGateway(), rules);
            Check(again.Session.CanServeOrder && again.Session.Reviews.Count == 1 && again.Session.Balance == restored.Session.Balance, "Mixed delivery history does not replay");
        }
    }
}
