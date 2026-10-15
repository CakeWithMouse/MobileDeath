using System.Collections.Generic;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;

namespace Blinopek.Tests
{
    public static partial class CoreScenarios
    {
        private static void TickJournalBoundary()
        {
            var rules = Rules();
            rules.dayTicks = 50000;
            var store = new MemoryStore { Data = new SavedGame { profile = Profile(), rules = rules } };
            var game = new GameController(store, new LocalDevelopmentGateway(), rules);
            Check(game.StartDay(7), "Cannot start journal fixture");

            var reusable = new GameCommand(ActionKind.Tick, GameCommand.MaxTickCount - 2);
            Check(game.Dispatch(reusable), "First tick batch rejected");
            reusable.a = 2;
            Check(game.Dispatch(reusable), "Second tick batch rejected");
            reusable.a = 1;
            Check(game.Dispatch(reusable), "Overflow batch rejected");
            reusable.a = -1;

            var ticks = store.Data.commands.FindAll(c => c.kind == ActionKind.Tick);
            Check(ticks.Count == 2 && ticks[0].a == GameCommand.MaxTickCount && ticks[1].a == 1,
                "Journal exceeded the replay limit or retained caller-owned commands");
            int count = store.Data.commands.Count;
            Check(!game.Dispatch(reusable) && !game.Dispatch(new GameCommand(ActionKind.Tick, GameCommand.MaxTickCount + 1)),
                "Invalid tick batch accepted");
            Check(store.Data.commands.Count == count && game.Session.ElapsedTicks == GameCommand.MaxTickCount + 1,
                "Rejected ticks changed the journal or simulation");

            var restored = new GameController(store, new LocalDevelopmentGateway(), rules);
            Check(restored.Session.ElapsedTicks == game.Session.ElapsedTicks && restored.Session.Balance == game.Session.Balance,
                "Bounded tick batches did not replay identically");
            Check(store.Data.commands.Count == count, "Replay appended duplicate migration commands");
        }

        private static void CommandRejection()
        {
            var session = Session(Profile(true));
            int revision = session.Revision;
            Check(!Do(session, ActionKind.NextGuest) && session.Revision == revision, "No-op changed the revision");
            Check(!Do(session, ActionKind.MoveToPlate) && session.Revision == revision + 1 && session.Pancake == null,
                "Rejected transfer changed the kitchen or failed to publish its explanation once");
            Check(Do(session, ActionKind.Accept) && Do(session, ActionKind.BeginStroke, -1, 500, 500), "Cannot start lesson batter");
            revision = session.Revision;
            int coverage = session.PanPancake.Batter.Count;
            Check(!Do(session, ActionKind.FinishBatter), "Incomplete lesson batter accepted");
            Check(session.Revision == revision + 1 && session.PanPancake.Stage == PancakeStage.Batter &&
                session.PanPancake.Batter.Count == coverage && session.Feedback.Contains("Закройте дырки"),
                "Lesson rejection changed the batter or duplicated its feedback revision");
            Check(Do(session, ActionKind.Stroke, 800, 500) && session.PanPancake.Batter.Count > coverage,
                "Rejected finish interrupted the held stroke");
        }

        private static void ShopNotifications()
        {
            var rules = Rules();
            rules.dayTicks = 2;
            rules.rent = 0;
            var profile = Profile();
            profile.ownedIngredients = 1;
            profile.coins = 100000;
            profile.honey = 100;
            var store = new MemoryStore { Data = new SavedGame { profile = profile, rules = rules } };
            var game = new GameController(store, new LocalDevelopmentGateway(), rules);
            game.StartDay(7);
            game.Dispatch(new GameCommand(ActionKind.Tick, rules.dayTicks + rules.guestIntervalTicks));
            var purchases = new List<ShopItem>();
            int changes = 0, writes = store.Writes;
            game.PurchaseApplied += purchases.Add;
            game.Changed += () => changes++;
            var items = new[] { ShopItem.Ingredient, ShopItem.Scraper, ShopItem.Patience,
                ShopItem.DarkTables, ShopItem.Helper, ShopItem.ToggleHelper };
            foreach (var item in items) Check(game.Buy(item, 1), "Shop action rejected: " + item);
            Check(purchases.Count == items.Length && changes == items.Length && store.Writes == writes + items.Length,
                "A purchase skipped or duplicated persistence/notifications");
            for (int i = 0; i < items.Length; i++) Check(purchases[i] == items[i], "Purchase event order changed");
            Check(!game.Buy(ShopItem.Ingredient, 1) && !game.Buy(ShopItem.Helper, 1) &&
                purchases.Count == items.Length && changes == items.Length && store.Writes == writes + items.Length,
                "Rejected duplicate purchases saved or notified");
            var restored = new GameController(store, new LocalDevelopmentGateway(), rules);
            Check(restored.Profile.Owns(Ingredient.Jam) && restored.Profile.scrapers == profile.scrapers + 1 &&
                restored.Profile.patienceUpgrade && restored.Profile.darkTablesUpgrade &&
                restored.Profile.helpersOwned == 2 && !restored.Profile.HelperEnabled(Ingredient.Jam), "Purchased state did not survive reload");
            Check(restored.Profile.coins == 100000 - rules.ingredientUnlockCost - rules.scraperPrice -
                rules.patienceUpgradeCost - rules.darkTableUpgradeCost && restored.Profile.honey == 100 - rules.helperHoneyPrice,
                "Purchases charged the wrong amount or currency");
        }
    }
}
