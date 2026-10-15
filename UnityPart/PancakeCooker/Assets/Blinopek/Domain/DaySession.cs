using System;
using System.Collections.Generic;

namespace Blinopek.Domain
{
    public sealed partial class DaySession
    {
        public const int TickMilliseconds = 50;
        public readonly BalanceRules Rules;
        public readonly Profile StartProfile;
        public int DecorationPoints { get; }
        public int GuestArrivalIntervalTicks { get; }

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
        public bool CanServeOrder => Phase != DayPhase.Result && HasActiveOrder;
        public bool GuestLeaving => Order != null && (Order.Phase == OrderPhase.Served || Order.Phase == OrderPhase.Cancelled || (Phase == DayPhase.Closing && Order.Phase == OrderPhase.Offered));
        public float GuestDepartureProgress => GuestLeaving ? Math.Min(1f, (float)betweenGuests / Math.Max(1, Rules.guestIntervalTicks)) : 0;
        public bool GuestHasLeft => GuestLeaving && betweenGuests >= Math.Max(1, Rules.guestIntervalTicks);
        public bool CanOfferNext => Phase == DayPhase.Serving && Order != null && (Order.Phase == OrderPhase.Served || Order.Phase == OrderPhase.Cancelled) && betweenGuests >= GuestArrivalIntervalTicks && (!ManualServing || GuestHasLeft);

        private readonly OrderGenerator orderGenerator;
        private int pancakeNumber, betweenGuests;
        private StrokeSampler stroke;
        private PancakeState strokePancake;
        public DaySession(Profile profile, BalanceRules rules, int seed, bool freeCooking = true, bool manualServing = true)
        {
            FreeCooking = freeCooking;
            ManualServing = manualServing;
            rules.Validate();
            Rules = rules;
            StartProfile = profile.Copy();
            orderGenerator = new OrderGenerator(StartProfile, Rules, seed);
            DecorationPoints = StartProfile.DecorationPoints(rules);
            GuestArrivalIntervalTicks = rules.GetGuestArrivalIntervalTicks(DecorationPoints);
            Scrapers = profile.scrapers;
            TutorialCompleted = profile.tutorialComplete;
            DemonstrationSeen = profile.demonstrationSeen;
            Tutorial = TutorialCompleted ? TutorialStep.Complete : TutorialStep.Welcome;
            OfferOrder();
        }

        public bool Execute(GameCommand command)
        {
            if (Phase == DayPhase.Result)
                return false;
            bool accepted = ExecuteCommand(command);
            if (accepted)
                Revision++;
            return accepted;
        }

        private bool ExecuteCommand(GameCommand command)
        {
            switch (command.kind)
            {
                case ActionKind.Tick:
                    return AdvanceTicks(command.a);
                case ActionKind.EnableFreeCooking:
                    return EnableFreeCooking();
                case ActionKind.EnableManualServing:
                    return EnableManualServing();
                case ActionKind.Accept:
                    return AcceptOrder();
                case ActionKind.Clarify:
                    return ClarifyOrder();
                case ActionKind.ServeOrder:
                    return ServeOrder();
                case ActionKind.NextGuest:
                    return NextGuest();
                case ActionKind.BeginStroke:
                    return BeginStroke(command.a, command.b, command.c);
                case ActionKind.Stroke:
                    return MoveStroke(command.a, command.b);
                case ActionKind.EndStroke:
                    return EndStroke();
                case ActionKind.FinishBatter:
                    return FinishBatter();
                case ActionKind.Flip:
                    return Flip();
                case ActionKind.MoveToPlate:
                    return MoveToPlate();
                case ActionKind.Portion:
                    return PlacePortion(command.a, command.b, command.c);
                case ActionKind.ApplyHelper:
                    return ApplyHelper(command.a);
                case ActionKind.Remove:
                    return RemoveIngredient(command.a);
                case ActionKind.Discard:
                    return Discard(command.a);
                case ActionKind.BeginFold:
                    return BeginFold(command.a);
                case ActionKind.FoldGesture:
                    return FoldGesture(command.a, command.b);
                case ActionKind.FinalizeFold:
                    return FinalizeFold();
                default:
                    return false;
            }
        }

        private bool EnableFreeCooking()
        {
            // Persist the transition after replaying older logs under their original rules.
            if (FreeCooking)
                return false;
            FreeCooking = true;
            return true;
        }

        private bool EnableManualServing()
        {
            if (ManualServing)
                return false;
            ManualServing = true;
            return true;
        }

        private bool Reject(string message)
        {
            Feedback = message;
            Revision++;
            return false;
        }
    }
}
