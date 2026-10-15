using System;
using Blinopek.Domain;

namespace Blinopek.Application
{
    public sealed partial class GameController
    {
        private void RestoreSession()
        {
            if (saved.sessionStart != null)
            {
                if (saved.schema != 1 || saved.environment != gateway.Environment)
                    throw new InvalidOperationException("Incompatible save environment or schema.");
                Session = SessionJournal.Replay(saved);
                if (Session.Phase == DayPhase.Result && !saved.settled)
                    Settle();
                else if (Session.Phase != DayPhase.Result)
                {
                    Dispatch(new GameCommand(ActionKind.EndStroke));
                    if (!Session.FreeCooking)
                        Dispatch(new GameCommand(ActionKind.EnableFreeCooking));
                    if (!Session.ManualServing)
                        Dispatch(new GameCommand(ActionKind.EnableManualServing));
                }
            }
        }

        private void Settle()
        {
            saved.profile = gateway.Settle(Session, saved.profile);
            saved.settled = true;
            Save();
        }

        public void Save()
        {
            if (StorageBlocked)
                return;
            try
            {
                store.Save(saved);
                StorageError = null;
            }
            catch (Exception ex)
            {
                StorageError = "Сохранение не записано: " + ex.Message;
            }
        }

        public bool ResetProgress()
        {
            if (!CanResetProgress)
                return false;
            var fresh = new SavedGame
            {
                profile = new Profile
                {
                    coins = nextRules.startingCoins,
                    honey = nextRules.startingHoney
                },
                rules = nextRules.Copy(),
                environment = gateway.Environment,
                awaitingDay = true,
                cafeName = CafeName
            };
            try
            {
                ((IResettableGameStore)store).Reset(fresh);
            }
            catch (Exception ex)
            {
                StorageError = "Не удалось сбросить прогресс: " + ex.Message;
                Changed?.Invoke();
                return false;
            }

            saved = fresh;
            Session = null;
            StorageBlocked = false;
            StorageError = null;
            Pauses &= ~PauseReason.RecipeBook;
            Changed?.Invoke();
            return true;
        }
    }
}
