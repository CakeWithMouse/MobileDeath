using System.Linq;
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
    public sealed class FreeCookingViewTests
    {
        private sealed class MemoryStore : IGameStore
        {
            public SavedGame Data;
            public SavedGame Load() => Data;
            public void Save(SavedGame data) { Data = data; }
        }
        private GameContent content;
        private GameSystem view;
        private GameController game;
        [SetUp] public void Setup()
        {
            content = ScriptableObject.CreateInstance<GameContent>();
            content.bodyFont = content.titleFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            content.balance = new BalanceRules { readyTicks = 2, burnTicks = 6, dayTicks = 10000, patiencePerPancakeTicks = 100 };
            var store = new MemoryStore { Data = new SavedGame { profile = new Profile { tutorialComplete = true, demonstrationSeen = true, coins = 10000 } } };
            game = new GameController(store, new LocalDevelopmentGateway(), content.balance);
            view = new GameObject("Free cooking test").AddComponent<GameSystem>();
            view.Initialize(game, content); Canvas.ForceUpdateCanvases();
            // EditMode has no Update loop to fit the safe area before pointer raycasts.
            typeof(GameSystem).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(view, null);
            Canvas.ForceUpdateCanvases(); Click("ContinueShift");
        }
        [TearDown] public void Cleanup()
        {
            if (view != null) Object.DestroyImmediate(view.gameObject);
            if (content != null) Object.DestroyImmediate(content);
        }
        private GameObject Find(string name) => view.GetComponentsInChildren<Transform>(true).Single(t => t.name == name).gameObject;
        private void Click(string name)
        {
            var b = Find(name).GetComponent<Button>();
            Assert.That(b.gameObject.activeInHierarchy && b.IsInteractable(), Is.True, name + " must be available");
            b.onClick.Invoke(); view.Refresh(); Canvas.ForceUpdateCanvases();
        }
        private void Pour()
        {
            var surface = Find("PanSurface"); var rect = surface.GetComponent<RectTransform>();
            var e = new PointerEventData(EventSystem.current) { pointerId = 3, position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(Vector3.zero)) };
            ExecuteEvents.Execute(surface, e, ExecuteEvents.pointerDownHandler);
            e.position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3(60, 0, 0)));
            ExecuteEvents.Execute(surface, e, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(surface, e, ExecuteEvents.pointerUpHandler);
            view.Refresh();
            Assert.That(view.Session.Pancake, Is.Not.Null, "Pointer input must pour without requiring acceptance");
            Assert.That(view.Session.Pancake.Batter.Count, Is.GreaterThan(0));
            Assert.That(view.OwnsPointer(3), Is.False);
        }
        private void Send(ActionKind kind, int a = 0, int b = 0)
        { Assert.That(game.Dispatch(new GameCommand(kind, a, b)), Is.True, kind.ToString()); }
        private void SwipePlate(Vector2 delta)
        {
            var surface = Find("PlateSurface"); var rect = surface.GetComponent<RectTransform>();
            var e = new PointerEventData(EventSystem.current) { pointerId = 4, position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(Vector3.zero)) };
            ExecuteEvents.Execute(surface, e, ExecuteEvents.pointerDownHandler);
            e.position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(delta));
            ExecuteEvents.Execute(surface, e, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(surface, e, ExecuteEvents.pointerUpHandler);
            view.Refresh();
        }
        private void FirstPancakeOnPlate()
        {
            Click("KitchenTab"); Pour(); Click("FinishBatter"); Send(ActionKind.Tick, 2); view.Refresh();
            Click("FlipHandle"); Send(ActionKind.Tick, 2); view.Refresh(); Click("MoveToPlate");
        }

        [Test] public void PanCanFryWhilePlateIsBeingFilledAndFolded()
        {
            FirstPancakeOnPlate(); var first = view.Session.PlatePancake;
            Pour(); var second = view.Session.PanPancake; Click("FinishBatter");
            view.SelectIngredient(0);
            SwipePlate(new Vector2(60, 0));
            Assert.That(first.Fillings[0].Mask.Count, Is.GreaterThan(0));
            Assert.That(second.IngredientBits, Is.Zero);
            Click("Fold0"); SwipePlate(new Vector2(0, 40));
            Assert.That(first.Folds.Count, Is.EqualTo(1));
            Assert.That(second.Folds.Count, Is.Zero);
            Send(ActionKind.Tick, 2); view.Refresh(); Click("FlipHandle"); Send(ActionKind.Tick, 2); view.Refresh();
            Assert.That(Find("MoveToPlate").GetComponent<Button>().interactable, Is.False, "Occupied plate must block transfer");
            Assert.That(Find("PancakeOnPan").activeInHierarchy && Find("PancakeOnPlate").activeInHierarchy, Is.True);
            SwipePlate(new Vector2(40, 0));
            Assert.That(view.Session.TrayPancakeAt(0), Is.SameAs(first));
            Assert.That(view.Session.PanPancake, Is.SameAs(second));
            Assert.That(Find("PancakeOnPlate").GetComponentInChildren<PancakeGraphic>().Motion.State, Is.SameAs(first), "Finishing animation must stay on the plate while the second pancake fries");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PointerCanPourDuringPartialOrFinalFold(bool finalFold)
        {
            FirstPancakeOnPlate(); var first = view.Session.PlatePancake;
            Click("Fold0"); SwipePlate(new Vector2(0, 40));
            if (finalFold) SwipePlate(new Vector2(40, 0));
            Pour(); var second = view.Session.PanPancake;
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(Find("PancakeOnPlate").activeInHierarchy, Is.True);
            Click("FinishBatter"); Send(ActionKind.Tick, 2); view.Refresh(); Click("FlipHandle"); Send(ActionKind.Tick, 2); view.Refresh();
            Assert.That(Find("MoveToPlate").GetComponent<Button>().interactable, Is.False, "Transfer must wait for the plate and its finishing animation");
            if (!finalFold) SwipePlate(new Vector2(40, 0));
            var motion = Find("PancakeOnPlate").GetComponentInChildren<PancakeGraphic>().Motion;
            for (int i = 0; i < 20; i++) motion.Advance(.1f);
            view.Refresh(); Click("MoveToPlate");
            Assert.That(view.Session.PlatePancake, Is.SameAs(second));
            Assert.That(view.Session.PanPancake, Is.Null);
            Assert.That(view.Session.TrayPancakeAt(0), Is.SameAs(first));
        }

        [Test] public void OrderIsAcceptedOnceWithoutLeavingTheCounter()
        {
            var order = view.Session.Order;
            Assert.That(order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(Find("Counter").activeInHierarchy, Is.True);
            Assert.That(view.GetComponentsInChildren<Button>(true).Any(b => b.name == "AcceptOrder"), Is.False);
            view.Refresh(); view.Refresh();
            Click("ClarifyOrder");
            Assert.That(order.Clarified, Is.True);
            Click("Pause"); Click("ExitToMainMenu"); Click("ContinueShift");
            Assert.That(view.Session.Order, Is.SameAs(order));
            Assert.That(view.Session.Ledger.Revenue, Is.EqualTo(order.Price));
            Assert.That(Find("Counter").activeInHierarchy, Is.True);
        }

        [Test] public void PointerCanPourAndFryWithoutConfirmingAnOrder()
        {
            Click("KitchenTab"); Pour(); Click("FinishBatter"); Send(ActionKind.Tick, 2); view.Refresh();
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(view.Session.Pancake.HeatA, Is.EqualTo(2));
            Click("FlipHandle"); Send(ActionKind.Tick, 2); view.Refresh(); Click("MoveToPlate");
            Assert.That(view.Session.Pancake.Stage, Is.EqualTo(PancakeStage.Plate));
            Assert.That(Find("KitchenOrderHud").activeInHierarchy, Is.True);
            Assert.That(view.Session.Ledger.Revenue, Is.EqualTo(view.Session.Order.Price));
        }

        [Test] public void GuestLeavingDoesNotSwitchAwayFromTheFryingPan()
        {
            Click("KitchenTab"); Pour(); Click("FinishBatter"); var pancake = view.Session.Pancake;
            Send(ActionKind.Tick, view.Session.Order.PatienceLimit); view.Refresh();
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Cancelled));
            Assert.That(view.Session.Pancake, Is.SameAs(pancake));
            Assert.That(Find("Kitchen").activeInHierarchy, Is.True);
            Click("FlipHandle"); Send(ActionKind.Tick, 2); view.Refresh(); Click("MoveToPlate");
            Assert.That(pancake.Stage, Is.EqualTo(PancakeStage.Plate));
            Send(ActionKind.Tick, view.Session.Rules.guestIntervalTicks - 2); view.Refresh();
            Assert.That(view.Session.Order.Id, Is.EqualTo(2));
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(view.Session.PlatePancake, Is.SameAs(pancake));
            Assert.That(Find("Kitchen").activeInHierarchy, Is.True, "Arriving guests must not interrupt cooking");
        }

        [Test] public void SpeechBubbleIsHiddenWhileWaitingForTheNextGuest()
        {
            Send(ActionKind.Tick, view.Session.Rules.dayTicks + view.Session.Rules.guestIntervalTicks);
            content.balance.guestArrivalBaseTicks = 80; content.balance.guestArrivalMinTicks = 40;
            content.balance.guestArrivalReductionPerDecorationPointTicks = 2;
            Assert.That(game.StartDay(7), Is.True);
            Send(ActionKind.Accept); Send(ActionKind.Tick, view.Session.Order.PatienceLimit);
            Send(ActionKind.Tick, 40); view.Refresh();
            Assert.That(view.Session.GuestHasLeft, Is.True);
            Assert.That(Find("Visitor").activeSelf, Is.False);
            Assert.That(Find("SpeechBubble").activeSelf, Is.False, "Empty speech bubble must not float above the empty counter");
            Send(ActionKind.Tick, 40); view.Refresh();
            Assert.That(view.Session.Order.Id, Is.EqualTo(2));
            Assert.That(Find("Visitor").activeSelf && Find("SpeechBubble").activeSelf && Find("Request").activeSelf, Is.True);
        }

        [Test] public void CounterShowsTheNextGuestWithoutAContinueButton()
        {
            Click("KitchenTab"); PrepareReadyPortion(); Send(ActionKind.ServeOrder); view.Refresh();
            Assert.That(Find("Counter").activeInHierarchy, Is.True);
            Assert.That(view.GetComponentsInChildren<Button>(true).Any(b => b.name == "NextGuest"), Is.False);
            Send(ActionKind.Tick, view.Session.Rules.guestIntervalTicks - 1); view.Refresh();
            Assert.That(Find("Reaction").activeInHierarchy, Is.True);
            Assert.That(view.GetComponentsInChildren<Button>(true).Any(b => b.name == "AcceptOrder"), Is.False);
            Send(ActionKind.Tick, 1); view.Refresh();
            Assert.That(view.Session.Order.Id, Is.EqualTo(2));
            Assert.That(Find("Visitor").activeInHierarchy && Find("Request").activeInHierarchy, Is.True);
            Assert.That(Find("Visitor").GetComponent<RawImage>().color.a, Is.EqualTo(1));
            Assert.That(Find("Reaction").activeInHierarchy, Is.False);
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
        }

        [Test] public void PreparedStockShowsOnTrayAndSurvivesNavigation()
        {
            Click("KitchenTab");
            for (int i = 0; i < 3; i++)
            {
                CoreScenarios.Paint(c => Assert.That(game.Dispatch(c), Is.True), -1);
                Send(ActionKind.FinishBatter); Send(ActionKind.Tick, 2); Send(ActionKind.Flip); Send(ActionKind.Tick, 2); Send(ActionKind.MoveToPlate);
                CoreScenarios.Paint(c => Assert.That(game.Dispatch(c), Is.True), 0);
                Send(ActionKind.BeginFold); Send(ActionKind.FoldGesture, 0, 400); Send(ActionKind.FoldGesture, 400, 0);
            }
            view.Refresh();
            Assert.That(Find("ReadyPancake0").activeInHierarchy && Find("ReadyPancake1").activeInHierarchy, Is.True);
            Assert.That(Find("TrayCount").GetComponent<Text>().text, Is.EqualTo("Готово: 3"));
            Assert.That(Find("PancakeOnPlate").activeInHierarchy, Is.False);
            Pour(); Click("FinishBatter"); var active = view.Session.Pancake;
            Click("CounterTab"); Click("KitchenTab");
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(view.Session.TrayCount, Is.EqualTo(3), "Navigation must keep prepared food on the tray");
            Click("KitchenTray");
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(Find("Counter").activeInHierarchy, Is.True);
            SwipeTray(view.TrayServeDirection * 100);
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Served));
            Assert.That(view.Session.Pancake, Is.SameAs(active));
            Assert.That(Find("Counter").activeInHierarchy, Is.True);
            Assert.That(view.Session.Order.Finished.Count, Is.EqualTo(3), "The entire prepared tray must reach the guest");
            Click("KitchenTab");
            Assert.That(Find("PancakeOnPan").activeInHierarchy, Is.True);
            Assert.That(view.Session.TrayCount, Is.Zero);
        }

        private void PrepareReadyPortion()
        {
            CoreScenarios.Paint(c => Assert.That(game.Dispatch(c), Is.True), -1);
            Send(ActionKind.FinishBatter); Send(ActionKind.Tick, 2); Send(ActionKind.Flip); Send(ActionKind.Tick, 2); Send(ActionKind.MoveToPlate);
            CoreScenarios.Paint(c => Assert.That(game.Dispatch(c), Is.True), 0);
            Send(ActionKind.BeginFold); Send(ActionKind.FoldGesture, 0, 400); Send(ActionKind.FoldGesture, 400, 0);
            view.Refresh();
            foreach (var graphic in view.GetComponentsInChildren<PancakeGraphic>(true))
                if (graphic.Motion != null) for (int i = 0; i < 30; i++) graphic.Motion.Advance(.1f);
            view.Refresh(); Canvas.ForceUpdateCanvases();
        }
        private void SwipeTray(Vector2 delta, System.Action beforeRelease = null)
        {
            var target = Find("DeliveryTray"); var rect = target.GetComponent<RectTransform>();
            var parent = (RectTransform)rect.parent;
            var start = parent.InverseTransformPoint(rect.position);
            var e = new PointerEventData(EventSystem.current) { pointerId = 9, position = RectTransformUtility.WorldToScreenPoint(null, rect.position) };
            ExecuteEvents.Execute(target, e, ExecuteEvents.pointerDownHandler);
            e.position = RectTransformUtility.WorldToScreenPoint(null, parent.TransformPoint(start + (Vector3)delta));
            ExecuteEvents.Execute(target, e, ExecuteEvents.dragHandler);
            beforeRelease?.Invoke();
            ExecuteEvents.Execute(target, e, ExecuteEvents.pointerUpHandler);
            view.Refresh(); Canvas.ForceUpdateCanvases();
            Assert.That(view.OwnsPointer(9), Is.False);
            Assert.That(target.GetComponent<TrayServeDrag>().IsDragging, Is.False);
        }
        private void TouchReady(int slot, bool discard)
        {
            var target = Find("ReadyPancake" + slot);
            VerifyReadyRaycast(target);
            var position = RectTransformUtility.WorldToScreenPoint(null, target.transform.position);
            var e = new PointerEventData(EventSystem.current) { pointerId = 8, position = position };
            ExecuteEvents.Execute(target, e, ExecuteEvents.pointerDownHandler);
            if (discard) {
                e.position = RectTransformUtility.WorldToScreenPoint(null, Find("DiscardZone").transform.position);
                ExecuteEvents.Execute(target, e, ExecuteEvents.dragHandler);
                Assert.That(Find("DraggedReadyPancake").activeInHierarchy, Is.True);
            }
            ExecuteEvents.Execute(target, e, ExecuteEvents.pointerUpHandler);
            view.Refresh(); Canvas.ForceUpdateCanvases();
            Assert.That(view.OwnsPointer(8), Is.False);
            Assert.That(Find("DraggedReadyPancake").activeSelf, Is.False);
        }
        private void VerifyReadyRaycast(GameObject target)
        {
            // GraphicRaycaster needs the depths produced by a rendered Canvas. EditMode
            // has no game frame, so render once before checking the actual hit target.
            var canvas = view.GetComponentInChildren<Canvas>();
            var camera = new GameObject("Input test camera").AddComponent<Camera>();
            var texture = new RenderTexture(Screen.width, Screen.height, 24);
            try {
                camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -10); camera.targetTexture = texture;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); camera.Render();
                var e = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(camera, target.transform.position) };
                var hits = new System.Collections.Generic.List<RaycastResult>();
                canvas.GetComponent<GraphicRaycaster>().Raycast(e, hits);
                Assert.That(hits.Count > 0 && hits[0].gameObject == target, Is.True, "Ready portion must receive the topmost pointer hit");
            }
            finally {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
                camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(camera.gameObject);
                Canvas.ForceUpdateCanvases();
            }
        }

        private void Tap(GameObject target)
        {
            var e = new PointerEventData(EventSystem.current) {
                pointerId = 8, position = RectTransformUtility.WorldToScreenPoint(null, target.transform.position)
            };
            ExecuteEvents.Execute(target, e, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(target, e, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(target, e, ExecuteEvents.pointerClickHandler);
            view.Refresh(); Canvas.ForceUpdateCanvases();
        }

        [TestCase("KitchenTray")]
        [TestCase("ReadyPancake0")]
        [TestCase("ReadyPancake1")]
        public void TappingAnyPartOfPreparedTrayOpensCounter(string targetName)
        {
            Click("KitchenTab"); PrepareReadyPortion(); PrepareReadyPortion();
            Assert.That(view.GetComponentsInChildren<Transform>(true).Any(t => t.name == "ServeOrder"), Is.False);
            var target = Find(targetName); VerifyReadyRaycast(target); Tap(target);
            Assert.That(Find("Counter").activeInHierarchy, Is.True);
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(view.Session.TrayCount, Is.EqualTo(2));
            Assert.That(view.Session.Reviews, Is.Empty);
            Assert.That(view.OwnsPointer(8), Is.False);
        }

        [TestCase("KitchenTray")]
        [TestCase("ReadyPancake0")]
        public void TrayTapCannotStartServingWhilePausedOrWithoutAnActiveOrder(string targetName)
        {
            Click("KitchenTab"); PrepareReadyPortion(); var target = Find(targetName);
            game.SetPause(PauseReason.Menu, true); view.Refresh(); Tap(target);
            Assert.That(Find("Kitchen").activeInHierarchy, Is.True);
            game.SetPause(PauseReason.Menu, false); view.Refresh();
            Send(ActionKind.Tick, view.Session.Order.PatienceLimit); view.Refresh(); Tap(target);
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Cancelled));
            Assert.That(Find("Kitchen").activeInHierarchy, Is.True);
        }

        [Test] public void DraggingPreparedPortionDoesNotOpenCounter()
        {
            Click("KitchenTab"); PrepareReadyPortion(); PrepareReadyPortion();
            TouchReady(0, true);
            Assert.That(view.Session.TrayCount, Is.EqualTo(1));
            Assert.That(Find("Kitchen").activeInHierarchy, Is.True);
            // Some input modules still deliver a click after releasing a drag over its source.
            ExecuteEvents.Execute(Find("ReadyPancake0"), new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            view.Refresh();
            Assert.That(Find("Kitchen").activeInHierarchy, Is.True);
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
        }

        [Test] public void SelectedReadyPortionCanBeDiscardedWhileBothStationsAreOccupied()
        {
            Click("KitchenTab"); PrepareReadyPortion(); var ready = view.Session.TrayPancakeAt(0);
            Pour(); Click("FinishBatter"); Send(ActionKind.Tick, 2); view.Refresh(); Click("FlipHandle");
            Send(ActionKind.Tick, 2); view.Refresh(); Click("MoveToPlate"); var plate = view.Session.PlatePancake;
            Pour(); var pan = view.Session.PanPancake;
            TouchReady(0, false);
            Assert.That(Find("ReadyPancake0").transform.Find("SelectedReady").gameObject.activeSelf, Is.True);
            Click("Discard");
            Assert.That(view.Session.FindTrayPancake(ready.Id), Is.Null);
            Assert.That(view.Session.PanPancake, Is.SameAs(pan));
            Assert.That(view.Session.PlatePancake, Is.SameAs(plate));
        }

        [Test] public void TrayPagesAllowDraggingTheThirdPortionToTrash()
        {
            Click("KitchenTab"); PrepareReadyPortion(); PrepareReadyPortion(); PrepareReadyPortion();
            var first = view.Session.TrayPancakeAt(0); var third = view.Session.TrayPancakeAt(2);
            Click("TrayNext");
            Assert.That(Find("ReadyPancake0").GetComponent<ReadyPancakeDrag>().Pancake, Is.SameAs(third));
            Assert.That(Find("ReadyPancake1").activeSelf, Is.False);
            TouchReady(0, true);
            Assert.That(view.Session.TrayCount, Is.EqualTo(2));
            Assert.That(view.Session.FindTrayPancake(third.Id), Is.Null);
            Assert.That(Find("ReadyPancake0").GetComponent<ReadyPancakeDrag>().Pancake, Is.SameAs(first));
            Assert.That(Find("TrayNext").activeSelf, Is.False);
        }

        [Test] public void ExplicitServeStaysAvailableDuringReplacementAndWorksFromCounter()
        {
            Click("KitchenTab");
            Assert.That(Find("KitchenTray").GetComponent<Button>().interactable, Is.True);
            PrepareReadyPortion();
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(Find("KitchenTray").GetComponent<Button>().interactable, Is.True);
            TouchReady(0, false); Click("Discard");
            Assert.That(Find("KitchenTray").GetComponent<Button>().interactable, Is.True);
            PrepareReadyPortion(); Click("CounterTab");
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(view.Session.Reviews, Is.Empty);
            Assert.That(Find("DeliveryHint").activeInHierarchy, Is.True);
            VerifyReadyRaycast(Find("DeliveryTray"));
            SwipeTray(view.TrayServeDirection * 100);
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Served));
            Assert.That(view.Session.Reviews.Count, Is.EqualTo(1));
            Assert.That(Find("DeliveryHint").activeInHierarchy, Is.False);
            int balance = view.Session.Balance;
            SwipeTray(view.TrayServeDirection * 100);
            Assert.That(view.Session.Reviews.Count, Is.EqualTo(1));
            Assert.That(view.Session.Balance, Is.EqualTo(balance));
        }

        [Test] public void EmptyTrayCanBeHandedOverFromEitherScreen()
        {
            Click("KitchenTab");
            VerifyReadyRaycast(Find("KitchenTray"));
            Click("KitchenTray");
            VerifyReadyRaycast(Find("DeliveryTray"));
            SwipeTray(view.TrayServeDirection * 100);
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Served));
            Assert.That(view.Session.Order.Report.Total, Is.Zero);
            Assert.That(view.Session.Order.Report.Refund, Is.EqualTo(view.Session.Order.Price));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KitchenTrayRemainsClickableDuringPartialAndFinalFold(bool finalFold)
        {
            Click("KitchenTab"); PrepareReadyPortion();
            Pour(); Click("FinishBatter"); Send(ActionKind.Tick, 2); view.Refresh();
            Click("FlipHandle"); Send(ActionKind.Tick, 2); view.Refresh(); Click("MoveToPlate");
            var folding = view.Session.PlatePancake;
            Click("Fold0"); SwipePlate(new Vector2(0, 40));
            if (finalFold) SwipePlate(new Vector2(40, 0));
            VerifyReadyRaycast(Find("KitchenTray"));
            Click("KitchenTray"); SwipeTray(view.TrayServeDirection * 100);
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Served));
            Assert.That(view.Session.Order.Finished.Count, Is.EqualTo(finalFold ? 2 : 1));
            Click("KitchenTab");
            if (finalFold) Assert.That(Find("PancakeOnPlate").activeInHierarchy, Is.False, "Delivered pancake must not reappear in its finishing animation");
            else Assert.That(view.Session.PlatePancake, Is.SameAs(folding), "Serving must preserve unfinished cooking");
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, true)]
        public void KitchenTrayAlwaysOpensCounterBeforeHandoff(bool busyPan, bool busyPlate, bool sparePortion)
        {
            Click("KitchenTab"); PrepareReadyPortion();
            if (sparePortion) PrepareReadyPortion();
            if (busyPlate) {
                Pour(); Click("FinishBatter"); Send(ActionKind.Tick, 2); view.Refresh();
                Click("FlipHandle"); Send(ActionKind.Tick, 2); view.Refresh(); Click("MoveToPlate");
            }
            if (busyPan) { Pour(); Click("FinishBatter"); }
            var pan = view.Session.PanPancake; var plate = view.Session.PlatePancake;
            int balance = view.Session.Balance, trayCount = view.Session.TrayCount;
            Assert.That(view.CanServeTray, Is.False, "A hidden counter cannot accept tray input");
            SwipeTray(view.TrayServeDirection * 100);
            Click("KitchenTray");
            Assert.That(Find("Kitchen").activeInHierarchy, Is.False);
            Assert.That(Find("Counter").activeInHierarchy && view.CanServeTray, Is.True);
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(view.Session.Order.Report, Is.Null);
            Assert.That(view.Session.Reviews, Is.Empty);
            Assert.That(view.Session.Balance, Is.EqualTo(balance));
            Assert.That(view.Session.TrayCount, Is.EqualTo(trayCount));
            Assert.That(view.Session.PanPancake, Is.SameAs(pan));
            Assert.That(view.Session.PlatePancake, Is.SameAs(plate));
            SwipeTray(view.TrayServeDirection * 100);
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Served));
            Assert.That(Find("Counter").activeInHierarchy, Is.True);
            Assert.That(view.Session.PanPancake, Is.SameAs(pan));
            Assert.That(view.Session.PlatePancake, Is.SameAs(plate));
        }

        [Test] public void TrayRequiresMovementTowardGuestAndReturnsAfterInvalidGestures()
        {
            Click("KitchenTab"); PrepareReadyPortion(); Click("KitchenTray");
            var tray = Find("DeliveryTray").GetComponent<RectTransform>(); var home = tray.anchoredPosition;
            var direction = view.TrayServeDirection;
            foreach (var delta in new[] { Vector2.zero, direction * 20, -direction * 100, new Vector2(-direction.y, direction.x) * 100 }) {
                SwipeTray(delta);
                Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
                Assert.That(view.Session.Reviews, Is.Empty);
                Assert.That(tray.anchoredPosition, Is.EqualTo(home));
            }
            SwipeTray(direction * 100, () => {
                Assert.That(Vector2.Dot(tray.anchoredPosition - home, direction), Is.GreaterThan(70));
                Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted), "The guest receives food only on release");
            });
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Served));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InterruptedTrayGestureCannotHandOverOrder(bool pause)
        {
            Click("KitchenTab"); PrepareReadyPortion(); Click("KitchenTray");
            SwipeTray(view.TrayServeDirection * 100, () => {
                if (pause) game.SetPause(PauseReason.Menu, true);
                else Send(ActionKind.Tick, view.Session.Order.PatienceLimit);
                view.Refresh();
            });
            Assert.That(view.Session.Order.Phase, Is.EqualTo(pause ? OrderPhase.Accepted : OrderPhase.Cancelled));
            Assert.That(view.Session.Order.Phase, Is.Not.EqualTo(OrderPhase.Served));
            if (pause) {
                game.SetPause(PauseReason.Menu, false); view.Refresh();
                SwipeTray(view.TrayServeDirection * 100);
                Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Served));
            }
        }
    }
}
