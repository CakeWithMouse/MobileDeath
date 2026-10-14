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
            Do(s, ActionKind.Accept); Check(!Do(s, ActionKind.ServeOrder), "Served an empty order");
            Prepare(s, s.Order.Portions[0]); var unwanted = s.Order.Finished[0];
            Check(s.CanServeOrder && s.Reviews.Count == 0 && s.Ledger.Tips == 0, "Ready order completed automatically");
            int paid = s.Ledger.Revenue, products = s.Ledger.Products;
            Check(Do(s, ActionKind.Discard, unwanted.Id) && !s.CanServeOrder && s.Order.Finished.Count == 0, "Discarded portion still counts towards the order");
            Check(!Do(s, ActionKind.ServeOrder) && s.Ledger.Revenue == paid && s.Ledger.Products == products, "Incomplete order served or discard changed the receipt");
            Prepare(s, s.Order.Portions[0]); var replacement = s.Order.Finished[0];
            Check(replacement.Id != unwanted.Id && Do(s, ActionKind.ServeOrder), "Replacement could not be served");
            Check(!Do(s, ActionKind.Discard, replacement.Id) && s.Order.Finished[0] == replacement, "Already delivered food was discarded");
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
