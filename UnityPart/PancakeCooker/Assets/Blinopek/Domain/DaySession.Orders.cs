namespace Blinopek.Domain
{
    public sealed partial class DaySession
    {
        private bool AcceptOrder()
        {
            if (Phase != DayPhase.Serving || Order.Phase != OrderPhase.Offered)
                return false;
            Order.Phase = OrderPhase.Accepted;
            if (!IsTutorial)
                Ledger.Revenue += Order.Price;
            Tutorial = IsTutorial ? TutorialStep.Batter : TutorialStep.Complete;
            Feedback = Pancake == null ? "Можно приготовить новый блин или выдать готовые порции с подноса." : "Заказ принят. Продолжайте готовить начатый блин.";
            if (FreeCooking)
                AssignReadyPancakes();
            return true;
        }

        private bool ClarifyOrder()
        {
            if ((Order.Phase != OrderPhase.Offered && !HasActiveOrder) || Order.Clarified)
                return false;
            Order.Clarified = true;
            if (!IsTutorial)
            {
                Order.WaitTicks += Rules.clarifyPenaltyTicks;
                if (HasActiveOrder && Order.WaitTicks >= Order.PatienceLimit)
                    CancelOrder();
            }

            Feedback = "В каждой строке — начинка и форма одного блина. Уточнение уменьшает терпение.";
            return true;
        }

        private bool ServeOrder()
        {
            if (!CanServeOrder)
                return Reject("Для выдачи нужен принятый заказ ожидающего гостя.");
            // The explicit action hands over the whole tray, including extra ready portions.
            Order.Finished.AddRange(ReadyPancakes);
            ReadyPancakes.Clear();
            CompleteOrder();
            return true;
        }

        private bool NextGuest()
        {
            if (!CanOfferNext)
                return false;
            OfferOrder();
            return true;
        }

        private void OfferOrder()
        {
            Order = orderGenerator.Next(TutorialCompleted);
            Feedback = "Гость ждёт у стойки.";
        }

        private void AssignReadyPancakes()
        {
            if (!HasActiveOrder)
                return;
            while (ReadyPancakes.Count > 0 && Order.Finished.Count < Order.Portions.Count)
            {
                Order.Finished.Add(ReadyPancakes[0]);
                ReadyPancakes.RemoveAt(0);
            }

            if (Order.Finished.Count == Order.Portions.Count)
            {
                if (!ManualServing)
                    CompleteOrder();
                else
                    Feedback = "Заказ готов. Нажмите на поднос или замените неудачную порцию.";
            }
        }

        private void CompleteOrder()
        {
            bool lesson = IsTutorial;
            Order.Report = QualityEvaluator.Evaluate(Order, Rules);
            if (lesson)
            {
                Ledger.Revenue += Order.Price;
                TutorialCompleted = true;
                Tutorial = TutorialStep.Complete;
            }

            Ledger.Tips += Order.Report.Tips;
            Ledger.Refunds += Order.Report.Refund;
            Order.Phase = OrderPhase.Served;
            Reviews.Add(Order.Report);
            betweenGuests = 0;
            Feedback = lesson ? "Отлично! Теперь обычная смена: следите за жаркой, временем и расходами." : Order.Report.Refund > 0 ? "Гость недоволен и просит вернуть оплату." : "Заказ выдан целиком. Спасибо!";
            if (Phase == DayPhase.Closing && !ManualServing)
                FinishDay();
        }

        private void CancelOrder()
        {
            if (!HasActiveOrder)
                return;
            Ledger.Refunds += Order.Price;
            Order.Phase = OrderPhase.Cancelled;
            Order.Report = new QualityReport
            {
                Refund = Order.Price
            };
            Order.Report.Reasons.Add("Терпение закончилось. Гость ушёл, оплата возвращена.");
            Reviews.Add(Order.Report);
            if (FreeCooking)
            {
                ReadyPancakes.InsertRange(0, Order.Finished);
                Order.Finished.Clear();
                Feedback = "Гость ушёл. Блин и готовые порции остались на кухне, продукты учтены в расходах.";
            }
            else
            {
                PanPancake = PlatePancake = null;
                ClearStroke();
                Feedback = "Гость ушёл. Продукты остаются расходом.";
            }

            betweenGuests = 0;
        }
    }
}
