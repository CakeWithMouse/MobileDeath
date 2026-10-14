using System;
using System.Collections.Generic;

namespace Blinopek.Domain
{
    // Persisted schema-1 identifiers: never reorder/reassign these values without a migration.
    public enum Ingredient { Butter = 0, Jam = 1, Honey = 2, Curd = 3, Mushrooms = 4 }
    public enum Fold { Triangle = 0, Envelope = 1, Roll = 2, Incorrect = 3 }
    public enum Direction { Up = 0, Right = 1, Down = 2, Left = 3 }
    public enum PancakeStage { Batter, SideA, SideB, Plate, Folding, Finished }
    public enum DayPhase { Serving, Closing, Result }
    public enum OrderPhase { Offered, Accepted, Served, Cancelled }
    public enum TutorialStep { Welcome, Batter, Demonstration, Discard, Retry, SideA, SideB, Filling, Folding, Complete }
    public enum ActionKind { Tick = 0, Accept = 1, Clarify = 2, BeginStroke = 3, Stroke = 4, EndStroke = 5, FinishBatter = 6, Flip = 7, MoveToPlate = 8, Portion = 9, Remove = 10, Discard = 11, BeginFold = 12, FoldGesture = 13, FinalizeFold = 14, NextGuest = 15, TutorialContinue = 16, ApplyHelper = 17 }

    // All simulation time is in 50 ms ticks, coordinates in [0,1000], money in hundredths.
    [Serializable]
    public sealed class BalanceRules
    {
        public string version = "core-dev-1";
        public int dayTicks = 4800;
        public int readyTicks = 100;
        public int burnTicks = 180;
        public int patiencePerPancakeTicks = 2200;
        public int clarifyPenaltyTicks = 160;
        public int guestIntervalTicks = 40;
        public int maxPancakes = 2;
        public int multiPancakeDay = 2;
        public int rent = 800;
        public int basePrice = 1200;
        public int ingredientPrice = 400;
        public int batterStampCost = 2;
        public int spreadStampCost = 2;
        public int portionCost = 15;
        public int ingredientUnlockCost = 1800;
        public int scraperPrice = 200;
        public int patienceUpgradeCost = 2400;
        public int helperHoneyPrice = 10;
        public int startingCoins = 3000;
        public int startingHoney = 0;
        public int refundThreshold = 45;
        public int requiredCoveragePercent = 90;
        public int spreadCoveragePercent = 72;
        public int brushRadius = 95;
        public int strokeSpacing = 35;
        public int portionTolerance = 140;
        public int maxPortions = 24;
        public BalanceRules Copy() => (BalanceRules)MemberwiseClone();

        public void Validate()
        {
            if (readyTicks < 1 || burnTicks <= readyTicks || dayTicks < 1 || patiencePerPancakeTicks < 1 ||
                maxPancakes < 1 || maxPancakes > 3 || brushRadius < 20 || brushRadius > 250 || strokeSpacing < 5 ||
                strokeSpacing > brushRadius || requiredCoveragePercent < 1 || requiredCoveragePercent > 100 ||
                spreadCoveragePercent < 1 || spreadCoveragePercent > 100 || rent < 0 || basePrice < 0 ||
                ingredientPrice < 0 || batterStampCost < 0 || spreadStampCost < 0 || portionCost < 0 ||
                ingredientUnlockCost < 0 || scraperPrice < 0 || patienceUpgradeCost < 0 || helperHoneyPrice < 0 ||
                startingCoins < 0 || startingHoney < 0 || clarifyPenaltyTicks < 0 || guestIntervalTicks < 0 ||
                refundThreshold < 0 || refundThreshold > 100 || maxPortions < 4 || maxPortions > 64 || portionTolerance < 1)
                throw new ArgumentException("Invalid gameplay balance configuration.");
        }
    }

