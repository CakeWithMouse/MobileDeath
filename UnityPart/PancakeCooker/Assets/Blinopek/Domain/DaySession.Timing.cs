using System;

namespace Blinopek.Domain
{
    public sealed partial class DaySession
    {
        private bool AdvanceTicks(int count)
        {
            if (count < 1 || count > GameCommand.MaxTickCount)
                return false;
            for (int i = 0; i < count && Phase != DayPhase.Result; i++)
                Tick();
            return true;
        }

        private void Tick()
        {
            if (IsTutorial)
            {
                var pan = PanPancake;
                if (IsLessonPancake(pan) && Tutorial == TutorialStep.Demonstration)
                {
                    pan.HeatA += 3;
                    if (pan.HeatA >= Rules.burnTicks)
                    {
                        Tutorial = TutorialStep.Discard;
                        Feedback = "Первый блин комом! Перетащите подгоревший блин в миску справа.";
                    }
                }
                else if (IsLessonPancake(pan) && pan.Stage == PancakeStage.SideA && Tutorial == TutorialStep.SideA)
                    pan.HeatA = Math.Min(Rules.readyTicks, pan.HeatA + 1);
                else if (IsLessonPancake(pan) && pan.Stage == PancakeStage.SideB)
                    pan.HeatB = Math.Min(Rules.readyTicks, pan.HeatB + 1);
                else if (FreeCooking && !IsLessonPancake(pan))
                    HeatPancake();
                return;
            }

            ElapsedTicks++;
            if (HasActiveOrder)
            {
                Order.WaitTicks++;
                if (Order.WaitTicks >= Order.PatienceLimit)
                    CancelOrder();
            }
            else if (!ManualServing || GuestLeaving)
                betweenGuests++;
            HeatPancake();
            if (ElapsedTicks >= Rules.dayTicks)
            {
                bool justClosed = Phase != DayPhase.Closing;
                Phase = DayPhase.Closing;
                if (ManualServing && justClosed && Order.Phase == OrderPhase.Offered)
                    betweenGuests = 0;
                if (!HasActiveOrder && (!ManualServing || GuestHasLeft))
                    FinishDay();
            }
        }

        private void HeatPancake()
        {
            if (PanPancake == null)
                return;
            if (PanPancake.Stage == PancakeStage.SideA)
                PanPancake.HeatA++;
            if (PanPancake.Stage == PancakeStage.SideB)
                PanPancake.HeatB++;
        }

        private void FinishDay()
        {
            if (Phase == DayPhase.Result)
                return;
            int total = StartProfile.ratingTotal, count = StartProfile.ratingCount;
            foreach (var review in Reviews)
            {
                total += review.Total;
                count++;
            }

            int rating = count == 0 ? 70 : total / count;
            Ledger.Rent = Rules.rent * (150 - rating) / 100;
            Phase = DayPhase.Result;
            ClearStroke();
            PanPancake = PlatePancake = null;
            ReadyPancakes.Clear();
        }
    }
}
