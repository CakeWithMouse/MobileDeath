using System;
using System.Collections.Generic;

namespace Blinopek.Domain
{
    public sealed class DaySession
    {
        public const int TickMilliseconds = 50;
        public readonly BalanceRules Rules;
        public readonly Profile StartProfile;
        public readonly Ledger Ledger = new Ledger();
        public readonly List<QualityReport> Reviews = new List<QualityReport>();
        public DayPhase Phase { get; private set; }
        public OrderState Order { get; private set; }
        public PancakeState Pancake { get; private set; }
        public TutorialStep Tutorial { get; private set; }
        public int ElapsedTicks { get; private set; }
        public int Scrapers { get; private set; }
        public bool DemonstrationSeen { get; private set; }
        public bool TutorialCompleted { get; private set; }
        public string Feedback { get; private set; } = "Примите первый заказ.";
        public int Revision { get; private set; }
        public int Balance => StartProfile.coins + Ledger.Profit;
        public bool Success => Phase == DayPhase.Result && Balance >= 0;
        public bool IsTutorial => !TutorialCompleted && Order != null && Order.Id == 1;
        public bool HasActiveOrder => Order != null && Order.Phase == OrderPhase.Accepted;
        public bool CanOfferNext => Phase == DayPhase.Serving && Order != null && (Order.Phase == OrderPhase.Served || Order.Phase == OrderPhase.Cancelled) && betweenGuests >= Rules.guestIntervalTicks;
        private uint random;
        private int guestNumber, pancakeNumber, betweenGuests;
        private StrokeSampler stroke;

        public DaySession(Profile profile, BalanceRules rules, int seed)
        {
            rules.Validate(); Rules = rules; StartProfile = profile.Copy(); random = (uint)seed;
            if (random == 0) random = 1;
            Scrapers = profile.scrapers; TutorialCompleted = profile.tutorialComplete; DemonstrationSeen = profile.demonstrationSeen;
            Tutorial = TutorialCompleted ? TutorialStep.Complete : TutorialStep.Welcome;
            OfferOrder();
        }
        private int Random(int max) { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return (int)(random % (uint)max); }
        private void OfferOrder()
        {
            guestNumber++;
            var portions = new List<PortionSpec>();
            bool lesson = !TutorialCompleted && guestNumber == 1;
            int count = lesson || StartProfile.day < Rules.multiPancakeDay ? 1 : 1 + Random(Rules.maxPancakes);
            var owned = new List<int>();
            for (int i = 0; i < 5; i++) if (StartProfile.Owns((Ingredient)i)) owned.Add(i);
            if (owned.Count == 0) throw new InvalidOperationException("A profile must own at least one ingredient.");
            int price = 0;
            for (int i = 0; i < count; i++)
            {
                int bits = lesson ? 1 : 1 << owned[Random(owned.Count)];
                if (!lesson && StartProfile.day >= 3 && owned.Count > 1 && Random(3) == 0) bits |= 1 << owned[Random(owned.Count)];
                Fold shape = lesson || StartProfile.day == 1 ? Fold.Triangle : (Fold)Random(3);
                portions.Add(new PortionSpec(bits, shape));
                price += Rules.basePrice;
                for (int j = 0; j < 5; j++) if ((bits & (1 << j)) != 0) price += Rules.ingredientPrice;
            }
            int patience = Rules.patiencePerPancakeTicks * count;
            if (StartProfile.patienceUpgrade) patience = patience * 3 / 2;
            Order = new OrderState(guestNumber, (guestNumber - 1) % 6, portions, price, patience);
            Feedback = "Гость ждёт у стойки.";
        }