    [Serializable]
    public sealed class Profile
    {
        public int day = 1;
        public int coins = 3000;
        public int honey;
        public int scrapers;
        public int ownedIngredients = 1;
        public int helpersOwned;
        public int helpersEnabled;
        public bool patienceUpgrade;
        public bool tutorialComplete;
        public bool demonstrationSeen;
        public int ratingTotal;
        public int ratingCount;
        public Profile Copy() => (Profile)MemberwiseClone();
        public bool Owns(Ingredient type) => (ownedIngredients & (1 << (int)type)) != 0;
        public bool HelperEnabled(Ingredient type) => (helpersOwned & helpersEnabled & (1 << (int)type)) != 0;
    }

    [Serializable]
    public sealed class GameCommand
    {
        public ActionKind kind;
        public int a, b, c, d;
        public GameCommand() { }
        public GameCommand(ActionKind kind, int a = 0, int b = 0, int c = 0, int d = 0)
        { this.kind = kind; this.a = a; this.b = b; this.c = c; this.d = d; }
        public GameCommand Copy() => new GameCommand(kind, a, b, c, d);
    }

    public struct Point
    {
        public int X, Y;
        public Point(int x, int y) { X = x; Y = y; }
        public static bool InCircle(int x, int y) => (long)(x - 500) * (x - 500) + (long)(y - 500) * (y - 500) <= 250000;
    }

    public static class Recipes
    {
        public static readonly Point[] PortionTargets = { new Point(320, 320), new Point(680, 320), new Point(320, 680), new Point(680, 680) };
        public static bool IsSpread(Ingredient type) => type <= Ingredient.Honey;
        public static string IngredientName(Ingredient type)
        {
            switch (type) { case Ingredient.Butter: return "масло"; case Ingredient.Jam: return "варенье"; case Ingredient.Honey: return "мёд"; case Ingredient.Curd: return "творог"; default: return "грибы"; }
        }
        public static string FoldName(Fold fold) => fold == Fold.Triangle ? "треугольник" : fold == Fold.Envelope ? "конверт" : fold == Fold.Roll ? "трубочка" : "неверная форма";
        public static Direction[] Gestures(Fold fold)
        {
            switch (fold) {
                case Fold.Triangle: return new[] { Direction.Up, Direction.Right };
                case Fold.Envelope: return new[] { Direction.Right, Direction.Left, Direction.Down, Direction.Up };
                case Fold.Roll: return new[] { Direction.Down, Direction.Down, Direction.Down };
                default: return new Direction[0];
            }
        }
    }

    public sealed class PortionSpec
    {
        public readonly int Ingredients;
        public readonly Fold Shape;
        public PortionSpec(int ingredients, Fold shape) { Ingredients = ingredients; Shape = shape; }
        public bool Needs(Ingredient type) => (Ingredients & (1 << (int)type)) != 0;
        public override string ToString()
        {
            var names = new List<string>();
            for (int i = 0; i < 5; i++) if (Needs((Ingredient)i)) names.Add(Recipes.IngredientName((Ingredient)i));
            return string.Join(" + ", names) + " · " + Recipes.FoldName(Shape);
        }
    }

    public sealed class OrderState
    {
        public readonly int Id, Visitor, Price, PatienceLimit;
        public readonly IReadOnlyList<PortionSpec> Portions;
        public readonly string Request;
        public OrderPhase Phase { get; internal set; }
        public int WaitTicks { get; internal set; }
        public bool Clarified { get; internal set; }
        public readonly List<PancakeState> Finished = new List<PancakeState>();
        public QualityReport Report { get; internal set; }
        public OrderState(int id, int visitor, List<PortionSpec> portions, int price, int patience)
        {
            Id = id; Visitor = visitor; Portions = portions.AsReadOnly(); Price = price; PatienceLimit = patience;
            var lines = new List<string>();
            for (int i = 0; i < portions.Count; i++) lines.Add((i + 1) + ". " + portions[i]);
            Request = "Здравствуйте! " + (portions.Count == 1 ? "Один блин, пожалуйста." : "Мне " + portions.Count + " блина, пожалуйста.") + "\n" + string.Join("\n", lines);
        }
    }

    public sealed class Ledger
    {
        public int Revenue, Tips, Refunds, Products, Rent;
        public int Profit => Revenue + Tips - Refunds - Products - Rent;
    }
}
