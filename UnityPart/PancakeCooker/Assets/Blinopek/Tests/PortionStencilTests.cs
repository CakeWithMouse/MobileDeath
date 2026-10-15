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
    public sealed class PortionStencilTests
    {
        private sealed class MemoryStore : IGameStore
        {
            public SavedGame Data;
            public SavedGame Load() => Data;
            public void Save(SavedGame value) { Data = value; }
        }
        private GameContent content;
        private GameView view;
        private MemoryStore store;
        private RectTransform Plate => Find("PlateSurface").GetComponent<RectTransform>();
        private Text[] Guides => view.GetComponentsInChildren<Text>(true).Where(t => t.name == "PortionGuide").ToArray();
        private GameObject Find(string name) => view.GetComponentsInChildren<Transform>(true).Single(t => t.name == name).gameObject;
        private static Vector2 Screen(RectTransform rect, float x = .5f, float y = .5f)
            => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector2((x - .5f) * rect.rect.width, (y - .5f) * rect.rect.height)));
        private void Refresh() { view.Refresh(); Canvas.ForceUpdateCanvases(); }
        private void Click(string name) { Find(name).GetComponent<Button>().onClick.Invoke(); Refresh(); }
        private void Send(ActionKind kind, int a = 0, int b = 0, int c = 0)
            => Assert.That(view.Command(kind, a, b, c), Is.True, kind.ToString());
        private PointerEventData Begin(int type)
        {
            var source = Find("Ingredient" + type);
            var e = new PointerEventData(EventSystem.current) { pointerId = 7, position = Screen(source.GetComponent<RectTransform>()) };
            ExecuteEvents.Execute(source, e, ExecuteEvents.pointerDownHandler); Refresh(); return e;
        }
        private void Drop(int type, Vector2 position)
        {
            var e = Begin(type); e.position = position;
            ExecuteEvents.Execute(Find("Ingredient" + type), e, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(Find("Ingredient" + type), e, ExecuteEvents.pointerUpHandler); Refresh();
        }
        private int Composition(int type) => QualityEvaluator.EvaluatePancake(view.Session.PlatePancake,
            new PortionSpec(1 << type, Fold.Triangle), view.Session.Rules).Composition;
        private void Cook()
        {
            CoreScenarios.Paint(command => Assert.That(view.Controller.Dispatch(command), Is.True), -1);
            Send(ActionKind.FinishBatter); Send(ActionKind.Tick, 2); Send(ActionKind.Flip);
            Send(ActionKind.Tick, 2); Send(ActionKind.MoveToPlate); Refresh();
        }
        [SetUp] public void Setup()
        {
            content = ScriptableObject.CreateInstance<GameContent>();
            content.bodyFont = content.titleFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            content.balance = new BalanceRules { readyTicks = 2, burnTicks = 6, dayTicks = 10000, patiencePerPancakeTicks = 10000 };
            store = new MemoryStore { Data = new SavedGame { profile = new Profile {
                tutorialComplete = true, demonstrationSeen = true, coins = 10000, ownedIngredients = 31, scrapers = 3 } } };
            view = new GameObject("Portion stencil tests").AddComponent<GameView>();
            view.Initialize(new GameController(store, new LocalDevelopmentGateway(), content.balance), content);
            typeof(GameView).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(view, null);
            Refresh(); Click("ContinueShift"); Click("KitchenTab"); Cook();
        }
        [TearDown] public void Cleanup()
        {
            if (view != null) Object.DestroyImmediate(view.gameObject);
            if (content != null) Object.DestroyImmediate(content);
        }

        [TestCase(3, .6f), TestCase(3, 1f), TestCase(3, 1.2f)]
        [TestCase(4, .6f), TestCase(4, 1f), TestCase(4, 1.2f)]
        public void DropsOnVisibleTargetsMatchStoredPositionsAndScore(int type, float scale)
        {
            Find("SafeArea").transform.localScale = Vector3.one * scale; Refresh();
            int cost = view.Session.Ledger.Products;
            for (int i = 0; i < Recipes.PortionTargets.Length; i++)
            {
                var guide = Guides[i]; Drop(type, Screen(guide.rectTransform));
                var p = view.Session.PlatePancake.Fillings[type].Portions[i];
                Assert.That(p.X, Is.EqualTo(Recipes.PortionTargets[i].X));
                Assert.That(p.Y, Is.EqualTo(Recipes.PortionTargets[i].Y));
            }
            Assert.That(Composition(type), Is.EqualTo(100));
            Assert.That(view.Session.Ledger.Products - cost, Is.EqualTo(4 * content.balance.portionCost));
            Assert.That(view.OwnsPointer(7), Is.False);
            Assert.That(Find("DraggedPortion").activeSelf, Is.False);
            var replay = new GameController(store, new LocalDevelopmentGateway(), content.balance);
            for (int i = 0; i < 4; i++)
            {
                var p = replay.Session.PlatePancake.Fillings[type].Portions[i];
                Assert.That(p.X, Is.EqualTo(Recipes.PortionTargets[i].X));
                Assert.That(p.Y, Is.EqualTo(Recipes.PortionTargets[i].Y));
            }
        }

        [TestCase(3), TestCase(4)] public void MissesAndDuplicatesDoNotFillOtherTargets(int type)
        {
            Drop(type, Screen(Plate)); Assert.That(Composition(type), Is.Zero);
            Assert.That(Guides.Count(g => g.gameObject.activeSelf), Is.EqualTo(4));
            Send(ActionKind.Remove, type);
            for (int i = 0; i < 4; i++) Drop(type, Screen(Guides[0].rectTransform));
            Assert.That(Composition(type), Is.EqualTo(25));
            Assert.That(view.Session.PlatePancake.Fillings[type].Portions.Count, Is.EqualTo(4));
            Assert.That(Guides.Count(g => g.gameObject.activeSelf), Is.EqualTo(3));
        }

        [TestCase(3), TestCase(4)] public void StencilGlyphsAreVisibleWithTheActualGameFont(int type)
        {
            var font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/Blinopek/ui/fonts/PT_Sans-Web-Regular.ttf")
                ?? UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/PT_Sans-Web-Regular.ttf");
            Assert.That(font, Is.Not.Null, "Import the actual game font into the validation project");
            foreach (var guide in Guides) guide.font = font;
            view.SelectIngredient(type); Refresh();
            foreach (var guide in Guides)
            {
                Assert.That(guide.gameObject.activeInHierarchy, Is.True);
                Assert.That(guide.canvasRenderer.GetMesh().vertexCount, Is.GreaterThanOrEqualTo(4), "Stencil mark must generate visible geometry");
            }
        }

        [TestCase(3), TestCase(4)] public void DropsOutsidePancakeAndClicksOnContainerCostNothing(int type)
        {
            int cost = view.Session.Ledger.Products;
            Drop(type, Screen(Plate, 0, 0)); Drop(type, Screen(Plate, 1.2f, .5f));
            var e = Begin(type); ExecuteEvents.Execute(Find("Ingredient" + type), e, ExecuteEvents.pointerUpHandler);
            Assert.That(view.Session.PlatePancake.Fillings[type], Is.Null);
            Assert.That(view.Session.Ledger.Products, Is.EqualTo(cost));
        }

        [TestCase(3), TestCase(4)] public void FilledTargetsHideOnlyForTheirIngredientAndReturnAfterScraping(int type)
        {
            Drop(type, Screen(Guides[0].rectTransform));
            Assert.That(Guides.Count(g => g.gameObject.activeSelf), Is.EqualTo(3), "Filled stencil mark must stop inviting another portion");
            view.SelectIngredient(type == 3 ? 4 : 3); Refresh();
            Assert.That(Guides.Count(g => g.gameObject.activeSelf), Is.EqualTo(4));
            view.SelectIngredient(type); Send(ActionKind.Remove, type); Refresh();
            Assert.That(Guides.Count(g => g.gameObject.activeSelf), Is.EqualTo(4));
            Send(ActionKind.BeginFold, 0); Refresh();
            Assert.That(Guides.Any(g => g.gameObject.activeSelf), Is.False);
        }

        [TestCase(3), TestCase(4)] public void OtherFingerCannotMoveHeldPortion(int type)
        {
            Begin(type); var ghost = Find("DraggedPortion");
            var other = new PointerEventData(EventSystem.current) { pointerId = 8, position = Screen(Plate) };
            ExecuteEvents.Execute(Find("Ingredient" + type), other, ExecuteEvents.dragHandler);
            Assert.That(ghost.activeSelf, Is.False, "Uncaptured finger must not move the portion preview");
            ExecuteEvents.Execute(Find("Ingredient" + type), other, ExecuteEvents.pointerUpHandler);
            Assert.That(view.Session.PlatePancake.Fillings[type], Is.Null);
            Assert.That(view.OwnsPointer(7), Is.True);
        }

        [TestCase(3), TestCase(4)] public void CancelledGestureCannotRestoreGhost(int type)
        {
            var e = Begin(type);
            var focus = typeof(GameView).GetMethod("OnApplicationFocus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            focus.Invoke(view, new object[] { false }); focus.Invoke(view, new object[] { true });
            e.position = Screen(Plate);
            ExecuteEvents.Execute(Find("Ingredient" + type), e, ExecuteEvents.dragHandler);
            Assert.That(Find("DraggedPortion").activeSelf, Is.False, "Cancelled drag must not recreate its preview after focus returns");
            ExecuteEvents.Execute(Find("Ingredient" + type), e, ExecuteEvents.pointerUpHandler);
            Assert.That(Find("DraggedPortion").activeSelf, Is.False);
            Assert.That(view.Session.PlatePancake.Fillings[type], Is.Null);
        }

        [TestCase(3), TestCase(4)] public void DropCannotJumpToReplacementPancake(int type)
        {
            var e = Begin(type); Send(ActionKind.Discard, view.Session.PlatePancake.Id); Cook();
            int cost = view.Session.Ledger.Products; e.position = Screen(Plate);
            ExecuteEvents.Execute(Find("Ingredient" + type), e, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(Find("Ingredient" + type), e, ExecuteEvents.pointerUpHandler);
            Assert.That(view.Session.PlatePancake.Fillings[type], Is.Null);
            Assert.That(view.Session.Ledger.Products, Is.EqualTo(cost));
            Assert.That(Find("DraggedPortion").activeSelf, Is.False);
        }

        [TestCase(3, 140, 25, 3), TestCase(3, 141, 0, 4)]
        [TestCase(4, 140, 25, 3), TestCase(4, 141, 0, 4)]
        public void StencilUsesTheSameToleranceAsScoring(int type, int offset, int score, int remaining)
        {
            Drop(type, Screen(Plate, (320 + offset) / 1000f, .32f));
            Assert.That(Composition(type), Is.EqualTo(score));
            Assert.That(Guides.Count(g => g.gameObject.activeSelf), Is.EqualTo(remaining));
        }

        [TestCase(3), TestCase(4)] public void HelperFillsStencilWithoutAnExtraManualPortion(int type)
        {
            view.Session.StartProfile.helpersOwned = view.Session.StartProfile.helpersEnabled = 1 << type;
            int cost = view.Session.Ledger.Products; var e = Begin(type); e.position = Screen(Plate);
            ExecuteEvents.Execute(Find("Ingredient" + type), e, ExecuteEvents.dragHandler);
            Assert.That(Find("DraggedPortion").activeSelf, Is.False);
            ExecuteEvents.Execute(Find("Ingredient" + type), e, ExecuteEvents.pointerUpHandler); Refresh();
            Assert.That(view.Session.PlatePancake.Fillings[type].Portions.Count, Is.EqualTo(4));
            Assert.That(Composition(type), Is.EqualTo(100));
            Assert.That(view.Session.Ledger.Products - cost, Is.EqualTo(4 * content.balance.portionCost));
            Assert.That(Guides.Any(g => g.gameObject.activeSelf), Is.False);
        }

        [TestCase(3), TestCase(4)] public void IngredientsCannotStartDraggingWithoutPlatePancake(int type)
        {
            Send(ActionKind.Discard, view.Session.PlatePancake.Id); Refresh();
            var e = Begin(type); e.position = Screen(Plate);
            ExecuteEvents.Execute(Find("Ingredient" + type), e, ExecuteEvents.dragHandler);
            Assert.That(view.OwnsPointer(7), Is.False);
            Assert.That(Find("DraggedPortion").activeSelf, Is.False);
        }
    }
}
