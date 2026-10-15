using System;
using System.Collections.Generic;

namespace Blinopek.Domain
{
    public readonly struct CoverageStamp
    {
        public readonly int X, Y, Radius;
        public CoverageStamp(int x, int y, int radius) { X = x; Y = y; Radius = radius; }
    }

    public sealed class CoverageMask
    {
        public const int Size = 128;
        private readonly bool[] cells = new bool[Size * Size];
        private readonly List<CoverageStamp> stamps = new List<CoverageStamp>();
        // Retain the original brush geometry for rendering; scoring still uses cells.
        // Existing saves rebuild these from their command journal during replay.
        public IReadOnlyList<CoverageStamp> Stamps => stamps;
        public int Count { get; private set; }
        public int Revision { get; private set; }
        public static readonly int CircleCells = CountCircle();
        public int Percent => Count * 100 / CircleCells;
        public bool this[int x, int y] => cells[y * Size + x];
        private static int CountCircle()
        {
            int count = 0;
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                if (Point.InCircle((x * 1000 + 500) / Size, (y * 1000 + 500) / Size)) count++;
            return count;
        }
        public void Stamp(int px, int py, int radius)
        {
            stamps.Add(new CoverageStamp(px, py, radius));
            int x0 = Math.Max(0, (px - radius) * Size / 1000 - 1), x1 = Math.Min(Size - 1, (px + radius) * Size / 1000 + 1);
            int y0 = Math.Max(0, (py - radius) * Size / 1000 - 1), y1 = Math.Min(Size - 1, (py + radius) * Size / 1000 + 1);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                int u = (x * 1000 + 500) / Size, v = (y * 1000 + 500) / Size;
                if (Point.InCircle(u, v) && (u - px) * (u - px) + (v - py) * (v - py) <= radius * radius && !cells[y * Size + x])
                { cells[y * Size + x] = true; Count++; }
            }
            Revision++;
        }
    }

    public sealed class IngredientLayer
    {
        public readonly CoverageMask Mask = new CoverageMask();
        public readonly List<Point> Portions = new List<Point>();
        public int Dose;
    }

    public sealed class PancakeState
    {
        public readonly int Id;
        public readonly bool IsLesson;
        public PancakeStage Stage { get; internal set; }
        public readonly CoverageMask Batter = new CoverageMask();
        public readonly IngredientLayer[] Fillings = new IngredientLayer[5];
        public int HeatA { get; internal set; }
        public int HeatB { get; internal set; }
        public Fold IntendedFold { get; internal set; }
        public Fold ActualFold { get; internal set; } = Fold.Incorrect;
        public readonly List<Direction> Folds = new List<Direction>();
        public bool FoldError { get; internal set; }
        public PancakeState(int id, bool isLesson = false) { Id = id; IsLesson = isLesson; }
        public int IngredientBits
        {
            get { int bits = 0; for (int i = 0; i < 5; i++) if (Fillings[i] != null && Fillings[i].Dose > 0) bits |= 1 << i; return bits; }
        }
    }

    // A gesture carries residual distance, so splitting a line across frames never creates extra doses.
    public sealed class StrokeSampler
    {
        private int lastX, lastY;
        private double distanceToNext;
        private readonly int spacing;
        private readonly Action<int, int> stamp;
        public StrokeSampler(int x, int y, int spacing, Action<int, int> stamp)
        { lastX = x; lastY = y; this.spacing = spacing; this.stamp = stamp; distanceToNext = spacing; stamp(x, y); }
        public void Move(int x, int y)
        {
            double dx = x - lastX, dy = y - lastY, distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance == 0) return;
            double next = distanceToNext;
            while (next <= distance + 0.000001)
            {
                stamp((int)Math.Round(lastX + dx * next / distance), (int)Math.Round(lastY + dy * next / distance));
                next += spacing;
            }
            distanceToNext = next - distance; lastX = x; lastY = y;
        }
    }
}
