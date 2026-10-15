using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;

namespace Blinopek.Tests
{
    public static partial class CoreScenarios
    {
        private static void DarkTablesPurchase()
        {
            Check(!new Profile().darkTablesUpgrade, "New players must start with light tables");
            var rules = Rules(); rules.dayTicks = 2; rules.rent = 0;
            var profile = Profile(); profile.coins = rules.darkTableUpgradeCost + rules.rent;
            var store = new MemoryStore { Data = new SavedGame { profile = profile, rules = rules } };
            var game = new GameController(store, new LocalDevelopmentGateway(), rules);
            Check(!game.Buy(ShopItem.DarkTables), "Cannot buy before a completed day");
            game.StartDay(7);
            Check(!game.Buy(ShopItem.DarkTables), "Cannot buy during service");
            game.Dispatch(new GameCommand(ActionKind.Tick, rules.dayTicks + rules.guestIntervalTicks));
            Check(game.Session.Success, "Successful day required for test");
            int coins = game.Profile.coins, notifications = 0;
            game.PurchaseApplied += item => { if (item == ShopItem.DarkTables) notifications++; };
            Check(game.Buy(ShopItem.DarkTables), "Affordable upgrade refused");
            Check(game.Profile.darkTablesUpgrade && game.Profile.coins == coins - rules.darkTableUpgradeCost, "Wrong ownership or cost");
            int writes = store.Writes;
            Check(!game.Buy(ShopItem.DarkTables) && store.Writes == writes && notifications == 1, "Duplicate purchase charged or notified");
            var restored = new GameController(store, new LocalDevelopmentGateway(), rules);
            Check(restored.Profile.darkTablesUpgrade && !restored.Buy(ShopItem.DarkTables), "Upgrade lost after reload");
            rules.rent = 50000;
            Check(restored.StartDay(8) && restored.Session.StartProfile.darkTablesUpgrade, "Next day lost furniture");
            restored.Dispatch(new GameCommand(ActionKind.Tick, rules.dayTicks + rules.guestIntervalTicks));
            Check(restored.Profile.darkTablesUpgrade, "Failed next day removed purchased furniture");
        }
        private static void DarkTablesGuards()
        {
            var rules = Rules(); rules.dayTicks = 2; rules.rent = 0;
            var profile = Profile(); profile.coins = rules.darkTableUpgradeCost + rules.rent - 1;
            var store = new MemoryStore { Data = new SavedGame { profile = profile, rules = rules } };
            var game = new GameController(store, new LocalDevelopmentGateway(), rules);
            game.StartDay(7); game.Dispatch(new GameCommand(ActionKind.Tick, rules.dayTicks + rules.guestIntervalTicks));
            Check(game.Session.Success, "Affordability must be checked after a successful day");
            int coins = game.Profile.coins, writes = store.Writes;
            Check(!game.Buy(ShopItem.DarkTables) && !game.Profile.darkTablesUpgrade && game.Profile.coins == coins && store.Writes == writes, "Unaffordable purchase mutated state");
            rules.rent = 50000;
            var failed = new GameController(new MemoryStore { Data = new SavedGame { profile = profile.Copy(), rules = rules } }, new LocalDevelopmentGateway(), rules);
            failed.StartDay(7); failed.Dispatch(new GameCommand(ActionKind.Tick, rules.dayTicks + rules.guestIntervalTicks));
            Check(!failed.Session.Success && !failed.Buy(ShopItem.DarkTables) && !failed.Profile.darkTablesUpgrade, "Failed day unlocked furniture");
        }
    }
}
