using System;
using Blinopek.Domain;

namespace Blinopek.Application
{
    [Flags]
    public enum PauseReason
    {
        None = 0,
        Menu = 1,
        RecipeBook = 2,
        Background = 4,
        Focus = 8
    }

    public enum ShopItem
    {
        Ingredient,
        Scraper,
        Patience,
        Helper,
        ToggleHelper,
        DarkTables
    }

    public interface IGameStore
    {
        SavedGame Load();
        void Save(SavedGame game);
    }

    public interface IResettableGameStore : IGameStore
    {
        void Reset(SavedGame game);
    }

    // The production gateway must obtain and confirm sessions through the server.
    // This boundary deliberately does not treat a file as a production wallet.
    public interface IDayGateway
    {
        string Environment { get; }

        Profile Settle(DaySession session, Profile current);
    }
}
