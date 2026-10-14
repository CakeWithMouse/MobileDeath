using System;
using System.Collections.Generic;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;

namespace Blinopek.Tests
{
    // Shared by NUnit in Unity and the dependency-free dotnet runner.
    public static partial class CoreScenarios
    {
        public static readonly Dictionary<string, Action> Cases = new Dictionary<string, Action> {
            { "Complete order pays exactly once", CompleteOrder },
            { "First lesson burns, discards and protects retry", SafeTutorial },
            { "Second side is required before filling", CookingGuards },
            { "Burned pancakes remain serveable", BurnedIsServeable },
            { "Incorrect fold does not deadlock", IncorrectFold },
            { "Multiple portions wait and match in any order", MultiplePortions },
            { "Scraper removes one whole type with no refund", Scraper },
            { "Repeated painting costs more without more coverage", RepeatPaint },
            { "Stroke sampling is independent of event count", StrokeSampling },
            { "Wrong portion placement lowers quality", PortionPlacement },
            { "Patience expiration refunds once and preserves kitchen", Timeout },
            { "Closing finishes accepted order and rejects new one", Closing },
            { "Unaccepted visitor creates no closing revenue", UnacceptedClosing },
            { "Pause reasons compose and stop every clock", Pauses },
            { "Save replay restores heat, masks, costs and receipt", Replay },
            { "Failure restores balance, tools and day", FailedDay },
            { "Success advances once and purchases persist", ProgressAndShop },
            { "Owned helpers use normal costs and only one ingredient", Helpers },
            { "Locked ingredients never appear in generated orders", Generation },
            { "Active session pins its balance settings", PinnedRules },
            { "Corrupt storage is never overwritten", BlockedStorage },
            { "Audio only observes committed commands and restores silently", AudioCommitted },
            { "Audio money and serving feedback happen once", AudioReceipts },
            { "Audio cooking and patience thresholds trigger once", AudioThresholds },
            { "Audio helper emits a bounded sequence", AudioHelper },
            { "Cooking before acceptance prepares a paid-for stock without revenue", CookBeforeOrder },
            { "Serving prepared stock preserves extra portions and the active pancake", ServePreparedStock },
            { "Cancelled orders return prepared portions and preserve held strokes", KeepCancelledCooking },
            { "A pancake started before the lesson keeps ordinary cooking rules", FreeCookingBeforeLesson },
            { "Prepared stock and independent heat survive save replay", FreeCookingReplay },
            { "Legacy saves replay before switching cooking rules exactly once", LegacyCookingReplay },
            { "Closing clears unused stock without serving or paying for it", ClosePreparedKitchen },
            { "Cooking without a guest emits tray and heat audio without payment", FreeCookingAudio },
            { "A second pancake fries while the first is partially folded", FryWhileFolding },
            { "Filling tools affect only the plate while the pan is occupied", FillWhileFrying },
            { "Discard removes only the specified pancake", DiscardOneStation },
            { "Timeout and closing handle both kitchen stations", ConcurrentTimeout },
            { "Replay restores two pancakes and an interrupted gesture independently", ConcurrentReplay },
            { "The tutorial protects only its own pancake during parallel cooking", ConcurrentLesson },
            { "Ready pancakes can be discarded by identity without affecting either station", DiscardReadyPancake },
            { "A discarded order portion must be replaced before explicit serving", ReplaceReadyPortion },
            { "Prepared orders still expire while waiting for explicit serving", ReadyOrderTimeout },
            { "Discard and explicit serving survive replay without duplicate rewards", ManualServingReplay },
            { "Automatic-serving saves migrate without rewriting past orders", LegacyServingReplay }
        };
        private sealed class MemoryStore : IGameStore { public SavedGame Data; public int Writes; public SavedGame Load() => Data; public void Save(SavedGame game) { Data = game; Writes++; } }
        private sealed class BrokenStore : IGameStore { public SavedGame Load() => throw new Exception("broken"); public void Save(SavedGame game) => throw new Exception("Must not overwrite"); }
        private static BalanceRules Rules() => new BalanceRules { readyTicks = 2, burnTicks = 6, dayTicks = 2000, patiencePerPancakeTicks = 1000, guestIntervalTicks = 1 };
        private static Profile Profile(bool tutorial = false) => new Profile { tutorialComplete = !tutorial, demonstrationSeen = !tutorial, coins = 10000, scrapers = 2, ownedIngredients = 31 };
        private static DaySession Session(Profile profile = null, BalanceRules rules = null, int seed = 77) => new DaySession(profile ?? Profile(), rules ?? Rules(), seed);
        private static bool Do(DaySession s, ActionKind kind, int a = 0, int b = 0, int c = 0) => s.Execute(new GameCommand(kind, a, b, c));
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        public static void Paint(Action<GameCommand> send, int type)
        {
            for (int y = 60; y <= 940; y += 110)
            {
                int edge = (int)Math.Sqrt(250000 - (y - 500) * (y - 500));
                send(new GameCommand(ActionKind.BeginStroke, type, 501 - edge, y));
                send(new GameCommand(ActionKind.Stroke, 499 + edge, y)); send(new GameCommand(ActionKind.EndStroke));
            }
        }
        private static void Plate(DaySession s, bool burned = false)
        {
            Paint(c => s.Execute(c), -1); Check(Do(s, ActionKind.FinishBatter), "Finish batter failed");
            Do(s, ActionKind.Tick, burned ? s.Rules.burnTicks : s.Rules.readyTicks); Check(Do(s, ActionKind.Flip), "Flip failed");
            Do(s, ActionKind.Tick, s.Rules.readyTicks); Check(Do(s, ActionKind.MoveToPlate), "Move failed");
        }
        private static void Fill(DaySession s, PortionSpec spec)
        {
            for (int i = 0; i < 5; i++) if (spec.Needs((Ingredient)i))
            {
                if (i < 3) Paint(c => s.Execute(c), i);
                else foreach (var target in Recipes.PortionTargets) Do(s, ActionKind.Portion, i, target.X, target.Y);
            }
        }
        private static void FoldPancake(DaySession s, Fold fold)
        {
            Check(Do(s, ActionKind.BeginFold, (int)fold), "Begin fold failed");
            foreach (var direction in Recipes.Gestures(fold)) {
                int dx = direction == Direction.Right ? 400 : direction == Direction.Left ? -400 : 0;
                int dy = direction == Direction.Up ? 400 : direction == Direction.Down ? -400 : 0;
                Check(Do(s, ActionKind.FoldGesture, dx, dy), "Fold gesture failed");
            }
        }
        private static void CompleteOrder()
        {
            var s = Session(); Check(Do(s, ActionKind.Accept), "Accept"); int paid = s.Ledger.Revenue;
            Check(!Do(s, ActionKind.Accept) && s.Ledger.Revenue == paid, "Double acceptance");
            Plate(s); Fill(s, s.Order.Portions[0]); FoldPancake(s, s.Order.Portions[0].Shape);
            Check(s.CanServeOrder && s.Order.Phase == OrderPhase.Accepted && s.Order.Report == null && s.Reviews.Count == 0, "Final fold served automatically");
            Check(Do(s, ActionKind.ServeOrder), "Explicit serving rejected");
            Check(s.Order.Phase == OrderPhase.Served && s.Order.Report.Total >= 95, "Correct dish not served well");
            int tips = s.Ledger.Tips; Check(!Do(s, ActionKind.FinalizeFold) && !Do(s, ActionKind.ServeOrder) && s.Ledger.Tips == tips && s.Reviews.Count == 1, "Double serving");
        }
        private static void SafeTutorial()
        {
            var s = Session(Profile(true)); Do(s, ActionKind.Accept);
            Paint(c => s.Execute(c), -1); Do(s, ActionKind.FinishBatter); Do(s, ActionKind.Tick, 20);
            Check(s.Tutorial == TutorialStep.Discard && s.Pancake.HeatA >= s.Rules.burnTicks, "No demonstration");
            Check(!Do(s, ActionKind.Flip), "Demonstration must be discarded"); Do(s, ActionKind.Discard);
            Check(s.DemonstrationSeen && s.Ledger.Products == 0 && s.Ledger.Revenue == 0 && s.Order.WaitTicks == 0, "Unsafe lesson");
            Paint(c => s.Execute(c), -1); Do(s, ActionKind.FinishBatter); Do(s, ActionKind.Tick, 500);
            Check(s.Pancake.HeatA == s.Rules.readyTicks && s.ElapsedTicks == 0, "Lesson ready hold");
            Do(s, ActionKind.Flip); Do(s, ActionKind.Tick, 500); Do(s, ActionKind.MoveToPlate); Fill(s, s.Order.Portions[0]);
            Do(s, ActionKind.BeginFold); Check(!Do(s, ActionKind.FoldGesture, -400, 0) && s.Pancake.Folds.Count == 0, "Bad tutorial gesture advanced");
            Do(s, ActionKind.FoldGesture, 0, 400); Do(s, ActionKind.FoldGesture, 400, 0);
            Check(!s.TutorialCompleted && s.Ledger.Revenue == 0 && Do(s, ActionKind.ServeOrder), "Lesson must wait for explicit serving");
            Check(s.TutorialCompleted && s.Ledger.Revenue == s.Order.Price && s.Ledger.Products == 0, "Tutorial reward");
            Do(s, ActionKind.Tick, 1); Do(s, ActionKind.NextGuest); Do(s, ActionKind.Accept); Do(s, ActionKind.BeginStroke, -1, 500, 500);
            Check(s.Ledger.Products > 0 && !s.IsTutorial, "Safe mode leaked into ordinary order");
        }
        private static void CookingGuards()
        {
            var s = Session(); Do(s, ActionKind.Accept); Do(s, ActionKind.BeginStroke, -1, 500, 500); Do(s, ActionKind.EndStroke);
            Check(!Do(s, ActionKind.BeginStroke, 0, 500, 500) && !Do(s, ActionKind.MoveToPlate), "Fill in pan");
            Do(s, ActionKind.FinishBatter); Do(s, ActionKind.Flip); Check(!Do(s, ActionKind.MoveToPlate), "Raw second side moved");
            Do(s, ActionKind.Tick, 2); Check(Do(s, ActionKind.MoveToPlate) && s.Pancake.HeatA == 0, "Early flip must keep raw side A");
        }
        private static void BurnedIsServeable() { var s = Session(); Do(s, ActionKind.Accept); Plate(s, true); Fill(s, s.Order.Portions[0]); FoldPancake(s, Fold.Triangle); Check(Do(s, ActionKind.ServeOrder) && s.Order.Phase == OrderPhase.Served && s.Order.Report.Cooking < 100, "Burnt dish blocked or unpenalized"); }
        private static void IncorrectFold() { var s = Session(); Do(s, ActionKind.Accept); Plate(s); Fill(s, s.Order.Portions[0]); Do(s, ActionKind.BeginFold); Do(s, ActionKind.FoldGesture, -400); Check(Do(s, ActionKind.FinalizeFold) && Do(s, ActionKind.ServeOrder) && s.Order.Phase == OrderPhase.Served && s.Order.Report.Shape == 0, "Wrong fold deadlock"); }
        private static void MultiplePortions()
        {
            var profile = Profile(); profile.day = 3; DaySession s = null;
            for (int seed = 1; seed < 100; seed++) { s = Session(profile, null, seed); if (s.Order.Portions.Count > 1) break; }
            Check(s.Order.Portions.Count > 1, "No multi order"); Do(s, ActionKind.Accept);
            for (int i = s.Order.Portions.Count - 1; i >= 0; i--) {
                Plate(s); Fill(s, s.Order.Portions[i]); FoldPancake(s, s.Order.Portions[i].Shape);
                if (i > 0) Check(s.Order.Phase == OrderPhase.Accepted && s.Ledger.Tips == 0, "Served before last portion");
            }
            Check(Do(s, ActionKind.ServeOrder) && s.Order.Report.Total >= 95, "Reverse preparation penalized");
        }
        private static void Scraper()
        {
            var s = Session(); Do(s, ActionKind.Accept); Plate(s); Paint(c => s.Execute(c), 0); Paint(c => s.Execute(c), 1); int cost = s.Ledger.Products;
            Check(Do(s, ActionKind.Remove, 0) && s.Pancake.Fillings[0] == null && s.Pancake.Fillings[1] != null && s.Scrapers == 1 && s.Ledger.Products == cost, "Scraper semantics");
            Check(!Do(s, ActionKind.Remove, 0) && s.Scrapers == 1, "Repeated scraper spent again");
            Do(s, ActionKind.BeginFold); Check(!Do(s, ActionKind.Remove, 1), "Scraper during fold");
        }
        private static void RepeatPaint()
        {
            var s = Session(); Do(s, ActionKind.Accept); Paint(c => s.Execute(c), -1); int count = s.Pancake.Batter.Count, cost = s.Ledger.Products;
            Paint(c => s.Execute(c), -1); Check(s.Pancake.Batter.Count == count && s.Ledger.Products == cost * 2, "Repeated dose not charged");
        }
        private static void StrokeSampling()
        {
            var a = new List<Point>(); var b = new List<Point>();
            var first = new StrokeSampler(100, 500, 35, (x, y) => a.Add(new Point(x, y)));
            var second = new StrokeSampler(100, 500, 35, (x, y) => b.Add(new Point(x, y)));
            first.Move(900, 500); for (int x = 110; x <= 900; x += 10) second.Move(x, 500);
            Check(a.Count == b.Count, "Frame count changed doses"); for (int i = 0; i < a.Count; i++) Check(a[i].X == b[i].X && a[i].Y == b[i].Y, "Frame count changed positions");
        }
        private static void PortionPlacement()
        {
            var s = Session(); Do(s, ActionKind.Accept); Plate(s); for (int i = 0; i < 4; i++) Do(s, ActionKind.Portion, 3, 500, 500);
            var wrong = QualityEvaluator.EvaluatePancake(s.Pancake, new PortionSpec(8, Fold.Triangle), s.Rules).Composition;
            Do(s, ActionKind.Remove, 3); foreach (var p in Recipes.PortionTargets) Do(s, ActionKind.Portion, 3, p.X, p.Y);
            Check(QualityEvaluator.EvaluatePancake(s.Pancake, new PortionSpec(8, Fold.Triangle), s.Rules).Composition > wrong, "Portion placement ignored");
        }
        private static void Timeout()
        {
            var s = Session(); Do(s, ActionKind.Accept); Plate(s); var pancake = s.Pancake; int products = s.Ledger.Products; Do(s, ActionKind.Tick, 1000);
            Check(s.Order.Phase == OrderPhase.Cancelled && s.Pancake == pancake && s.Ledger.Refunds == s.Order.Price && s.Ledger.Products == products, "Timeout refund/preservation");
            Do(s, ActionKind.Tick, 100); Check(s.Ledger.Refunds == s.Order.Price && !Do(s, ActionKind.FinalizeFold), "Duplicate timeout refund");
            Fill(s, s.Order.Portions[0]); FoldPancake(s, s.Order.Portions[0].Shape);
            Check(s.ReadyPancakes.Count == 1 && s.Order.Phase == OrderPhase.Cancelled, "Cannot finish cooking after the guest leaves");
        }
        private static void Closing()
        {
            var rules = Rules(); rules.dayTicks = 2; var s = Session(null, rules); Do(s, ActionKind.Accept); Plate(s);
            Check(s.Phase == DayPhase.Closing && s.HasActiveOrder, "Closing dropped active order"); Fill(s, s.Order.Portions[0]); FoldPancake(s, Fold.Triangle);
            Check(Do(s, ActionKind.ServeOrder), "Cannot explicitly serve the closing order");
            Check(s.Phase == DayPhase.Result && !Do(s, ActionKind.NextGuest) && s.Ledger.Rent > 0, "Closing did not settle");
        }
        private static void UnacceptedClosing() { var rules = Rules(); rules.dayTicks = 2; var s = Session(null, rules); Do(s, ActionKind.Tick, 2); Check(s.Phase == DayPhase.Result && s.Ledger.Revenue == 0 && !Do(s, ActionKind.Accept), "Accepted after close"); }
        private static void Pauses()
        {
            var store = new MemoryStore { Data = new SavedGame { profile = Profile(), rules = Rules() } }; var game = new GameController(store, new LocalDevelopmentGateway(), Rules()); game.StartDay(7); game.Dispatch(new GameCommand(ActionKind.Accept));
            game.SetPause(PauseReason.Menu, true); game.SetPause(PauseReason.Background, true); game.SetPause(PauseReason.Menu, false);
            Check(!game.Dispatch(new GameCommand(ActionKind.Tick, 10)) && game.Session.ElapsedTicks == 0, "One reason cleared another");
            game.SetPause(PauseReason.Background, false); Check(game.Dispatch(new GameCommand(ActionKind.Tick, 10)) && game.Session.ElapsedTicks == 10, "Did not resume");
        }
        private static void Replay()
        {
            var store = new MemoryStore { Data = new SavedGame { profile = Profile(), rules = Rules() } }; var game = new GameController(store, new LocalDevelopmentGateway(), Rules()); game.StartDay(7);
            game.Dispatch(new GameCommand(ActionKind.Accept)); Paint(c => game.Dispatch(c), -1); game.Dispatch(new GameCommand(ActionKind.FinishBatter)); game.Dispatch(new GameCommand(ActionKind.Tick, 3)); game.Save();
            var restored = new GameController(store, new LocalDevelopmentGateway(), Rules());
            Check(restored.Session.Pancake.HeatA == 3 && restored.Session.Balance == game.Session.Balance && restored.Session.Pancake.Batter.Count == game.Session.Pancake.Batter.Count && restored.Session.Order.Id == game.Session.Order.Id, "Replay differs");
        }
        private static void FailedDay()
        {
            var rules = Rules(); rules.rent = 100000; rules.dayTicks = 2;
            var store = new MemoryStore { Data = new SavedGame { profile = Profile(), rules = rules } }; var game = new GameController(store, new LocalDevelopmentGateway(), rules); int coins = game.Profile.coins; game.StartDay(7); game.Dispatch(new GameCommand(ActionKind.Tick, 2));
            Check(!game.Session.Success && game.Profile.coins == coins && game.Profile.day == 1 && game.Profile.scrapers == 2, "Failed day changed profile");
            Check(game.StartDay(8) && game.Session.StartProfile.coins == coins && game.Profile.day == 1, "Retry advanced");
        }
        private static void ProgressAndShop()
        {
            var rules = Rules(); rules.dayTicks = 2; var profile = Profile(); profile.ownedIngredients = 1;
            var store = new MemoryStore { Data = new SavedGame { profile = profile, rules = rules } }; var game = new GameController(store, new LocalDevelopmentGateway(), rules); game.StartDay(7); game.Dispatch(new GameCommand(ActionKind.Tick, 2));
            int coins = game.Profile.coins; Check(game.Profile.day == 2, "No advancement");
            var restored = new GameController(store, new LocalDevelopmentGateway(), rules); Check(restored.Profile.day == 2 && restored.Profile.coins == coins, "Duplicate settlement on load");
            Check(restored.Buy(ShopItem.Ingredient, 1) && !restored.Buy(ShopItem.Ingredient, 1) && restored.Profile.coins == coins - rules.ingredientUnlockCost, "Double purchase");
            Check(restored.StartDay(9) && restored.Session.StartProfile.Owns(Ingredient.Jam), "Purchase lost next day");
            Check(!restored.Buy(ShopItem.Scraper), "Purchase during shift");
        }
        private static void Helpers()
        {
            var profile = Profile(); profile.helpersOwned = profile.helpersEnabled = 1; var s = Session(profile); Do(s, ActionKind.Accept); Plate(s); int before = s.Ledger.Products;
            Check(Do(s, ActionKind.ApplyHelper, 0) && s.Pancake.IngredientBits == 1 && s.Pancake.Fillings[0].Mask.Percent >= 72 && s.Ledger.Products > before, "Helper semantics");
            Check(!Do(s, ActionKind.ApplyHelper, 1), "Unowned helper usable");
        }
        private static void Generation()
        {
            var profile = Profile(); profile.day = 5; profile.ownedIngredients = 1;
            for (int seed = 1; seed < 100; seed++) foreach (var p in Session(profile, null, seed).Order.Portions) Check(p.Ingredients == 1, "Locked ingredient generated");
        }
        private static void PinnedRules()
        {
            var rules = Rules(); var game = new GameController(new MemoryStore(), new LocalDevelopmentGateway(), rules); game.StartDay(1); rules.readyTicks = 99;
            Check(game.Session.Rules.readyTicks == 2, "Changing content mutated active session");
        }
        private static void BlockedStorage()
        {
            var game = new GameController(new BrokenStore(), new LocalDevelopmentGateway(), Rules()); Check(game.StorageBlocked && !game.StartDay(1), "Corrupt data overwritten"); game.Save();
        }
    }
}
