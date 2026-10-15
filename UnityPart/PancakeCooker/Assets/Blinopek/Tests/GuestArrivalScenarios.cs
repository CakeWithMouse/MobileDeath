using System;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;

namespace Blinopek.Tests
{
    public static partial class CoreScenarios
    {
        private static BalanceRules DecorationRules()
        {
            var rules = Rules(); rules.guestIntervalTicks = 40;
            rules.guestArrivalBaseTicks = 80; rules.guestArrivalMinTicks = 40;
            rules.guestArrivalReductionPerDecorationPointTicks = 2; rules.darkTablesDecorationPoints = 10;
            return rules;
        }
        private static void DecorationGuestBalance()
        {
            var rules = DecorationRules(); rules.Validate();
            Check(rules.GetGuestArrivalIntervalTicks(0) == 80 && rules.GetGuestArrivalIntervalTicks(10) == 60 && rules.GetGuestArrivalIntervalTicks(20) == 40, "Decoration curve has incorrect timing");
            Check(rules.GetGuestArrivalIntervalTicks(int.MaxValue) == 40 && rules.GetGuestArrivalIntervalTicks(-1) == 80, "Decoration curve overflowed or escaped its bounds");
            rules.guestArrivalReductionPerDecorationPointTicks = 0;
            Check(rules.GetGuestArrivalIntervalTicks(100) == 80, "Cannot balance decoration influence down to zero");
            rules.guestArrivalBaseTicks = 0;
            Check(rules.GetGuestArrivalIntervalTicks(100) == 40, "Legacy rules changed their interval");
            foreach (int invalid in new[] { 0, 39, 81 }) {
                rules = DecorationRules(); rules.guestArrivalMinTicks = invalid;
                bool rejected = false; try { rules.Validate(); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Arrival minimum can cut off departure or exceed the base interval");
            }
            rules = DecorationRules(); rules.guestArrivalReductionPerDecorationPointTicks = int.MaxValue;
            Check(rules.GetGuestArrivalIntervalTicks(int.MaxValue) == 40, "Large decoration values overflowed");
        }
        private static void DecorationGuestFlow()
        {
            foreach (bool decorated in new[] { false, true }) foreach (bool serve in new[] { false, true }) {
                var rules = DecorationRules(); var profile = Profile(); profile.ownedIngredients = 1; profile.darkTablesUpgrade = decorated;
                var store = new MemoryStore { Data = new SavedGame { profile = profile, rules = rules } };
                var game = new GameController(store, new LocalDevelopmentGateway(), rules); game.StartDay(7);
                Action<GameCommand> send = c => Check(game.Dispatch(c), "Guest flow command failed: " + c.kind);
                send(new GameCommand(ActionKind.Accept));
                if (serve) { PrepareWithCommands(send); send(new GameCommand(ActionKind.ServeOrder)); }
                else send(new GameCommand(ActionKind.Tick, game.Session.Order.PatienceLimit));
                int interval = decorated ? 60 : 80;
                Check(game.Session.GuestArrivalIntervalTicks == interval, "Installed furniture did not change the session interval");
                send(new GameCommand(ActionKind.Tick, rules.guestIntervalTicks));
                Check(game.Session.GuestHasLeft && game.Session.GuestDepartureProgress == 1 && !game.Session.CanOfferNext, "Arrival tuning changed the departure animation");
                send(new GameCommand(ActionKind.Tick, interval - rules.guestIntervalTicks - 1));
                Check(game.Session.Order.Id == 1, "Guest arrived before the decorated interval");
                game.SetPause(PauseReason.Menu, true);
                Check(!game.Dispatch(new GameCommand(ActionKind.Tick, 100)), "Paused arrival advanced");
                game.SetPause(PauseReason.Menu, false); send(new GameCommand(ActionKind.Tick, 1));
                Check(game.Session.Order.Id == 2 && game.Session.Order.Phase == OrderPhase.Offered, "Decorated interval did not trigger arrival");
            }
        }
        private static void DecorationGuestPurchase()
        {
            var rules = DecorationRules(); rules.dayTicks = 2; rules.rent = 0;
            var profile = Profile(); profile.coins = rules.darkTableUpgradeCost;
            var store = new MemoryStore { Data = new SavedGame { profile = profile, rules = rules } };
            var game = new GameController(store, new LocalDevelopmentGateway(), rules); game.StartDay(7);
            Check(game.Session.DecorationPoints == 0 && game.Session.GuestArrivalIntervalTicks == 80, "Bare cafe received decoration bonus");
            game.Dispatch(new GameCommand(ActionKind.Tick, rules.dayTicks + rules.guestIntervalTicks));
            Check(game.Buy(ShopItem.DarkTables) && game.StartDay(8), "Could not install furniture for the next day");
            Check(game.Session.DecorationPoints == 10 && game.Session.GuestArrivalIntervalTicks == 60, "Purchase did not affect guest flow");
            rules.guestArrivalBaseTicks = 160; rules.guestArrivalReductionPerDecorationPointTicks = 3; rules.darkTablesDecorationPoints = 20;
            var restored = new GameController(store, new LocalDevelopmentGateway(), rules);
            Check(restored.Session.DecorationPoints == 10 && restored.Session.GuestArrivalIntervalTicks == 60, "New balance changed the saved shift");
            restored.Dispatch(new GameCommand(ActionKind.Tick, 42)); Check(restored.StartDay(9), "Cannot start rebalanced shift");
            Check(restored.Session.DecorationPoints == 20 && restored.Session.GuestArrivalIntervalTicks == 100, "Next shift ignored new balance");
        }
        private static void DecorationGuestClosing()
        {
            var rules = DecorationRules(); rules.dayTicks = 50; rules.patiencePerPancakeTicks = 4;
            var store = new MemoryStore { Data = new SavedGame { profile = Profile(), rules = rules } };
            var game = new GameController(store, new LocalDevelopmentGateway(), rules); game.StartDay(7);
            game.Dispatch(new GameCommand(ActionKind.Accept)); game.Dispatch(new GameCommand(ActionKind.Tick, rules.dayTicks));
            Check(game.Session.Phase == DayPhase.Result && game.Session.Order.Id == 1, "Closing waited for an arrival or spawned another guest");
        }
    }
}
