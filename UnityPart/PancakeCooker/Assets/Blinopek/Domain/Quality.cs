using System;
using System.Collections.Generic;

namespace Blinopek.Domain
{
    public sealed class QualityReport
    {
        public int Composition, Coverage, Cooking, Shape, Waiting, Quantity, Total, Tips, Refund;
        public readonly List<string> Reasons = new List<string>();
    }

    public static class QualityEvaluator
    {
        public static QualityReport Evaluate(OrderState order, BalanceRules rules)
        {
            // At most three pancakes. Exhaustive matching is small and independent of preparation order.
            int n = order.Portions.Count;
            int[] best = new int[n], candidate = new int[n];
            int bestScore = -1;
            Match(0, 0, 0, order, rules, candidate, best, ref bestScore);
            var report = new QualityReport { Quantity = Math.Min(n, order.Finished.Count) * 100 / n };
            for (int i = 0; i < n; i++)
            {
                if (best[i] >= order.Finished.Count) continue;
                var part = EvaluatePancake(order.Finished[best[i]], order.Portions[i], rules);
                report.Composition += part.Composition; report.Coverage += part.Coverage;
                report.Cooking += part.Cooking; report.Shape += part.Shape;
            }
            report.Composition /= n; report.Coverage /= n; report.Cooking /= n; report.Shape /= n;
            report.Waiting = Math.Max(0, 100 - order.WaitTicks * 100 / order.PatienceLimit);
            report.Total = (report.Composition * 40 + report.Cooking * 25 + report.Coverage * 15 + report.Shape * 15 + report.Quantity * 5) / 100;
            if (report.Composition < 85) report.Reasons.Add("Состав, количество или расположение начинки не совпали с просьбой.");
            if (report.Coverage < 90) report.Reasons.Add("В тесте остались дырки.");
            if (report.Cooking < 85) report.Reasons.Add("Одна из сторон недожарена или подгорела.");
            if (report.Shape < 100) report.Reasons.Add("Форма отличается от заказа.");
            if (report.Waiting < 35) report.Reasons.Add("Гость долго ждал.");
            if (report.Total < rules.refundThreshold) report.Refund = order.Price;
            else report.Tips = order.Price * report.Total * report.Waiting / 50000;
            return report;
        }
        private static void Match(int i, int used, int score, OrderState order, BalanceRules rules, int[] candidate, int[] best, ref int bestScore)
        {
            if (i == candidate.Length) { if (score > bestScore) { bestScore = score; Array.Copy(candidate, best, candidate.Length); } return; }
            for (int p = 0; p < candidate.Length; p++) if ((used & (1 << p)) == 0)
            {
                candidate[i] = p;
                int part = p < order.Finished.Count ? EvaluatePancake(order.Finished[p], order.Portions[i], rules).Total : 0;
                Match(i + 1, used | (1 << p), score + part, order, rules, candidate, best, ref bestScore);
            }
        }
        public static QualityReport EvaluatePancake(PancakeState pancake, PortionSpec spec, BalanceRules rules)
        {
            int composition = 0, types = 0;
            for (int i = 0; i < 5; i++)
            {
                bool expected = spec.Needs((Ingredient)i);
                var layer = pancake.Fillings[i];
                bool present = layer != null && layer.Dose > 0;
                if (!expected && !present) continue;
                types++;
                if (expected && present) composition += Recipes.IsSpread((Ingredient)i)
                    ? Math.Min(100, layer.Mask.Percent * 100 / rules.spreadCoveragePercent)
                    : PortionQuality(layer.Portions, rules.portionTolerance);
            }
            var r = new QualityReport {
                Composition = types == 0 ? 100 : composition / types,
                Coverage = Math.Min(100, pancake.Batter.Percent * 100 / rules.requiredCoveragePercent),
                Cooking = (HeatQuality(pancake.HeatA, rules) + HeatQuality(pancake.HeatB, rules)) / 2,
                Shape = pancake.ActualFold == spec.Shape ? 100 : 0
            };
            r.Total = r.Composition * 40 + r.Cooking * 25 + r.Coverage * 15 + r.Shape * 15;
            return r;
        }
        private static int PortionQuality(List<Point> portions, int tolerance)
        {
            int hits = 0, matched = MatchedPortionTargets(portions, tolerance);
            for (int i = 0; i < Recipes.PortionTargets.Length; i++) if ((matched & (1 << i)) != 0) hits++;
            return Math.Max(0, hits * 100 / Recipes.PortionTargets.Length - Math.Max(0, portions.Count - Recipes.PortionTargets.Length) * 15);
        }
        // The stencil and scoring must agree on which distinct positions are filled.
        public static int MatchedPortionTargets(IReadOnlyList<Point> portions, int tolerance)
        {
            int used = 0;
            if (portions == null) return used;
            foreach (var p in portions)
            {
                int best = -1, dist = tolerance * tolerance;
                for (int i = 0; i < Recipes.PortionTargets.Length; i++) if ((used & (1 << i)) == 0)
                {
                    var t = Recipes.PortionTargets[i]; int d = (p.X - t.X) * (p.X - t.X) + (p.Y - t.Y) * (p.Y - t.Y);
                    if (d <= dist) { best = i; dist = d; }
                }
                if (best >= 0) used |= 1 << best;
            }
            return used;
        }
        public static int HeatQuality(int heat, BalanceRules rules) => heat < rules.readyTicks ? heat * 60 / rules.readyTicks : heat >= rules.burnTicks ? 10 : 100;
    }
}
