using System;
using System.Collections.Generic;
using Blinopek.Domain;

namespace Blinopek.Application
{
    public sealed partial class GameController : IGameState
    {
        // 1) Владеет объектом.
        // Сохранение в памяти и активная смена создаются/заменяются только контроллером.
        private SavedGame saved;
        public DaySession Session { get; private set; }

        // 2) Общается через API.
        // Адаптеры передаёт GameStartup; реализации хранения и сервера контроллеру неизвестны.
        private readonly IGameStore store;
        private readonly IDayGateway gateway;
        private readonly BalanceRules nextRules;

        // 3) Внутренние поля и объекты класса.
        // Состояние пауз/хранения и API чтения текущего профиля.
        public Profile Profile => saved.profile;
        public PauseReason Pauses { get; private set; }
        public bool Paused => Pauses != PauseReason.None;
        public string StorageError { get; private set; }
        public bool StorageBlocked { get; private set; }
        public bool CanResetProgress => gateway.Environment == "local-development" && store is IResettableGameStore;

        public event Action Changed;
        // Live notifications only. Restoring the command log does not replay presentation effects.
        public event Action<GameCommand> CommandApplied;
        public event Action DayStarted;
        public event Action<ShopItem> PurchaseApplied;
        public GameController(IGameStore store, IDayGateway gateway, BalanceRules rules)
        {
            this.store = store;
            this.gateway = gateway;
            nextRules = rules;
            try
            {
                saved = store.Load();
            }
            catch (Exception ex)
            {
                StorageError = "Не удалось восстановить сохранение: " + ex.Message;
                StorageBlocked = true;
            }

            if (saved == null)
                saved = new SavedGame
                {
                    profile = new Profile
                    {
                        coins = rules.startingCoins,
                        honey = rules.startingHoney
                    },
                    rules = rules,
                    awaitingDay = true
                };
            RestoreSession();
        }

        public bool StartDay(int seed)
        {
            if (StorageBlocked || (Session != null && Session.Phase != DayPhase.Result))
                return false;
            saved = new SavedGame
            {
                profile = Profile.Copy(),
                sessionStart = Profile.Copy(),
                seed = seed,
                rules = nextRules.Copy(),
                environment = gateway.Environment,
                cafeName = CafeName
            };
            Session = new DaySession(saved.sessionStart, saved.rules, saved.seed, false, false);
            Dispatch(new GameCommand(ActionKind.EnableFreeCooking));
            Dispatch(new GameCommand(ActionKind.EnableManualServing));
            DayStarted?.Invoke();
            Changed?.Invoke();
            return true;
        }

        public bool RestartDay()
        {
            if (StorageBlocked || Session == null || Session.Phase == DayPhase.Result)
                return false;
            var fresh = new SavedGame
            {
                profile = saved.sessionStart.Copy(),
                sessionStart = saved.sessionStart.Copy(),
                seed = saved.seed,
                rules = saved.rules.Copy(),
                environment = gateway.Environment,
                cafeName = CafeName,
                commands = new List<GameCommand>
                {
                    new GameCommand(ActionKind.EnableFreeCooking),
                    new GameCommand(ActionKind.EnableManualServing)
                }
            };
            // Replay into a separate session without changing the old state or external pauses.
            var restarted = SessionJournal.Replay(fresh);
            try
            {
                store.Save(fresh);
            }
            catch (Exception ex)
            {
                StorageError = "Не удалось начать день заново: " + ex.Message;
                Changed?.Invoke();
                return false;
            }

            saved = fresh;
            Session = restarted;
            StorageError = null;
            Pauses &= ~(PauseReason.Menu | PauseReason.RecipeBook);
            Changed?.Invoke();
            DayStarted?.Invoke();
            return true;
        }

        public bool Dispatch(GameCommand command)
        {
            if (StorageBlocked || Paused || Session == null || !Session.Execute(command))
            {
                Changed?.Invoke();
                return false;
            }

            SessionJournal.Append(saved.commands, command);
            if (Session.Phase == DayPhase.Result && !saved.settled)
                Settle();
            if (command.kind != ActionKind.Tick && command.kind != ActionKind.Stroke && command.kind != ActionKind.BeginStroke)
                Save();
            CommandApplied?.Invoke(command);
            Changed?.Invoke();
            // Keep arrivals in the journal so older, manually advanced sessions replay unchanged.
            if (command.kind == ActionKind.Tick && Session.CanOfferNext)
                Dispatch(new GameCommand(ActionKind.NextGuest));
            return true;
        }

        public void SetPause(PauseReason reason, bool enabled)
        {
            if (enabled)
            {
                // End a held gesture at its last real point before pausing; never invent pointer travel.
                if (!Paused && Session != null)
                    Dispatch(new GameCommand(ActionKind.EndStroke));
                Pauses |= reason;
                Save();
            }
            else
                Pauses &= ~reason;
            Changed?.Invoke();
        }
    }
}
