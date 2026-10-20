using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;
using Blinopek.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Blinopek.Tests
{
    public sealed class MenuNavigationTests
    {
        private sealed class MemoryStore : IGameStore
        {
            public SavedGame Data;
            public int SaveCount;
            public SavedGame Load() => Data;
            public void Save(SavedGame data) { Data = data; SaveCount++; }
        }
        private MemoryStore store;
        private GameContent content;
        private GameSystem view;
        private GameController game;

        [SetUp] public void Setup()
        {
            content = ScriptableObject.CreateInstance<GameContent>();
            content.bodyFont = content.titleFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            content.balance = new BalanceRules { startingCoins = 10000, rent = 0, dayTicks = 20 };
            store = new MemoryStore { Data = new SavedGame {
                profile = new Profile { coins = 10000, tutorialComplete = true, demonstrationSeen = true }
            } };
            game = new GameController(store, new LocalDevelopmentGateway(), content.balance);
            BuildView();
        }
        private void BuildView()
        {
            view = new GameObject("Menu navigation test").AddComponent<GameSystem>();
            view.Initialize(game, content); Canvas.ForceUpdateCanvases();
            Invoke("Update"); Canvas.ForceUpdateCanvases();
        }
        [TearDown] public void Cleanup()
        {
            if (view != null) Object.DestroyImmediate(view.gameObject);
            if (content != null) Object.DestroyImmediate(content);
        }
        private GameObject Find(string name) => view.GetComponentsInChildren<Transform>(true).Single(t => t.name == name).gameObject;
        private void Invoke(string method) => typeof(GameSystem).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
        private void Click(string name)
        {
            var button = Find(name).GetComponent<Button>();
            Assert.That(button.gameObject.activeInHierarchy && button.IsInteractable(), Is.True, name);
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            view.Refresh(); Canvas.ForceUpdateCanvases();
        }
        private void AssertShopBlocked()
        {
            Assert.That(Find("MainMenuShop").activeInHierarchy, Is.False);
            Invoke("OpenShop");
            Assert.That(Find("Shop").activeSelf, Is.False, "The callback must also reject opening the shop");
            Assert.That(game.Buy(ShopItem.Scraper), Is.False);
        }

        [Test] public void StartupDoesNotStartTheLevelAndShopReturnsToFacade()
        {
            Assert.That(game.Session, Is.Null);
            Assert.That(game.Paused, Is.True);
            Click("MainMenuShop"); Assert.That(Find("Shop").activeInHierarchy, Is.True);
            Click("CloseShop"); Assert.That(Find("MainMenu").activeInHierarchy, Is.True);
            Assert.That(game.Session, Is.Null);
            Click("ContinueShift");
            Assert.That(game.Session, Is.Not.Null); Assert.That(game.Paused, Is.False);
            AssertShopBlocked();
        }

        [Test] public void ShopIsBlockedDuringLevelPauseAndClosing()
        {
            Click("ContinueShift"); AssertShopBlocked();
            Click("Pause"); AssertShopBlocked(); Click("Resume");
            Assert.That(game.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(game.Dispatch(new GameCommand(ActionKind.Tick, content.balance.dayTicks)), Is.True);
            Assert.That(game.Session.Phase, Is.EqualTo(DayPhase.Closing));
            view.Refresh(); AssertShopBlocked();
        }

        [Test] public void RestoredUnfinishedLevelCannotOpenShopFromFacade()
        {
            Click("ContinueShift"); game.Dispatch(new GameCommand(ActionKind.Tick, 1)); game.Save();
            Object.DestroyImmediate(view.gameObject);
            game = new GameController(store, new LocalDevelopmentGateway(), content.balance); BuildView();
            Assert.That(Find("MainMenu").activeInHierarchy, Is.True);
            Assert.That(game.Session.ElapsedTicks, Is.EqualTo(1)); AssertShopBlocked();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RestoredOrderIsAcceptedOnceWhenResuming(bool alreadyAccepted)
        {
            game.SetPause(PauseReason.Menu, false);
            Assert.That(game.StartDay(7), Is.True);
            if (alreadyAccepted) Assert.That(game.Dispatch(new GameCommand(ActionKind.Accept)), Is.True);
            game.Save(); Object.DestroyImmediate(view.gameObject);
            game = new GameController(store, new LocalDevelopmentGateway(), content.balance); BuildView();
            Assert.That(game.Session.Order.Phase, Is.EqualTo(alreadyAccepted ? OrderPhase.Accepted : OrderPhase.Offered));
            Click("ContinueShift"); view.Refresh();
            Assert.That(game.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(game.Session.Ledger.Revenue, Is.EqualTo(game.Session.Order.Price));
            Assert.That(store.Data.commands.Count(c => c.kind == ActionKind.Accept), Is.EqualTo(1));
            Assert.That(Find("Counter").activeInHierarchy, Is.True);
        }

        [Test]
        public void RenderingSceneDoesNotAcceptOrdersAdvanceTimeOrSave()
        {
            // Invoke the real play callback without the test helper's system refresh.
            Find("ContinueShift").GetComponent<Button>().onClick.Invoke();
            Assert.That(game.Paused, Is.False);
            Assert.That(game.Session.Order.Phase, Is.EqualTo(OrderPhase.Offered));
            var session = game.Session;
            int commands = store.Data.commands.Count, writes = store.SaveCount;
            int revision = session.Revision, ticks = session.ElapsedTicks;
            var pauses = game.Pauses;

            view.Scene.Render();
            view.Scene.Render();

            Assert.That(Find("Counter").activeInHierarchy, Is.True);
            Assert.That(game.Session, Is.SameAs(session));
            Assert.That(session.Order.Phase, Is.EqualTo(OrderPhase.Offered));
            Assert.That(session.Revision, Is.EqualTo(revision));
            Assert.That(session.ElapsedTicks, Is.EqualTo(ticks));
            Assert.That(store.Data.commands.Count, Is.EqualTo(commands));
            Assert.That(store.SaveCount, Is.EqualTo(writes));
            Assert.That(game.Pauses, Is.EqualTo(pauses));
            Assert.That(view.AtMainMenu, Is.False);

            view.Refresh();
            view.Refresh();
            Assert.That(session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(store.Data.commands.Count(c => c.kind == ActionKind.Accept), Is.EqualTo(1));
        }

        [Test] public void OnlySystemNavigatesBackToCounterAfterServing()
        {
            Click("ContinueShift"); Click("KitchenTab");
            Assert.That(view.InKitchen, Is.True);
            Assert.That(game.Dispatch(new GameCommand(ActionKind.ServeOrder)), Is.True);

            // Rendering can display the new order state, but cannot choose the next screen.
            view.Scene.Render(); view.Scene.Render();
            Assert.That(view.InKitchen, Is.True);
            Assert.That(Find("Kitchen").activeInHierarchy, Is.True);

            view.Refresh();
            Assert.That(view.InKitchen, Is.False);
            Assert.That(Find("Counter").activeInHierarchy, Is.True);
        }

        [Test] public void ClosingSettingsPreservesFocusPause()
        {
            Click("ContinueShift"); Click("Pause");
            view.SetPause(PauseReason.Focus, true);
            Click("Resume");
            Assert.That(view.SettingsOpen, Is.False);
            Assert.That(game.Pauses, Is.EqualTo(PauseReason.Focus));
            Assert.That(game.Dispatch(new GameCommand(ActionKind.Tick, 1)), Is.False);
            view.SetPause(PauseReason.Focus, false);
            Assert.That(game.Dispatch(new GameCommand(ActionKind.Tick, 1)), Is.True);
        }

        [Test] public void SystemForwardsAudioEventsAndUnsubscribesWhenDestroyed()
        {
            Click("ContinueShift");
            int before = view.Audio.FeedbackVersion;
            Assert.That(game.Dispatch(new GameCommand(ActionKind.ServeOrder)), Is.True);
            Assert.That(view.Audio.FeedbackVersion, Is.GreaterThan(before));

            Object.DestroyImmediate(view.gameObject);
            // Any remaining audio handler would touch destroyed AudioSources on the next pause.
            Assert.DoesNotThrow(() => game.SetPause(PauseReason.Menu, true));
            Assert.DoesNotThrow(() => game.SetPause(PauseReason.Menu, false));
        }

        [Test] public void ClosingFacadeSettingsRefreshesOnTheNextFrame()
        {
            Click("MainMenuSettings"); AssertTopHit("Resume");
            Find("Resume").GetComponent<Button>().onClick.Invoke();
            Invoke("LateUpdate");
            Assert.That(Find("PauseMenu").activeSelf, Is.False);
            Assert.That(Find("MainMenu").activeInHierarchy && game.Paused, Is.True);
            Click("MainMenuShop"); AssertTopHit("CloseShop"); Click("CloseShop");
            Click("MainMenuSettings"); AssertTopHit("ExitToMainMenu"); Click("ExitToMainMenu");
            Assert.That(Find("PauseMenu").activeSelf, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PauseExitCanBeClickedAndResumesTheSameLevel(bool kitchen)
        {
            Click("ContinueShift");
            if (kitchen) Click("KitchenTab");
            Assert.That(game.Dispatch(new GameCommand(ActionKind.Tick, 1)), Is.True);
            var session = game.Session;
            Click("Pause"); AssertTopHit("ExitToMainMenu"); Click("ExitToMainMenu");
            Assert.That(Find("MainMenu").activeInHierarchy, Is.True);
            Assert.That(Find("PauseMenu").activeSelf, Is.False);
            Assert.That(Find("Kitchen").activeInHierarchy || Find("Counter").activeInHierarchy, Is.False);
            Assert.That(game.Paused, Is.True); AssertShopBlocked();
            Assert.That(game.Dispatch(new GameCommand(ActionKind.Tick, 1)), Is.False);
            Assert.That(session.ElapsedTicks, Is.EqualTo(1));
            Click("MainMenuSettings"); Click("Resume");
            Assert.That(Find("PauseMenu").activeSelf, Is.False, "Settings close immediately from the facade");
            Assert.That(game.Paused, Is.True);
            Click("ContinueShift");
            Assert.That(game.Session, Is.SameAs(session)); Assert.That(game.Paused, Is.False);
            Assert.That(Find(kitchen ? "Kitchen" : "Counter").activeInHierarchy, Is.True);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void PauseRestartsCurrentDayFromCounterOrKitchen(bool kitchen)
        {
            Click("ContinueShift");
            if (kitchen) Click("KitchenTab");
            Assert.That(game.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(game.Dispatch(new GameCommand(ActionKind.BeginStroke, -1, 500, 500)), Is.True);
            Assert.That(game.Dispatch(new GameCommand(ActionKind.EndStroke)), Is.True);
            Assert.That(game.Dispatch(new GameCommand(ActionKind.Tick, 1)), Is.True);
            view.Refresh(); var previous = game.Session; string profile = JsonUtility.ToJson(game.Profile);
            Click("Pause"); AssertTopHit("RestartDay"); Click("RestartDay");
            Assert.That(game.Session, Is.Not.SameAs(previous)); Assert.That(game.Paused, Is.False);
            Assert.That(game.Session.ElapsedTicks, Is.Zero); Assert.That(game.Session.Pancake, Is.Null);
            Assert.That(game.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(JsonUtility.ToJson(game.Profile), Is.EqualTo(profile));
            Assert.That(Find("PauseMenu").activeSelf || Find("MainMenu").activeSelf || Find("Kitchen").activeSelf, Is.False);
            Assert.That(Find("Counter").activeInHierarchy, Is.True);
            Click("KitchenTab");
            Assert.That(game.Dispatch(new GameCommand(ActionKind.BeginStroke, -1, 500, 500)), Is.True);
        }

        [Test] public void MainMenuSettingsDoNotOfferDayRestart()
        {
            Click("MainMenuSettings"); Assert.That(Find("RestartDay").activeInHierarchy, Is.False); Click("Resume");
            Click("ContinueShift"); Click("Pause"); Click("ExitToMainMenu");
            Click("MainMenuSettings"); Assert.That(Find("RestartDay").activeInHierarchy, Is.False);
            var session = game.Session; Invoke("RestartCurrentDay"); Assert.That(game.Session, Is.SameAs(session));
        }

        private void AssertTopHit(string name)
        {
            var canvas = view.GetComponentInChildren<Canvas>();
            var camera = new GameObject("Navigation test camera").AddComponent<Camera>();
            var target = new RenderTexture(Screen.width, Screen.height, 24);
            try {
                camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -10); camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); camera.Render();
                var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(camera, Find(name).transform.position) };
                var hits = new List<RaycastResult>(); canvas.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
                Assert.That(hits.Count, Is.GreaterThan(0), name + " must receive pointer input");
                Assert.That(hits[0].gameObject, Is.SameAs(Find(name)), name + " must not be covered by another element");
            }
            finally {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
                camera.targetTexture = null; Object.DestroyImmediate(target); Object.DestroyImmediate(camera.gameObject);
                Canvas.ForceUpdateCanvases();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShopReturnsBetweenLevelsAndClosesForNextLevel(bool failure)
        {
            content.balance.rent = failure ? 50000 : 0;
            Click("ContinueShift");
            Assert.That(game.Dispatch(new GameCommand(ActionKind.ServeOrder)), Is.True);
            Assert.That(game.Dispatch(new GameCommand(ActionKind.Tick, content.balance.dayTicks + content.balance.guestIntervalTicks)), Is.True);
            view.Refresh(); Assert.That(game.Session.Phase, Is.EqualTo(DayPhase.Result));
            Assert.That(game.Session.Success, Is.EqualTo(!failure));
            Invoke("OpenShop"); Assert.That(Find("Shop").activeSelf, Is.False, "Results must be acknowledged first");
            Click("ReturnToMainMenu"); Click("MainMenuShop");
            Assert.That(Find("Shop").activeInHierarchy, Is.True);
            if (!failure) { Click("BuyIngredient1"); Assert.That(game.Profile.Owns(Ingredient.Jam), Is.True); }
            else Assert.That(Find("BuyIngredient1").GetComponent<Button>().interactable, Is.False);
            Click("CloseShop"); Click("ContinueShift"); AssertShopBlocked();
            if (!failure) Assert.That(game.Session.StartProfile.Owns(Ingredient.Jam), Is.True);
        }
    }
}