        public bool Execute(GameCommand command)
        {
            bool accepted;
            if (command.kind == ActionKind.Tick)
            {
                if (command.a < 1 || command.a > 12000 || Phase == DayPhase.Result) return false;
                for (int i = 0; i < command.a && Phase != DayPhase.Result; i++) Tick();
                Revision++; return true;
            }
            if (Phase == DayPhase.Result) return false;
            switch (command.kind)
            {
                case ActionKind.Accept:
                    accepted = Phase == DayPhase.Serving && Order.Phase == OrderPhase.Offered;
                    if (accepted) { Order.Phase = OrderPhase.Accepted; if (!IsTutorial) Ledger.Revenue += Order.Price; Tutorial = IsTutorial ? TutorialStep.Batter : TutorialStep.Complete; Feedback = "Намажьте тесто по кругу сковороды."; }
                    break;
                case ActionKind.Clarify:
                    accepted = (Order.Phase == OrderPhase.Offered || HasActiveOrder) && !Order.Clarified;
                    if (accepted) { Order.Clarified = true; if (!IsTutorial) { Order.WaitTicks += Rules.clarifyPenaltyTicks; if (HasActiveOrder && Order.WaitTicks >= Order.PatienceLimit) CancelOrder(); } Feedback = "В каждой строке — начинка и форма одного блина. Уточнение уменьшает терпение."; }
                    break;
                case ActionKind.BeginStroke: accepted = BeginStroke(command.a, command.b, command.c); break;
                case ActionKind.Stroke:
                    accepted = stroke != null && ValidCoordinate(command.a, command.b);
                    if (accepted) stroke.Move(command.a, command.b);
                    break;
                case ActionKind.EndStroke: accepted = stroke != null; stroke = null; break;
                case ActionKind.FinishBatter:
                    accepted = HasActiveOrder && Pancake != null && Pancake.Stage == PancakeStage.Batter && Pancake.Batter.Count > 0;
                    if (accepted && IsTutorial && Pancake.Batter.Percent < Rules.requiredCoveragePercent) return Reject("Закройте дырки в тесте: нужно хотя бы " + Rules.requiredCoveragePercent + "%.");
                    if (accepted) { stroke = null; Pancake.Stage = PancakeStage.SideA; if (IsTutorial) Tutorial = DemonstrationSeen ? TutorialStep.SideA : TutorialStep.Demonstration; Feedback = IsTutorial && !DemonstrationSeen ? "Посмотрим, что случится, если передержать блин…" : "Первая сторона жарится. На готовности коснитесь блина или нажмите «Перевернуть»."; }
                    break;
                case ActionKind.Flip:
                    accepted = Pancake != null && Pancake.Stage == PancakeStage.SideA && HasActiveOrder;
                    if (accepted && IsTutorial && (!DemonstrationSeen || Pancake.HeatA < Rules.readyTicks)) return Reject("Дождитесь готовности. Учебную подгоревшую пробу нужно выбросить.");
                    if (accepted) { Pancake.Stage = PancakeStage.SideB; if (IsTutorial) Tutorial = TutorialStep.SideB; Feedback = "Жарится вторая сторона. Затем перенесите блин на тарелку."; }
                    break;
                case ActionKind.MoveToPlate:
                    accepted = Pancake != null && Pancake.Stage == PancakeStage.SideB && Pancake.HeatB >= Rules.readyTicks && HasActiveOrder;
                    if (accepted) { Pancake.Stage = PancakeStage.Plate; if (IsTutorial) Tutorial = TutorialStep.Filling; Feedback = "Выберите начинку: намажьте её или перетащите порции на отметки."; }
                    else return Reject("Сначала обжарьте вторую сторону.");
                    break;
                case ActionKind.Portion: accepted = PlacePortion(command.a, command.b, command.c); break;
                case ActionKind.ApplyHelper: accepted = ApplyHelper(command.a); break;
                case ActionKind.Remove:
                    accepted = OnPlate() && command.a >= 0 && command.a < 5 && Pancake.Fillings[command.a] != null && Scrapers > 0;
                    if (accepted) { stroke = null; Pancake.Fillings[command.a] = null; Scrapers--; Feedback = "Вся выбранная начинка удалена. Один скребок израсходован."; }
                    else return Reject("Нужны скребок и нанесённая начинка. После начала складывания исправление недоступно.");
                    break;
                case ActionKind.Discard:
                    accepted = Pancake != null && HasActiveOrder && Pancake.Stage < PancakeStage.Finished;
                    if (accepted) { if (IsTutorial && Tutorial == TutorialStep.Discard) DemonstrationSeen = true; Pancake = null; stroke = null; if (IsTutorial) Tutorial = DemonstrationSeen ? TutorialStep.Retry : TutorialStep.Batter; Feedback = "Сковорода свободна. Можно начать блин заново."; }
                    break;
                case ActionKind.BeginFold:
                    accepted = OnPlate() && command.a >= 0 && command.a < 3;
                    if (accepted && IsTutorial && (command.a != 0 || Pancake.IngredientBits != 1 || Pancake.Fillings[0].Mask.Percent < Rules.spreadCoveragePercent)) return Reject("Для первого гостя намажьте масло и выберите треугольник.");
                    if (accepted) { stroke = null; Pancake.Stage = PancakeStage.Folding; Pancake.IntendedFold = (Fold)command.a; if (IsTutorial) Tutorial = TutorialStep.Folding; Feedback = "Проведите по блину в направлении стрелки."; }
                    break;
                case ActionKind.FoldGesture: accepted = FoldGesture(command.a, command.b); break;
                case ActionKind.FinalizeFold:
                    accepted = Pancake != null && Pancake.Stage == PancakeStage.Folding && !IsTutorial;
                    if (accepted) FinishPancake();
                    break;
                case ActionKind.NextGuest:
                    accepted = CanOfferNext;
                    if (accepted) OfferOrder();
                    break;
                default: accepted = false; break;
            }
            if (accepted) Revision++;
            return accepted;
        }
        private bool Reject(string message) { Feedback = message; Revision++; return false; }
        private static bool ValidCoordinate(int x, int y) => x >= 0 && x <= 1000 && y >= 0 && y <= 1000;
        private bool OnPlate() => HasActiveOrder && Pancake != null && Pancake.Stage == PancakeStage.Plate;
        private void Spend(int amount) { if (!IsTutorial) Ledger.Products += amount; }
        private bool BeginStroke(int type, int x, int y)
        {
            if (stroke != null || !HasActiveOrder || !ValidCoordinate(x, y) || !Point.InCircle(x, y)) return false;
            if (type == -1)
            {
                if (Pancake == null) Pancake = new PancakeState(++pancakeNumber);
                if (Pancake.Stage != PancakeStage.Batter) return false;
                stroke = new StrokeSampler(x, y, Rules.strokeSpacing, (u, v) => { if (!Point.InCircle(u, v)) return; Pancake.Batter.Stamp(u, v, Rules.brushRadius); Spend(Rules.batterStampCost); });
                return true;
            }
            if (!OnPlate() || type < 0 || type > 2 || !StartProfile.Owns((Ingredient)type)) return false;
            var layer = Pancake.Fillings[type] ?? (Pancake.Fillings[type] = new IngredientLayer());
            stroke = new StrokeSampler(x, y, Rules.strokeSpacing, (u, v) => { if (!Point.InCircle(u, v)) return; layer.Mask.Stamp(u, v, Rules.brushRadius); layer.Dose++; Spend(Rules.spreadStampCost); });
            return true;
        }
        private bool PlacePortion(int type, int x, int y)
        {
            if (!OnPlate() || type < 3 || type > 4 || !StartProfile.Owns((Ingredient)type) || !ValidCoordinate(x, y) || !Point.InCircle(x, y)) return false;
            var layer = Pancake.Fillings[type] ?? (Pancake.Fillings[type] = new IngredientLayer());
            if (layer.Portions.Count >= Rules.maxPortions) return Reject("На блине уже слишком много порций этой начинки.");
            layer.Portions.Add(new Point(x, y)); layer.Dose++; Spend(Rules.portionCost); return true;
        }
        private bool ApplyHelper(int type)
        {
            if (!OnPlate() || type < 0 || type > 4 || !StartProfile.Owns((Ingredient)type) || !StartProfile.HelperEnabled((Ingredient)type)) return false;
            if (type >= 3) { foreach (var p in Recipes.PortionTargets) PlacePortion(type, p.X, p.Y); return true; }
            // Identical strokes and cost calculation to a human moving along these rows.
            for (int y = 80; y < 1000; y += 130)
            {
                int edge = (int)Math.Sqrt(250000 - (y - 500) * (y - 500));
                if (BeginStroke(type, 501 - edge, y)) { stroke.Move(499 + edge, y); stroke = null; }
            }
            return true;
        }
        private bool FoldGesture(int dx, int dy)
        {
            if (Pancake == null || Pancake.Stage != PancakeStage.Folding || Math.Abs((long)dx) > 1000 || Math.Abs((long)dy) > 1000) return false;
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < 160) return Reject("Проведите дальше через блин.");
            var direction = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? Direction.Right : Direction.Left) : (dy > 0 ? Direction.Up : Direction.Down);
            var expected = Recipes.Gestures(Pancake.IntendedFold);
            bool correct = Pancake.Folds.Count < expected.Length && expected[Pancake.Folds.Count] == direction;
            if (!correct && IsTutorial) return Reject("Попробуйте ещё раз по стрелке. В уроке неверный сгиб не портит блин.");
            Pancake.Folds.Add(direction); Pancake.FoldError |= !correct;
            if (Pancake.Folds.Count >= expected.Length) { if (!Pancake.FoldError) Pancake.ActualFold = Pancake.IntendedFold; FinishPancake(); }
            return true;
        }
        private void FinishPancake()
        {
            Pancake.Stage = PancakeStage.Finished; Order.Finished.Add(Pancake); Pancake = null; stroke = null;
            if (Order.Finished.Count < Order.Portions.Count) { Feedback = "Порция готова и ждёт на подносе. Приготовьте следующую."; return; }
            bool lesson = IsTutorial;
            Order.Report = QualityEvaluator.Evaluate(Order, Rules);
            if (lesson) { Ledger.Revenue += Order.Price; TutorialCompleted = true; Tutorial = TutorialStep.Complete; }
            Ledger.Tips += Order.Report.Tips; Ledger.Refunds += Order.Report.Refund;
            Order.Phase = OrderPhase.Served; Reviews.Add(Order.Report); betweenGuests = 0;
            Feedback = lesson ? "Отлично! Теперь обычная смена: следите за жаркой, временем и расходами." : Order.Report.Refund > 0 ? "Гость недоволен и просит вернуть оплату." : "Заказ выдан целиком. Спасибо!";
            if (Phase == DayPhase.Closing) FinishDay();
        }
        private void Tick()
        {
            if (IsTutorial)
            {
                if (Pancake != null && Tutorial == TutorialStep.Demonstration)
                { Pancake.HeatA += 3; if (Pancake.HeatA >= Rules.burnTicks) { Tutorial = TutorialStep.Discard; Feedback = "Первый блин комом! Перетащите подгоревший блин в миску справа."; } }
                else if (Pancake != null && Pancake.Stage == PancakeStage.SideA && Tutorial == TutorialStep.SideA) Pancake.HeatA = Math.Min(Rules.readyTicks, Pancake.HeatA + 1);
                else if (Pancake != null && Pancake.Stage == PancakeStage.SideB) Pancake.HeatB = Math.Min(Rules.readyTicks, Pancake.HeatB + 1);
                return;
            }
            ElapsedTicks++;
            if (HasActiveOrder)
            {
                Order.WaitTicks++;
                if (Order.WaitTicks >= Order.PatienceLimit) CancelOrder();
            }
            else betweenGuests++;
            if (Pancake != null)
            {
                if (Pancake.Stage == PancakeStage.SideA) Pancake.HeatA++;
                if (Pancake.Stage == PancakeStage.SideB) Pancake.HeatB++;
            }
            if (ElapsedTicks >= Rules.dayTicks)
            {
                Phase = DayPhase.Closing;
                if (!HasActiveOrder) FinishDay();
            }
        }
        private void CancelOrder()
        {
            if (!HasActiveOrder) return;
            Ledger.Refunds += Order.Price; Order.Phase = OrderPhase.Cancelled;
            Order.Report = new QualityReport { Refund = Order.Price };
            Order.Report.Reasons.Add("Терпение закончилось. Гость ушёл, оплата возвращена."); Reviews.Add(Order.Report);
            Pancake = null; stroke = null; betweenGuests = 0; Feedback = "Гость ушёл. Продукты остаются расходом.";
        }
        private void FinishDay()
        {
            if (Phase == DayPhase.Result) return;
            int total = StartProfile.ratingTotal, count = StartProfile.ratingCount;
            foreach (var review in Reviews) { total += review.Total; count++; }
            int rating = count == 0 ? 70 : total / count;
            Ledger.Rent = Rules.rent * (150 - rating) / 100;
            Phase = DayPhase.Result; stroke = null; Pancake = null;
        }
    }
}
