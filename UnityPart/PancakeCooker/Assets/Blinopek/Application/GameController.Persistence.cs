using System;
using Blinopek.Domain;

namespace Blinopek.Application
{
    public sealed partial class GameController
    {
        public System.Collections.Generic.IEnumerable<float> RestoreSteps()
        {
            if (!restorePending) throw new InvalidOperationException("Deferred restore is not pending.");
            if (saved.sessionStart != null)
            {
                if (saved.schema != 1 || saved.environment != gateway.Environment)
                    throw new InvalidOperationException("Incompatible save environment or schema.");
                var restored = new DaySession(saved.sessionStart, saved.rules, saved.seed, false, false);
                var budget = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < saved.commands.Count; i++)
                {
                    var command = saved.commands[i];
                    if (!restored.Execute(command)) throw new InvalidOperationException("Invalid command in saved session: " + command.kind);
                    if (i % 32 == 0 || budget.ElapsedMilliseconds >= 4)
                    {
                        yield return (i + 1f) / saved.commands.Count;
                        budget.Restart();
                    }
                }
                Session = restored;
                CompleteRestore();
            }
            restorePending = false;
            yield return 1;
        }
        private void RestoreSession()
        {
            if (saved.sessionStart != null)
            {
                if (saved.schema != 1 || saved.environment != gateway.Environment)
                    throw new InvalidOperationException("Incompatible save environment or schema.");
                Session = SessionJournal.Replay(saved);
                CompleteRestore();
            }
        }
        private void CompleteRestore()
        {
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
