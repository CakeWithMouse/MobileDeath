using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;

namespace Blinopek.Tests
{
    public static partial class CoreScenarios
    {
        private static void FirstShiftIsNewbieSession()
        {
            var rules = Rules(); int rent = rules.rent;
            // The lesson has its own scenarios; skip it here to reach the end of the shift.
            var store = new MemoryStore { Data = new SavedGame { profile = new Profile { tutorialComplete = true, demonstrationSeen = true }, rules = rules } };
            var game = new GameController(store, new LocalDevelopmentGateway(), rules);
            Check(game.IsNewbie && game.StartDay(7), "A new player must start the newbie shift");
            var easy = game.Session.Rules;
            Check(easy.rent == 0 && easy.refundThreshold == 0 && easy.maxPancakes == 1, "Newbie shift kept losses or extra pancakes");
            Check(easy.burnTicks == rules.burnTicks * 2 && easy.patiencePerPancakeTicks == rules.patiencePerPancakeTicks * 2, "Newbie shift is not forgiving");
            Check(rules.rent == rent, "Newbie rules changed the shared balance");
            var restored = new GameController(store, new LocalDevelopmentGateway(), rules);
            Check(restored.IsNewbie && restored.Session.Rules.rent == 0, "Restart lost the newbie rules");
            restored.Dispatch(new GameCommand(ActionKind.Tick, rules.dayTicks + rules.guestIntervalTicks));
            Check(restored.Session.Success && restored.Profile.matchesPlayed == 1 && !restored.IsNewbie, "Settled newbie shift must unlock the menu");
            Check(restored.StartDay(8) && restored.Session.Rules.rent == rent, "Second shift must use the normal rules");
        }
        private static void NewbieSessionIsNeverForced()
        {
            var store = new MemoryStore { Data = new SavedGame { profile = new Profile { day = 4 }, rules = Rules(), awaitingDay = true } };
            var game = new GameController(store, new LocalDevelopmentGateway(), Rules());
            Check(game.Profile.matchesPlayed == 3 && !game.IsNewbie, "Old save with finished days became a newbie");
            Check(!new GameController(new BrokenStore(), new LocalDevelopmentGateway(), Rules()).IsNewbie, "Blocked storage must keep the menu for a reset");
        }
    }
}
