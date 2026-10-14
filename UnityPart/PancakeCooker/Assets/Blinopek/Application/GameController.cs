using System;
using System.Collections.Generic;
using Blinopek.Domain;

namespace Blinopek.Application
{
    [Flags]
    public enum PauseReason { None = 0, Menu = 1, RecipeBook = 2, Background = 4, Focus = 8 }
    public enum ShopItem { Ingredient, Scraper, Patience, Helper, ToggleHelper }

    [Serializable]
    public sealed class SavedGame
    {
        public int schema = 1;
        public string environment = "local-development";
        public Profile profile;
        public Profile sessionStart;
        public BalanceRules rules;
        public int seed;
        public bool settled;
        public List<GameCommand> commands = new List<GameCommand>();
    }
    public interface IGameStore { SavedGame Load(); void Save(SavedGame game); }

    // The production gateway must obtain and confirm sessions through the server.
    // This boundary deliberately does not treat a file as a production wallet.
    public interface IDayGateway
    {
        string Environment { get; }
        Profile Settle(DaySession session, Profile current);
    }

    public sealed class GameController
    {
        private readonly IGameStore store;
        private readonly IDayGateway gateway;
        private readonly BalanceRules nextRules;
        private SavedGame saved;
        public DaySession Session { get; private set; }
        public Profile Profile => saved.profile;
        public PauseReason Pauses { get; private set; }
        public bool Paused => Pauses != PauseReason.None;
        public string StorageError { get; private set; }
        public bool StorageBlocked { get; private set; }
        public event Action Changed;
        // Live notifications only. Restoring the command log does not replay presentation effects.
        public event Action<GameCommand> CommandApplied;
        public event Action DayStarted;
        public event Action<ShopItem> PurchaseApplied;

        public GameController(IGameStore store, IDayGateway gateway, BalanceRules rules)
        {
            this.store = store; this.gateway = gateway; nextRules = rules;
            try { saved = store.Load(); }
            catch (Exception ex) { StorageError = "Не удалось восстановить сохранение: " + ex.Message; StorageBlocked = true; }
            if (saved == null)
                saved = new SavedGame { profile = new Profile { coins = rules.startingCoins, honey = rules.startingHoney }, rules = rules };
            if (saved.sessionStart != null)
            {
                if (saved.schema != 1 || saved.environment != gateway.Environment) throw new InvalidOperationException("Incompatible save environment or schema.");
                Session = new DaySession(saved.sessionStart, saved.rules, saved.seed);
                foreach (var command in saved.commands)
                    if (!Session.Execute(command)) throw new InvalidOperationException("Invalid command in saved session: " + command.kind);
                if (Session.Phase == DayPhase.Result && !saved.settled) Settle();
                else if (Session.Phase != DayPhase.Result) Dispatch(new GameCommand(ActionKind.EndStroke));
            }
        }
        public bool StartDay(int seed)
        {
            if (StorageBlocked || (Session != null && Session.Phase != DayPhase.Result)) return false;
            saved = new SavedGame { profile = Profile.Copy(), sessionStart = Profile.Copy(), seed = seed, rules = nextRules.Copy(), environment = gateway.Environment };
            Session = new DaySession(saved.sessionStart, saved.rules, saved.seed);
            Save(); DayStarted?.Invoke(); Changed?.Invoke(); return true;
        }
        public bool Dispatch(GameCommand command)
        {
            if (StorageBlocked || Paused || Session == null || !Session.Execute(command)) { Changed?.Invoke(); return false; }
            if (command.kind == ActionKind.Tick && saved.commands.Count > 0 && saved.commands[saved.commands.Count - 1].kind == ActionKind.Tick && saved.commands[saved.commands.Count - 1].a + command.a <= 12000)
                saved.commands[saved.commands.Count - 1].a += command.a;
            else saved.commands.Add(command.Copy());
            if (Session.Phase == DayPhase.Result && !saved.settled) Settle();
            if (command.kind != ActionKind.Tick && command.kind != ActionKind.Stroke && command.kind != ActionKind.BeginStroke) Save();
            CommandApplied?.Invoke(command); Changed?.Invoke(); return true;
        }
        private void Settle() { saved.profile = gateway.Settle(Session, saved.profile); saved.settled = true; Save(); }
        public void SetPause(PauseReason reason, bool enabled)
        {
            if (enabled) {
                // End a held gesture at its last real point before pausing; never invent pointer travel.
                if (!Paused && Session != null) Dispatch(new GameCommand(ActionKind.EndStroke));
                Pauses |= reason; Save();
            }
            else Pauses &= ~reason;
            Changed?.Invoke();
        }
        public void Save()
        {
            if (StorageBlocked) return;
            try { store.Save(saved); StorageError = null; }
            catch (Exception ex) { StorageError = "Сохранение не записано: " + ex.Message; }
        }
        public bool Buy(ShopItem item, int ingredient = 0)
        {
            if (StorageBlocked || Session == null || Session.Phase != DayPhase.Result || !Session.Success) return false;
            if (ingredient < 0 || ingredient >= 5) return false;
            int bit = 1 << ingredient, price = 0;
            switch (item)
            {
                case ShopItem.Ingredient: if ((Profile.ownedIngredients & bit) != 0) return false; price = nextRules.ingredientUnlockCost; break;
                case ShopItem.Scraper: price = nextRules.scraperPrice; break;
                case ShopItem.Patience: if (Profile.patienceUpgrade) return false; price = nextRules.patienceUpgradeCost; break;
                case ShopItem.Helper:
                    if (!Profile.Owns((Ingredient)ingredient) || (Profile.helpersOwned & bit) != 0 || Profile.honey < nextRules.helperHoneyPrice) return false;
                    Profile.honey -= nextRules.helperHoneyPrice; Profile.helpersOwned |= bit; Profile.helpersEnabled |= bit; Save(); PurchaseApplied?.Invoke(item); Changed?.Invoke(); return true;
                case ShopItem.ToggleHelper:
                    if ((Profile.helpersOwned & bit) == 0) return false; Profile.helpersEnabled ^= bit; Save(); PurchaseApplied?.Invoke(item); Changed?.Invoke(); return true;
            }
            if (Profile.coins < price) return false;
            Profile.coins -= price;
            if (item == ShopItem.Ingredient) Profile.ownedIngredients |= bit;
            if (item == ShopItem.Scraper) Profile.scrapers++;
            if (item == ShopItem.Patience) Profile.patienceUpgrade = true;
            Save(); PurchaseApplied?.Invoke(item); Changed?.Invoke(); return true;
        }
    }
}
