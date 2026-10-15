using System;

namespace Blinopek.Domain
{
    public sealed partial class DaySession
    {
        private bool MoveStroke(int x, int y)
        {
            if (stroke == null || !ValidCoordinate(x, y))
                return false;
            stroke.Move(x, y);
            return true;
        }

        private bool EndStroke()
        {
            bool active = stroke != null;
            ClearStroke();
            return active;
        }

        private bool FinishBatter()
        {
            var pancake = PanPancake;
            if (!CanCook || pancake == null || pancake.Stage != PancakeStage.Batter || pancake.Batter.Count == 0)
                return false;
            if (IsLessonPancake(pancake) && pancake.Batter.Percent < Rules.requiredCoveragePercent)
                return Reject("Закройте дырки в тесте: нужно хотя бы " + Rules.requiredCoveragePercent + "%.");
            ClearStroke(pancake);
            pancake.Stage = PancakeStage.SideA;
            if (IsLessonPancake(pancake))
                Tutorial = DemonstrationSeen ? TutorialStep.SideA : TutorialStep.Demonstration;
            Feedback = IsLessonPancake(pancake) && !DemonstrationSeen ? "Посмотрим, что случится, если передержать блин…" : "Первая сторона жарится. На готовности коснитесь блина или нажмите «Перевернуть».";
            return true;
        }

        private bool Flip()
        {
            var pancake = PanPancake;
            if (pancake == null || pancake.Stage != PancakeStage.SideA || !CanCook)
                return false;
            if (IsLessonPancake(pancake) && (!DemonstrationSeen || pancake.HeatA < Rules.readyTicks))
                return Reject("Дождитесь готовности. Учебную подгоревшую пробу нужно выбросить.");
            pancake.Stage = PancakeStage.SideB;
            if (IsLessonPancake(pancake))
                Tutorial = TutorialStep.SideB;
            Feedback = "Жарится вторая сторона. Затем перенесите блин на тарелку.";
            return true;
        }

        private bool MoveToPlate()
        {
            if (PlatePancake != null)
                return Reject("Сначала освободите тарелку: закончите или выбросьте лежащий на ней блин.");
            if (PanPancake == null || PanPancake.Stage != PancakeStage.SideB || PanPancake.HeatB < Rules.readyTicks || !CanCook)
                return Reject("Сначала обжарьте вторую сторону.");
            ClearStroke(PanPancake);
            PlatePancake = PanPancake;
            PanPancake = null;
            PlatePancake.Stage = PancakeStage.Plate;
            if (IsLessonPancake(PlatePancake))
                Tutorial = TutorialStep.Filling;
            Feedback = "Выберите начинку. Сковорода свободна — можно наливать следующий блин.";
            return true;
        }

        private bool RemoveIngredient(int type)
        {
            if (!OnPlate() || type < 0 || type >= 5 || PlatePancake.Fillings[type] == null || Scrapers <= 0)
                return Reject("Нужны скребок и нанесённая начинка. После начала складывания исправление недоступно.");
            ClearStroke(PlatePancake);
            PlatePancake.Fillings[type] = null;
            Scrapers--;
            Feedback = "Вся выбранная начинка удалена. Один скребок израсходован.";
            return true;
        }

        private bool BeginFold(int shape)
        {
            if (!OnPlate() || shape < 0 || shape >= 3)
                return false;
            if (IsLessonPancake(PlatePancake) && (shape != 0 || PlatePancake.IngredientBits != 1 || PlatePancake.Fillings[0].Mask.Percent < Rules.spreadCoveragePercent))
                return Reject("Для первого гостя намажьте масло и выберите треугольник.");
            ClearStroke(PlatePancake);
            PlatePancake.Stage = PancakeStage.Folding;
            PlatePancake.IntendedFold = (Fold)shape;
            if (IsLessonPancake(PlatePancake))
                Tutorial = TutorialStep.Folding;
            Feedback = "Проведите по блину в направлении стрелки.";
            return true;
        }

        private bool FinalizeFold()
        {
            if (PlatePancake == null || PlatePancake.Stage != PancakeStage.Folding || IsLessonPancake(PlatePancake))
                return false;
            FinishPancake();
            return true;
        }

        private static bool ValidCoordinate(int x, int y) => x >= 0 && x <= 1000 && y >= 0 && y <= 1000;
        private bool OnPlate() => CanCook && PlatePancake != null && PlatePancake.Stage == PancakeStage.Plate;
        private void Spend(PancakeState pancake, int amount)
        {
            if (!IsLessonPancake(pancake))
                Ledger.Products += amount;
        }

        private void ClearStroke(PancakeState target = null)
        {
            if (target != null && strokePancake != target)
                return;
            stroke = null;
            strokePancake = null;
        }

        private bool Discard(int id)
        {
            // Zero preserves old journal commands; pointer drops specify the exact pancake.
            var target = id == 0 ? Pancake : PanPancake?.Id == id ? PanPancake : PlatePancake?.Id == id ? PlatePancake : FindTrayPancake(id);
            if (!CanCook || target == null)
                return false;
            bool lesson = IsLessonPancake(target);
            if (lesson && Tutorial == TutorialStep.Discard)
                DemonstrationSeen = true;
            ClearStroke(target);
            if (target == PanPancake)
                PanPancake = null;
            else if (target == PlatePancake)
                PlatePancake = null;
            else
            {
                if (!ReadyPancakes.Remove(target))
                    Order.Finished.Remove(target);
                if (LastFinished == target)
                    LastFinished = null;
                if (FreeCooking)
                    AssignReadyPancakes();
            }

            if (lesson)
                Tutorial = DemonstrationSeen ? TutorialStep.Retry : TutorialStep.Batter;
            Feedback = "Блин выброшен. Можно продолжать готовку.";
            return true;
        }

