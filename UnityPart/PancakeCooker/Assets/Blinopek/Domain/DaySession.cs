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
        public PancakeState PanPancake { get; private set; }
        public PancakeState PlatePancake { get; private set; }
        // Convenience for single-pancake callers. Cooking commands always address a station.
        public PancakeState Pancake => PanPancake ?? PlatePancake;
        public readonly List<PancakeState> ReadyPancakes = new List<PancakeState>();
        public PancakeState LastFinished { get; private set; }
        public int FinishedCount { get; private set; }
        public bool FreeCooking { get; private set; }
        public bool ManualServing { get; private set; }
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
        public bool CanCook => Phase != DayPhase.Result && (FreeCooking || HasActiveOrder);
        public bool IsCookingLesson => IsLessonPancake(Pancake);
        public bool IsLessonPancake(PancakeState pancake) => IsTutorial && pancake != null && pancake.IsLesson;
        public int TrayCount => ReadyPancakes.Count + (HasActiveOrder ? Order.Finished.Count : 0);
        public bool CanServeOrder => Phase != DayPhase.Result && HasActiveOrder && Order.Finished.Count == Order.Portions.Count;
        public bool GuestLeaving => Order != null && (Order.Phase == OrderPhase.Served || Order.Phase == OrderPhase.Cancelled || (Phase == DayPhase.Closing && Order.Phase == OrderPhase.Offered));
        public float GuestDepartureProgress => GuestLeaving ? Math.Min(1f, (float)betweenGuests / Math.Max(1, Rules.guestIntervalTicks)) : 0;
        public bool GuestHasLeft => GuestLeaving && betweenGuests >= Math.Max(1, Rules.guestIntervalTicks);
        public bool CanOfferNext => Phase == DayPhase.Serving && Order != null && (Order.Phase == OrderPhase.Served || Order.Phase == OrderPhase.Cancelled) && (ManualServing ? GuestHasLeft : betweenGuests >= Rules.guestIntervalTicks);
        private uint random;
        private int guestNumber, pancakeNumber, betweenGuests;
        private StrokeSampler stroke;
        private PancakeState strokePancake;

        public DaySession(Profile profile, BalanceRules rules, int seed, bool freeCooking = true, bool manualServing = true)
        {
            FreeCooking = freeCooking; ManualServing = manualServing;
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
                case ActionKind.EnableFreeCooking:
                    // Persist the transition after replaying older logs under their original rules.
                    accepted = !FreeCooking;
                    if (accepted) FreeCooking = true;
                    break;
                case ActionKind.EnableManualServing:
                    accepted = !ManualServing;
                    if (accepted) ManualServing = true;
                    break;
                case ActionKind.ServeOrder:
                    accepted = CanServeOrder;
                    if (accepted) {
                        // The explicit action hands over the whole tray, including extra ready portions.
                        Order.Finished.AddRange(ReadyPancakes); ReadyPancakes.Clear();
                        CompleteOrder();
                    }
                    else return Reject("Сначала примите заказ и приготовьте все порции.");
                    break;
                case ActionKind.Accept:
                    accepted = Phase == DayPhase.Serving && Order.Phase == OrderPhase.Offered;
                    if (accepted) {
                        Order.Phase = OrderPhase.Accepted; if (!IsTutorial) Ledger.Revenue += Order.Price;
                        Tutorial = IsTutorial ? TutorialStep.Batter : TutorialStep.Complete;
                        Feedback = Pancake == null ? "Можно приготовить новый блин или выдать готовые порции с подноса." : "Заказ принят. Продолжайте готовить начатый блин.";
                        if (FreeCooking) ServeReadyPancakes();
                    }
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
                case ActionKind.EndStroke: accepted = stroke != null; ClearStroke(); break;
                case ActionKind.FinishBatter:
                    accepted = CanCook && PanPancake != null && PanPancake.Stage == PancakeStage.Batter && PanPancake.Batter.Count > 0;
                    if (accepted && IsLessonPancake(PanPancake) && PanPancake.Batter.Percent < Rules.requiredCoveragePercent) return Reject("Закройте дырки в тесте: нужно хотя бы " + Rules.requiredCoveragePercent + "%.");
                    if (accepted) { ClearStroke(PanPancake); PanPancake.Stage = PancakeStage.SideA; if (IsLessonPancake(PanPancake)) Tutorial = DemonstrationSeen ? TutorialStep.SideA : TutorialStep.Demonstration; Feedback = IsLessonPancake(PanPancake) && !DemonstrationSeen ? "Посмотрим, что случится, если передержать блин…" : "Первая сторона жарится. На готовности коснитесь блина или нажмите «Перевернуть»."; }
                    break;
                case ActionKind.Flip:
                    accepted = PanPancake != null && PanPancake.Stage == PancakeStage.SideA && CanCook;
                    if (accepted && IsLessonPancake(PanPancake) && (!DemonstrationSeen || PanPancake.HeatA < Rules.readyTicks)) return Reject("Дождитесь готовности. Учебную подгоревшую пробу нужно выбросить.");
                    if (accepted) { PanPancake.Stage = PancakeStage.SideB; if (IsLessonPancake(PanPancake)) Tutorial = TutorialStep.SideB; Feedback = "Жарится вторая сторона. Затем перенесите блин на тарелку."; }
                    break;
                case ActionKind.MoveToPlate:
                    if (PlatePancake != null) return Reject("Сначала освободите тарелку: закончите или выбросьте лежащий на ней блин.");
                    accepted = PanPancake != null && PanPancake.Stage == PancakeStage.SideB && PanPancake.HeatB >= Rules.readyTicks && CanCook;
                    if (accepted) { ClearStroke(PanPancake); PlatePancake = PanPancake; PanPancake = null; PlatePancake.Stage = PancakeStage.Plate; if (IsLessonPancake(PlatePancake)) Tutorial = TutorialStep.Filling; Feedback = "Выберите начинку. Сковорода свободна — можно наливать следующий блин."; }
                    else return Reject("Сначала обжарьте вторую сторону.");
                    break;
                case ActionKind.Portion: accepted = PlacePortion(command.a, command.b, command.c); break;
                case ActionKind.ApplyHelper: accepted = ApplyHelper(command.a); break;
                case ActionKind.Remove:
                    accepted = OnPlate() && command.a >= 0 && command.a < 5 && PlatePancake.Fillings[command.a] != null && Scrapers > 0;
                    if (accepted) { ClearStroke(PlatePancake); PlatePancake.Fillings[command.a] = null; Scrapers--; Feedback = "Вся выбранная начинка удалена. Один скребок израсходован."; }
                    else return Reject("Нужны скребок и нанесённая начинка. После начала складывания исправление недоступно.");
                    break;
                case ActionKind.Discard:
                    accepted = Discard(command.a);
                    break;
                case ActionKind.BeginFold:
                    accepted = OnPlate() && command.a >= 0 && command.a < 3;
                    if (accepted && IsLessonPancake(PlatePancake) && (command.a != 0 || PlatePancake.IngredientBits != 1 || PlatePancake.Fillings[0].Mask.Percent < Rules.spreadCoveragePercent)) return Reject("Для первого гостя намажьте масло и выберите треугольник.");
                    if (accepted) { ClearStroke(PlatePancake); PlatePancake.Stage = PancakeStage.Folding; PlatePancake.IntendedFold = (Fold)command.a; if (IsLessonPancake(PlatePancake)) Tutorial = TutorialStep.Folding; Feedback = "Проведите по блину в направлении стрелки."; }
                    break;
                case ActionKind.FoldGesture: accepted = FoldGesture(command.a, command.b); break;
                case ActionKind.FinalizeFold:
                    accepted = PlatePancake != null && PlatePancake.Stage == PancakeStage.Folding && !IsLessonPancake(PlatePancake);
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
        private bool OnPlate() => CanCook && PlatePancake != null && PlatePancake.Stage == PancakeStage.Plate;
        private void Spend(PancakeState pancake, int amount) { if (!IsLessonPancake(pancake)) Ledger.Products += amount; }
        private void ClearStroke(PancakeState target = null)
        {
            if (target != null && strokePancake != target) return;
            stroke = null; strokePancake = null;
        }
        private bool Discard(int id)
        {
            // Zero preserves old journal commands; pointer drops specify the exact pancake.
            var target = id == 0 ? Pancake : PanPancake?.Id == id ? PanPancake : PlatePancake?.Id == id ? PlatePancake : FindTrayPancake(id);
            if (!CanCook || target == null) return false;
            bool lesson = IsLessonPancake(target);
            if (lesson && Tutorial == TutorialStep.Discard) DemonstrationSeen = true;
            ClearStroke(target);
            if (target == PanPancake) PanPancake = null;
            else if (target == PlatePancake) PlatePancake = null;
            else {
                if (!ReadyPancakes.Remove(target)) Order.Finished.Remove(target);
                if (LastFinished == target) LastFinished = null;
                if (FreeCooking) ServeReadyPancakes();
            }
            if (lesson) Tutorial = DemonstrationSeen ? TutorialStep.Retry : TutorialStep.Batter;
            Feedback = "Блин выброшен. Можно продолжать готовку."; return true;
        }
        public PancakeState FindTrayPancake(int id)
        {
            if (Phase == DayPhase.Result) return null;
            if (HasActiveOrder) foreach (var pancake in Order.Finished) if (pancake.Id == id) return pancake;
            return ReadyPancakes.Find(pancake => pancake.Id == id);
        }
        public PancakeState TrayPancakeAt(int index)
        {
            if (index < 0 || index >= TrayCount) return null;
            int reserved = HasActiveOrder ? Order.Finished.Count : 0;
            return index < reserved ? Order.Finished[index] : ReadyPancakes[index - reserved];
        }
        private bool BeginStroke(int type, int x, int y)
        {
            if (stroke != null || !CanCook || !ValidCoordinate(x, y) || !Point.InCircle(x, y)) return false;
            if (type == -1)
            {
                if (!FreeCooking && PlatePancake != null) return false;
                if (PanPancake == null) PanPancake = new PancakeState(++pancakeNumber, IsTutorial && HasActiveOrder && PlatePancake == null);
                if (PanPancake.Stage != PancakeStage.Batter) return false;
                var pancake = PanPancake; strokePancake = pancake;
                stroke = new StrokeSampler(x, y, Rules.strokeSpacing, (u, v) => { if (!Point.InCircle(u, v)) return; pancake.Batter.Stamp(u, v, Rules.brushRadius); Spend(pancake, Rules.batterStampCost); });
                return true;
            }
            if (!OnPlate() || type < 0 || type > 2 || !StartProfile.Owns((Ingredient)type)) return false;
            var plate = PlatePancake; strokePancake = plate;
            var layer = plate.Fillings[type] ?? (plate.Fillings[type] = new IngredientLayer());
            stroke = new StrokeSampler(x, y, Rules.strokeSpacing, (u, v) => { if (!Point.InCircle(u, v)) return; layer.Mask.Stamp(u, v, Rules.brushRadius); layer.Dose++; Spend(plate, Rules.spreadStampCost); });
            return true;
        }
        private bool PlacePortion(int type, int x, int y)
        {
            if (!OnPlate() || type < 3 || type > 4 || !StartProfile.Owns((Ingredient)type) || !ValidCoordinate(x, y) || !Point.InCircle(x, y)) return false;
            var layer = PlatePancake.Fillings[type] ?? (PlatePancake.Fillings[type] = new IngredientLayer());
            if (layer.Portions.Count >= Rules.maxPortions) return Reject("На блине уже слишком много порций этой начинки.");
            layer.Portions.Add(new Point(x, y)); layer.Dose++; Spend(PlatePancake, Rules.portionCost); return true;
        }
        private bool ApplyHelper(int type)
        {
            if (!OnPlate() || type < 0 || type > 4 || !StartProfile.Owns((Ingredient)type) || !StartProfile.HelperEnabled((Ingredient)type)) return false;
            if (type >= 3) { foreach (var p in Recipes.PortionTargets) PlacePortion(type, p.X, p.Y); return true; }
            // Identical strokes and cost calculation to a human moving along these rows.
            for (int y = 80; y < 1000; y += 130)
            {
                int edge = (int)Math.Sqrt(250000 - (y - 500) * (y - 500));
                if (BeginStroke(type, 501 - edge, y)) { stroke.Move(499 + edge, y); ClearStroke(); }
            }
            return true;
        }
        private bool FoldGesture(int dx, int dy)
        {
            var pancake = PlatePancake;
            if (pancake == null || pancake.Stage != PancakeStage.Folding || Math.Abs((long)dx) > 1000 || Math.Abs((long)dy) > 1000) return false;
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < 160) return Reject("Проведите дальше через блин.");
            var direction = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? Direction.Right : Direction.Left) : (dy > 0 ? Direction.Up : Direction.Down);
            var expected = Recipes.Gestures(pancake.IntendedFold);
            bool correct = pancake.Folds.Count < expected.Length && expected[pancake.Folds.Count] == direction;
            if (!correct && IsLessonPancake(pancake)) return Reject("Попробуйте ещё раз по стрелке. В уроке неверный сгиб не портит блин.");
            pancake.Folds.Add(direction); pancake.FoldError |= !correct;
            if (pancake.Folds.Count >= expected.Length) { if (!pancake.FoldError) pancake.ActualFold = pancake.IntendedFold; FinishPancake(); }
            return true;
        }
        private void FinishPancake()
        {
            var pancake = PlatePancake;
            pancake.Stage = PancakeStage.Finished; LastFinished = pancake; FinishedCount++;
            if (FreeCooking) ReadyPancakes.Add(pancake); else Order.Finished.Add(pancake);
            PlatePancake = null; ClearStroke(pancake);
            if (FreeCooking) {
                Feedback = "Блин готов и ждёт на подносе. Можно готовить следующий.";
                ServeReadyPancakes(); return;
            }
            if (Order.Finished.Count < Order.Portions.Count) { Feedback = "Порция готова и ждёт на подносе. Приготовьте следующую."; return; }
            if (!ManualServing) CompleteOrder();
            else Feedback = "Заказ готов. Нажмите «Отдать заказ» или замените неудачную порцию.";
        }
        private void ServeReadyPancakes()
        {
            if (!HasActiveOrder) return;
            while (ReadyPancakes.Count > 0 && Order.Finished.Count < Order.Portions.Count) {
                Order.Finished.Add(ReadyPancakes[0]); ReadyPancakes.RemoveAt(0);
            }
            if (Order.Finished.Count == Order.Portions.Count) {
                if (!ManualServing) CompleteOrder();
                else Feedback = "Заказ готов. Нажмите «Отдать заказ» или замените неудачную порцию.";
            }
        }
        private void CompleteOrder()
        {
            bool lesson = IsTutorial;
            Order.Report = QualityEvaluator.Evaluate(Order, Rules);
            if (lesson) { Ledger.Revenue += Order.Price; TutorialCompleted = true; Tutorial = TutorialStep.Complete; }
            Ledger.Tips += Order.Report.Tips; Ledger.Refunds += Order.Report.Refund;
            Order.Phase = OrderPhase.Served; Reviews.Add(Order.Report); betweenGuests = 0;
            Feedback = lesson ? "Отлично! Теперь обычная смена: следите за жаркой, временем и расходами." : Order.Report.Refund > 0 ? "Гость недоволен и просит вернуть оплату." : "Заказ выдан целиком. Спасибо!";
            if (Phase == DayPhase.Closing && !ManualServing) FinishDay();
        }
        private void Tick()
        {
            if (IsTutorial)
            {
                var pan = PanPancake;
                if (IsLessonPancake(pan) && Tutorial == TutorialStep.Demonstration)
                { pan.HeatA += 3; if (pan.HeatA >= Rules.burnTicks) { Tutorial = TutorialStep.Discard; Feedback = "Первый блин комом! Перетащите подгоревший блин в миску справа."; } }
                else if (IsLessonPancake(pan) && pan.Stage == PancakeStage.SideA && Tutorial == TutorialStep.SideA) pan.HeatA = Math.Min(Rules.readyTicks, pan.HeatA + 1);
                else if (IsLessonPancake(pan) && pan.Stage == PancakeStage.SideB) pan.HeatB = Math.Min(Rules.readyTicks, pan.HeatB + 1);
                else if (FreeCooking && !IsLessonPancake(pan)) HeatPancake();
                return;
            }
            ElapsedTicks++;
            if (HasActiveOrder)
            {
                Order.WaitTicks++;
                if (Order.WaitTicks >= Order.PatienceLimit) CancelOrder();
            }
            else if (!ManualServing || GuestLeaving) betweenGuests++;
            HeatPancake();
            if (ElapsedTicks >= Rules.dayTicks)
            {
                bool justClosed = Phase != DayPhase.Closing;
                Phase = DayPhase.Closing;
                if (ManualServing && justClosed && Order.Phase == OrderPhase.Offered) betweenGuests = 0;
                if (!HasActiveOrder && (!ManualServing || GuestHasLeft)) FinishDay();
            }
        }
        private void HeatPancake()
        {
            if (PanPancake == null) return;
            if (PanPancake.Stage == PancakeStage.SideA) PanPancake.HeatA++;
            if (PanPancake.Stage == PancakeStage.SideB) PanPancake.HeatB++;
        }
        private void CancelOrder()
        {
            if (!HasActiveOrder) return;
            Ledger.Refunds += Order.Price; Order.Phase = OrderPhase.Cancelled;
            Order.Report = new QualityReport { Refund = Order.Price };
            Order.Report.Reasons.Add("Терпение закончилось. Гость ушёл, оплата возвращена."); Reviews.Add(Order.Report);
            if (FreeCooking) {
                ReadyPancakes.InsertRange(0, Order.Finished); Order.Finished.Clear();
                Feedback = "Гость ушёл. Блин и готовые порции остались на кухне, продукты учтены в расходах.";
            }
            else { PanPancake = PlatePancake = null; ClearStroke(); Feedback = "Гость ушёл. Продукты остаются расходом."; }
            betweenGuests = 0;
        }
        private void FinishDay()
        {
            if (Phase == DayPhase.Result) return;
            int total = StartProfile.ratingTotal, count = StartProfile.ratingCount;
            foreach (var review in Reviews) { total += review.Total; count++; }
            int rating = count == 0 ? 70 : total / count;
            Ledger.Rent = Rules.rent * (150 - rating) / 100;
            Phase = DayPhase.Result; ClearStroke(); PanPancake = PlatePancake = null; ReadyPancakes.Clear();
        }
    }
}
