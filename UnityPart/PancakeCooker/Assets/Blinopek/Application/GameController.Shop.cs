using Blinopek.Domain;

namespace Blinopek.Application
{
    public sealed partial class GameController
    {
        public bool Buy(ShopItem item, int ingredient = 0)
        {
            if (StorageBlocked || Session == null || Session.Phase != DayPhase.Result || !Session.Success)
                return false;
            if (ingredient < 0 || ingredient >= 5)
                return false;
            int bit = 1 << ingredient, price = 0;
            switch (item)
            {
                case ShopItem.Ingredient:
                    if ((Profile.ownedIngredients & bit) != 0)
                        return false;
                    price = nextRules.ingredientUnlockCost;
                    break;
                case ShopItem.Scraper:
                    price = nextRules.scraperPrice;
                    break;
                case ShopItem.Patience:
                    if (Profile.patienceUpgrade)
                        return false;
                    price = nextRules.patienceUpgradeCost;
                    break;
                case ShopItem.DarkTables:
                    if (Profile.darkTablesUpgrade)
                        return false;
                    price = nextRules.darkTableUpgradeCost;
                    break;
                case ShopItem.Helper:
                    if (!Profile.Owns((Ingredient)ingredient) || (Profile.helpersOwned & bit) != 0 || Profile.honey < nextRules.helperHoneyPrice)
                        return false;
                    Profile.honey -= nextRules.helperHoneyPrice;
                    Profile.helpersOwned |= bit;
                    Profile.helpersEnabled |= bit;
                    return CompletePurchase(item);
                case ShopItem.ToggleHelper:
                    if ((Profile.helpersOwned & bit) == 0)
                        return false;
                    Profile.helpersEnabled ^= bit;
                    return CompletePurchase(item);
            }

            if (Profile.coins < price)
                return false;
            Profile.coins -= price;
            if (item == ShopItem.Ingredient)
                Profile.ownedIngredients |= bit;
            if (item == ShopItem.Scraper)
                Profile.scrapers++;
            if (item == ShopItem.Patience)
                Profile.patienceUpgrade = true;
            if (item == ShopItem.DarkTables)
                Profile.darkTablesUpgrade = true;
            return CompletePurchase(item);
        }

        private bool CompletePurchase(ShopItem item)
        {
            Save();
            PurchaseApplied?.Invoke(item);
            Changed?.Invoke();
            return true;
        }
    }
}