        public PancakeState FindTrayPancake(int id)
        {
            if (Phase == DayPhase.Result)
                return null;
            if (HasActiveOrder)
                foreach (var pancake in Order.Finished)
                    if (pancake.Id == id)
                        return pancake;
            return ReadyPancakes.Find(pancake => pancake.Id == id);
        }

        public PancakeState TrayPancakeAt(int index)
        {
            if (index < 0 || index >= TrayCount)
                return null;
            int reserved = HasActiveOrder ? Order.Finished.Count : 0;
            return index < reserved ? Order.Finished[index] : ReadyPancakes[index - reserved];
        }

        private bool BeginStroke(int type, int x, int y)
        {
            if (stroke != null || !CanCook || !ValidCoordinate(x, y) || !Point.InCircle(x, y))
                return false;
            if (type == -1)
            {
                if (!FreeCooking && PlatePancake != null)
                    return false;
                if (PanPancake == null)
                    PanPancake = new PancakeState(++pancakeNumber, IsTutorial && HasActiveOrder && PlatePancake == null);
                if (PanPancake.Stage != PancakeStage.Batter)
                    return false;
                var pancake = PanPancake;
                strokePancake = pancake;
                stroke = new StrokeSampler(x, y, Rules.strokeSpacing, (u, v) =>
                {
                    if (!Point.InCircle(u, v))
                        return;
                    pancake.Batter.Stamp(u, v, Rules.brushRadius);
                    Spend(pancake, Rules.batterStampCost);
                });
                return true;
            }

            if (!OnPlate() || type < 0 || type > 2 || !StartProfile.Owns((Ingredient)type))
                return false;
            var plate = PlatePancake;
            strokePancake = plate;
            var layer = plate.Fillings[type] ?? (plate.Fillings[type] = new IngredientLayer());
            stroke = new StrokeSampler(x, y, Rules.strokeSpacing, (u, v) =>
            {
                if (!Point.InCircle(u, v))
                    return;
                layer.Mask.Stamp(u, v, Rules.brushRadius);
                layer.Dose++;
                Spend(plate, Rules.spreadStampCost);
            });
            return true;
        }

        private bool PlacePortion(int type, int x, int y)
        {
            if (!OnPlate() || type < 3 || type > 4 || !StartProfile.Owns((Ingredient)type) || !ValidCoordinate(x, y) || !Point.InCircle(x, y))
                return false;
            var layer = PlatePancake.Fillings[type] ?? (PlatePancake.Fillings[type] = new IngredientLayer());
            if (layer.Portions.Count >= Rules.maxPortions)
                return Reject("На блине уже слишком много порций этой начинки.");
            layer.Portions.Add(new Point(x, y));
            layer.Dose++;
            Spend(PlatePancake, Rules.portionCost);
            return true;
        }

        private bool ApplyHelper(int type)
        {
            if (!OnPlate() || type < 0 || type > 4 || !StartProfile.Owns((Ingredient)type) || !StartProfile.HelperEnabled((Ingredient)type))
                return false;
            if (type >= 3)
            {
                foreach (var p in Recipes.PortionTargets)
                    PlacePortion(type, p.X, p.Y);
                return true;
            }

            // Identical strokes and cost calculation to a human moving along these rows.
            for (int y = 80; y < 1000; y += 130)
            {
                int edge = (int)Math.Sqrt(250000 - (y - 500) * (y - 500));
                if (BeginStroke(type, 501 - edge, y))
                {
                    stroke.Move(499 + edge, y);
                    ClearStroke();
                }
            }

            return true;
        }

        private bool FoldGesture(int dx, int dy)
        {
            var pancake = PlatePancake;
            if (pancake == null || pancake.Stage != PancakeStage.Folding || Math.Abs((long)dx) > 1000 || Math.Abs((long)dy) > 1000)
                return false;
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < 160)
                return Reject("Проведите дальше через блин.");
            var direction = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? Direction.Right : Direction.Left) : (dy > 0 ? Direction.Up : Direction.Down);
            var expected = Recipes.Gestures(pancake.IntendedFold);
            bool correct = pancake.Folds.Count < expected.Length && expected[pancake.Folds.Count] == direction;
            if (!correct && IsLessonPancake(pancake))
                return Reject("Попробуйте ещё раз по стрелке. В уроке неверный сгиб не портит блин.");
            pancake.Folds.Add(direction);
            pancake.FoldError |= !correct;
            if (pancake.Folds.Count >= expected.Length)
            {
                if (!pancake.FoldError)
                    pancake.ActualFold = pancake.IntendedFold;
                FinishPancake();
            }

            return true;
        }

        private void FinishPancake()
        {
            var pancake = PlatePancake;
            pancake.Stage = PancakeStage.Finished;
            LastFinished = pancake;
            FinishedCount++;
            if (FreeCooking)
                ReadyPancakes.Add(pancake);
            else
                Order.Finished.Add(pancake);
            PlatePancake = null;
            ClearStroke(pancake);
            if (FreeCooking)
            {
                Feedback = "Блин готов и ждёт на подносе. Можно готовить следующий.";
                AssignReadyPancakes();
                return;
            }

            if (Order.Finished.Count < Order.Portions.Count)
            {
                Feedback = "Порция готова и ждёт на подносе. Приготовьте следующую.";
                return;
            }

            if (!ManualServing)
                CompleteOrder();
            else
                Feedback = "Заказ готов. Нажмите на поднос или замените неудачную порцию.";
        }
    }
}
