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
        private GameView view;
        [SetUp] public void Setup()
        {
            content = ScriptableObject.CreateInstance<GameContent>();
            content.bodyFont = content.titleFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            content.balance = new BalanceRules { readyTicks = 2, burnTicks = 6, dayTicks = 10000, patiencePerPancakeTicks = 100 };
            var store = new MemoryStore { Data = new SavedGame { profile = new Profile { tutorialComplete = true, demonstrationSeen = true, coins = 10000 } } };
            var game = new GameController(store, new LocalDevelopmentGateway(), content.balance);
            view = new GameObject("Free cooking test").AddComponent<GameView>();
            view.Initialize(game, content); Canvas.ForceUpdateCanvases();
            // EditMode has no Update loop to fit the safe area before pointer raycasts.
            typeof(GameView).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(view, null);
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
        { Assert.That(view.Controller.Dispatch(new GameCommand(kind, a, b)), Is.True, kind.ToString()); }
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
            Assert.That(view.Session.ReadyPancakes.Single(), Is.SameAs(first));
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
            Assert.That(view.Session.ReadyPancakes.Single(), Is.SameAs(first));
        }

        [Test] public void PointerCanPourAndFryBeforeAcceptingAnOrder()
        {
            Click("KitchenTab"); Pour(); Click("FinishBatter"); Send(ActionKind.Tick, 2); view.Refresh();
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Offered));
            Assert.That(view.Session.Pancake.HeatA, Is.EqualTo(2));
            Click("FlipHandle"); Send(ActionKind.Tick, 2); view.Refresh(); Click("MoveToPlate");
            Assert.That(view.Session.Pancake.Stage, Is.EqualTo(PancakeStage.Plate));
            Assert.That(Find("KitchenOrderHud").activeInHierarchy, Is.False);
            Assert.That(view.Session.Ledger.Revenue, Is.Zero);
        }

        [Test] public void GuestLeavingDoesNotSwitchAwayFromTheFryingPan()
        {
            Click("AcceptOrder"); Pour(); Click("FinishBatter"); var pancake = view.Session.Pancake;
            Send(ActionKind.Tick, view.Session.Order.PatienceLimit); view.Refresh();
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Cancelled));
            Assert.That(view.Session.Pancake, Is.SameAs(pancake));
            Assert.That(Find("Kitchen").activeInHierarchy, Is.True);
            Click("FlipHandle"); Send(ActionKind.Tick, 2); view.Refresh(); Click("MoveToPlate");
            Assert.That(pancake.Stage, Is.EqualTo(PancakeStage.Plate));
        }

        [Test] public void PreparedStockShowsOnTrayAndSurvivesAcceptance()
        {
            Click("KitchenTab");
            for (int i = 0; i < 3; i++)
            {
                CoreScenarios.Paint(c => Assert.That(view.Controller.Dispatch(c), Is.True), -1);
                Send(ActionKind.FinishBatter); Send(ActionKind.Tick, 2); Send(ActionKind.Flip); Send(ActionKind.Tick, 2); Send(ActionKind.MoveToPlate);
                CoreScenarios.Paint(c => Assert.That(view.Controller.Dispatch(c), Is.True), 0);
                Send(ActionKind.BeginFold); Send(ActionKind.FoldGesture, 0, 400); Send(ActionKind.FoldGesture, 400, 0);
            }
            view.Refresh();
            Assert.That(Find("ReadyPancake0").activeInHierarchy && Find("ReadyPancake1").activeInHierarchy, Is.True);
            Assert.That(Find("TrayCount").GetComponent<Text>().text, Is.EqualTo("Готово: 3"));
            Assert.That(Find("PancakeOnPlate").activeInHierarchy, Is.False);
            Pour(); Click("FinishBatter"); var active = view.Session.Pancake;
            Click("CounterTab"); Click("AcceptOrder");
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(view.Session.TrayCount, Is.EqualTo(3), "Acceptance must keep prepared food on the tray");
            Click("ServeOrder");
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Served));
            Assert.That(view.Session.Pancake, Is.SameAs(active));
            Assert.That(Find("Kitchen").activeInHierarchy, Is.True);
            Assert.That(Find("TrayCount").GetComponent<Text>().text, Is.EqualTo("Готово: 2"));
            Assert.That(Find("ReadyPancake0").activeInHierarchy && Find("ReadyPancake1").activeInHierarchy, Is.True);
        }

        private void PrepareReadyPortion()
        {
            CoreScenarios.Paint(c => Assert.That(view.Controller.Dispatch(c), Is.True), -1);
            Send(ActionKind.FinishBatter); Send(ActionKind.Tick, 2); Send(ActionKind.Flip); Send(ActionKind.Tick, 2); Send(ActionKind.MoveToPlate);
            CoreScenarios.Paint(c => Assert.That(view.Controller.Dispatch(c), Is.True), 0);
            Send(ActionKind.BeginFold); Send(ActionKind.FoldGesture, 0, 400); Send(ActionKind.FoldGesture, 400, 0);
            view.Refresh();
            foreach (var graphic in view.GetComponentsInChildren<PancakeGraphic>(true))
                if (graphic.Motion != null) for (int i = 0; i < 30; i++) graphic.Motion.Advance(.1f);
            view.Refresh(); Canvas.ForceUpdateCanvases();
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

        [Test] public void SelectedReadyPortionCanBeDiscardedWhileBothStationsAreOccupied()
        {
            Click("KitchenTab"); PrepareReadyPortion(); var ready = view.Session.ReadyPancakes.Single();
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
            var first = view.Session.ReadyPancakes[0]; var third = view.Session.ReadyPancakes[2];
            Click("TrayNext");
            Assert.That(Find("ReadyPancake0").GetComponent<ReadyPancakeDrag>().Pancake, Is.SameAs(third));
            Assert.That(Find("ReadyPancake1").activeSelf, Is.False);
            TouchReady(0, true);
            Assert.That(view.Session.TrayCount, Is.EqualTo(2));
            Assert.That(view.Session.FindTrayPancake(third.Id), Is.Null);
            Assert.That(Find("ReadyPancake0").GetComponent<ReadyPancakeDrag>().Pancake, Is.SameAs(first));
            Assert.That(Find("TrayNext").activeSelf, Is.False);
        }

        [Test] public void ExplicitServeWaitsForReplacementAndWorksFromCounter()
        {
            Click("AcceptOrder");
            Assert.That(Find("ServeOrder").GetComponent<Button>().interactable, Is.False);
            PrepareReadyPortion();
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Accepted));
            Assert.That(Find("ServeOrder").GetComponent<Button>().interactable, Is.True);
            TouchReady(0, false); Click("Discard");
            Assert.That(Find("ServeOrder").GetComponent<Button>().interactable, Is.False);
            PrepareReadyPortion(); Click("CounterTab"); Click("ServeOrderCounter");
            Assert.That(view.Session.Order.Phase, Is.EqualTo(OrderPhase.Served));
            Assert.That(view.Session.Reviews.Count, Is.EqualTo(1));
            Assert.That(Find("ServeOrderCounter").activeSelf, Is.False);
        }
    }
}
