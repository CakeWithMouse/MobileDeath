using System;
using System.IO;
using System.Linq;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;
using Blinopek.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Blinopek.Tests
{
    public sealed class ProgressResetTests
    {
        private string directory, file;
        private GameContent content;
        private GameView view;
        private readonly BalanceRules rules = new BalanceRules { startingCoins = 4200, startingHoney = 3 };

        [SetUp] public void Setup()
        {
            directory = Path.GetFullPath(Path.Combine("Library", "BlinopekTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory); file = Path.Combine(directory, "reset.json");
        }
        [TearDown] public void Cleanup()
        {
            if (view != null) Object.DestroyImmediate(view.gameObject);
            if (content != null) Object.DestroyImmediate(content);
            string root = Path.GetFullPath(Path.Combine("Library", "BlinopekTests")) + Path.DirectorySeparatorChar;
            if (directory.StartsWith(root, StringComparison.OrdinalIgnoreCase)) Directory.Delete(directory, true);
        }
        private GameController ExistingGame()
        {
            var store = new FileGameStore(file);
            store.Save(new SavedGame {
                profile = new Profile { day = 8, coins = 12000, honey = 80, scrapers = 9, ownedIngredients = 31,
                    helpersOwned = 31, helpersEnabled = 31, patienceUpgrade = true, darkTablesUpgrade = true, tutorialComplete = true,
                    demonstrationSeen = true, ratingTotal = 850, ratingCount = 10 }, rules = rules, awaitingDay = true
            });
            var game = new GameController(store, new LocalDevelopmentGateway(), rules);
            Assert.That(game.StartDay(42), Is.True);
            Assert.That(game.Dispatch(new GameCommand(ActionKind.Accept)), Is.True);
            Assert.That(game.Dispatch(new GameCommand(ActionKind.BeginStroke, -1, 500, 500)), Is.True);
            game.Save(); return game;
        }
        private void AssertFresh(GameController game)
        {
            var expected = new Profile { coins = rules.startingCoins, honey = rules.startingHoney };
            Assert.That(JsonUtility.ToJson(game.Profile), Is.EqualTo(JsonUtility.ToJson(expected)));
            Assert.That(game.Session, Is.Null);
            Assert.That(game.StorageBlocked, Is.False); Assert.That(game.StorageError, Is.Null);
        }
        [Test] public void ResetClearsProfileSessionAndOldBackupAcrossReloads()
        {
            var game = ExistingGame();
            game.SetPause(PauseReason.Menu | PauseReason.Background | PauseReason.RecipeBook, true);
            Assert.That(File.Exists(file + ".bak"), Is.True);
            Assert.That(game.ResetProgress(), Is.True); AssertFresh(game);
            Assert.That(game.Pauses, Is.EqualTo(PauseReason.Menu | PauseReason.Background));
            Assert.That(File.Exists(file + ".bak"), Is.False);
            Assert.That(File.Exists(file + ".writing"), Is.False);
            var store = new FileGameStore(file); var saved = store.Load();
            Assert.That(saved.commands, Is.Empty); Assert.That(saved.sessionStart, Is.Null); Assert.That(saved.settled, Is.False);
            var restored = new GameController(store, new LocalDevelopmentGateway(), rules); AssertFresh(restored);
            restored.Save(); File.WriteAllText(file, "broken after reset");
            var recovered = new GameController(new FileGameStore(file), new LocalDevelopmentGateway(), rules);
            AssertFresh(recovered);
            Assert.That(recovered.StartDay(52), Is.True);
            Assert.That(recovered.Session.IsTutorial, Is.True);
            Assert.That(recovered.Session.FreeCooking && recovered.Session.ManualServing, Is.True);
        }
        [Test] public void ExplicitResetCanRecoverBlockedStorage()
        {
            File.WriteAllText(file, "broken"); File.WriteAllText(file + ".bak", "also broken");
            var game = new GameController(new FileGameStore(file), new LocalDevelopmentGateway(), rules);
            Assert.That(game.StorageBlocked, Is.True);
            Assert.That(game.ResetProgress(), Is.True); AssertFresh(game);
            AssertFresh(new GameController(new FileGameStore(file), new LocalDevelopmentGateway(), rules));
        }
        [Test] public void FailedResetPreservesCurrentGameAndSavedProgress()
        {
            var game = ExistingGame(); var session = game.Session; var profile = game.Profile;
            string primary = File.ReadAllText(file), backup = File.ReadAllText(file + ".bak");
            Directory.CreateDirectory(file + ".writing");
            Assert.That(game.ResetProgress(), Is.False);
            Assert.That(game.Session, Is.SameAs(session)); Assert.That(game.Profile, Is.SameAs(profile));
            Assert.That(game.StorageError, Does.StartWith("Не удалось сбросить прогресс:"));
            Assert.That(File.ReadAllText(file), Is.EqualTo(primary)); Assert.That(File.ReadAllText(file + ".bak"), Is.EqualTo(backup));
            Directory.Delete(file + ".writing");
            Assert.That(game.ResetProgress(), Is.True); AssertFresh(game);
        }
        private sealed class ServerGateway : IDayGateway
        {
            public string Environment => "production";
            public Profile Settle(DaySession session, Profile current) => throw new InvalidOperationException("Unexpected settlement");
        }
        [Test] public void LocalResetIsUnavailableForServerProgress()
        {
            var game = new GameController(new FileGameStore(file), new ServerGateway(), rules);
            Assert.That(game.CanResetProgress, Is.False); Assert.That(game.ResetProgress(), Is.False);
            Assert.That(File.Exists(file), Is.False);
        }
        private void BuildView(GameController game)
        {
            content = ScriptableObject.CreateInstance<GameContent>(); content.balance = rules;
            content.bodyFont = content.titleFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            view = new GameObject("Progress reset test").AddComponent<GameView>();
            view.Initialize(game, content); Canvas.ForceUpdateCanvases();
        }
        private GameObject Find(string name) => view.GetComponentsInChildren<Transform>(true).Single(t => t.name == name).gameObject;
        private void Click(string name)
        {
            var button = Find(name).GetComponent<Button>();
            Assert.That(button.gameObject.activeInHierarchy && button.IsInteractable(), Is.True, name);
            button.onClick.Invoke(); view.Refresh(); Canvas.ForceUpdateCanvases();
        }
        [TestCase(false)]
        [TestCase(true)]
        public void SettingsRequireConfirmationAndRestartWithTutorial(bool duringShift)
        {
            var game = ExistingGame(); BuildView(game); var session = game.Session;
            if (duringShift) { Click("ContinueShift"); Click("Pause"); }
            else Click("MainMenuSettings");
            float music = view.Audio.MusicVolume, effects = view.Audio.EffectsVolume, ambience = view.Audio.AmbienceVolume;
            bool muted = view.Audio.Muted, alerts = view.Audio.CookingAlerts;
            Click("ResetProgress"); Assert.That(Find("ResetProgressConfirmation").activeInHierarchy, Is.True);
            Assert.That(game.Paused, Is.True); Assert.That(game.Session, Is.SameAs(session));
            Click("CancelResetProgress");
            Assert.That(Find("ResetProgressConfirmation").activeSelf, Is.False);
            Assert.That(game.Session, Is.SameAs(session)); Assert.That(game.Profile.day, Is.EqualTo(8));
            Click("ResetProgress"); Click("ConfirmResetProgress"); AssertFresh(game);
            Assert.That(Find("MainMenu").activeInHierarchy, Is.True);
            Assert.That(Find("PauseMenu").activeSelf || Find("ResetProgressConfirmation").activeSelf, Is.False);
            Assert.That(view.Audio.MusicVolume, Is.EqualTo(music)); Assert.That(view.Audio.EffectsVolume, Is.EqualTo(effects));
            Assert.That(view.Audio.AmbienceVolume, Is.EqualTo(ambience)); Assert.That(view.Audio.Muted, Is.EqualTo(muted));
            Assert.That(view.Audio.CookingAlerts, Is.EqualTo(alerts));
            Click("MainMenuShop");
            Assert.That(Find("BuyIngredient1").GetComponent<Button>().interactable, Is.False);
            Assert.That(Find("BuyIngredient1").GetComponentInChildren<Text>().text, Is.EqualTo("Купить"));
            Assert.That(Find("ShopBalance").GetComponent<Text>().text, Is.EqualTo("Златники: 42.00 · Жаринки: 3"));
            Click("CloseShop");
            Click("MainMenuSettings"); Click("Resume");
            Assert.That(game.Paused, Is.True, "Closing settings must keep the main menu paused");
            Click("ContinueShift");
            Assert.That(game.Session.StartProfile.day, Is.EqualTo(1)); Assert.That(game.Session.IsTutorial, Is.True);
            Assert.That(game.Session.FreeCooking && game.Session.ManualServing, Is.True);
            Assert.That(game.Paused, Is.False); Assert.That(Find("Counter").activeInHierarchy, Is.True);
        }
        [Test] public void FailedResetKeepsConfirmationOpenAndCanBeRetried()
        {
            var game = ExistingGame(); BuildView(game); var session = game.Session;
            Click("MainMenuSettings"); Click("ResetProgress"); Directory.CreateDirectory(file + ".writing");
            Click("ConfirmResetProgress");
            Assert.That(game.Session, Is.SameAs(session)); Assert.That(Find("ResetProgressConfirmation").activeInHierarchy, Is.True);
            Assert.That(Find("ResetError").GetComponent<Text>().text, Is.Not.Empty);
            Directory.Delete(file + ".writing"); Click("ConfirmResetProgress"); AssertFresh(game);
        }
        [Test] public void BlockedSaveCanBeResetFromMainMenuSettings()
        {
            File.WriteAllText(file, "broken"); File.WriteAllText(file + ".bak", "broken backup");
            var game = new GameController(new FileGameStore(file), new LocalDevelopmentGateway(), rules); BuildView(game);
            Click("MainMenuSettings"); Click("ResetProgress"); Click("ConfirmResetProgress"); AssertFresh(game);
            Click("ContinueShift"); Assert.That(game.Session.IsTutorial, Is.True);
        }
    }
}
