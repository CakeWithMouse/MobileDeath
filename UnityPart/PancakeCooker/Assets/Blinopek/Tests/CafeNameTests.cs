using System;
using System.IO;
using System.Linq;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;
using Blinopek.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Blinopek.Tests
{
    public sealed class CafeNameTests
    {
        private string directory, file;
        private BalanceRules rules;
        private GameController game;
        private GameContent content;
        private GameView view;
        [SetUp] public void Setup()
        {
            directory = Path.GetFullPath(Path.Combine("Library", "BlinopekTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory); file = Path.Combine(directory, "name.json");
            rules = new BalanceRules { dayTicks = 2, guestIntervalTicks = 1, rent = 0 };
            game = new GameController(new FileGameStore(file), new LocalDevelopmentGateway(), rules);
            game.Profile.tutorialComplete = true;
        }
        [TearDown] public void Cleanup()
        {
            if (view != null) Object.DestroyImmediate(view.gameObject);
            if (content != null) Object.DestroyImmediate(content);
            string root = Path.GetFullPath(Path.Combine("Library", "BlinopekTests")) + Path.DirectorySeparatorChar;
            if (directory.StartsWith(root, StringComparison.OrdinalIgnoreCase)) Directory.Delete(directory, true);
        }
        private GameController Reload() => new GameController(new FileGameStore(file), new LocalDevelopmentGateway(), rules);
        private void BuildView()
        {
            content = ScriptableObject.CreateInstance<GameContent>(); content.balance = rules;
            content.bodyFont = content.titleFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            view = new GameObject("Cafe name test").AddComponent<GameView>(); view.Initialize(game, content);
            Canvas.ForceUpdateCanvases();
        }
        private GameObject Find(string name) => view.GetComponentsInChildren<Transform>(true).Single(t => t.name == name).gameObject;
        private void Click(string name)
        {
            var button = Find(name).GetComponent<Button>();
            Assert.That(button.gameObject.activeInHierarchy && button.IsInteractable(), Is.True, name);
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            view.Refresh(); Canvas.ForceUpdateCanvases();
        }
        [Test] public void OldSaveWithoutNameUsesDefault()
        {
            var saved = JsonUtility.FromJson<SavedGame>("{\"schema\":1,\"profile\":{\"day\":1},\"awaitingDay\":true}");
            saved.rules = rules; new FileGameStore(file).Save(saved);
            Assert.That(Reload().CafeName, Is.EqualTo("Блинопёк"));
        }
        [Test] public void RenameBeforeFirstDayPersistsWithoutStartingSession()
        {
            Assert.That(game.RenameCafe("  Ёжик   и\tблин  ", out _), Is.True);
            Assert.That(game.CafeName, Is.EqualTo("Ёжик и блин"));
            var restored = Reload(); Assert.That(restored.CafeName, Is.EqualTo(game.CafeName)); Assert.That(restored.Session, Is.Null);
        }
        [TestCase(0)] [TestCase(50000)]
        public void NameSurvivesSettlementNextDayRestartAndProgressReset(int rent)
        {
            rules.rent = rent; game.StartDay(12);
            game.SetPause(PauseReason.Menu, true);
            Assert.That(game.RenameCafe("Мёд и масло", out _), Is.True);
            Assert.That(game.Paused && game.Session.ElapsedTicks == 0, Is.True);
            Assert.That(game.RestartDay(), Is.True); Assert.That(game.CafeName, Is.EqualTo("Мёд и масло"));
            game.Dispatch(new GameCommand(ActionKind.Tick, 3)); Assert.That(game.Session.Phase, Is.EqualTo(DayPhase.Result));
            game.StartDay(13); Assert.That(Reload().CafeName, Is.EqualTo("Мёд и масло"));
            Assert.That(game.ResetProgress(), Is.True); Assert.That(Reload().CafeName, Is.EqualTo("Мёд и масло"));
        }
        [TestCase("")] [TestCase("  \n\t ")] [TestCase("Название, которое гораздо длиннее тридцати двух символов")]
        public void InvalidNamesNeverReplaceTheSavedName(string name)
        {
            game.RenameCafe("Наш дом", out _); string before = File.ReadAllText(file);
            Assert.That(game.RenameCafe(name, out var error), Is.False); Assert.That(error, Is.Not.Empty);
            Assert.That(game.CafeName, Is.EqualTo("Наш дом")); Assert.That(File.ReadAllText(file), Is.EqualTo(before));
        }
        [Test] public void SaveFailureRetainsNameAndCanRetry()
        {
            game.RenameCafe("Наш дом", out _); string before = File.ReadAllText(file);
            Directory.CreateDirectory(file + ".writing");
            Assert.That(game.RenameCafe("Новое имя", out _), Is.False); Assert.That(game.CafeName, Is.EqualTo("Наш дом"));
            Assert.That(File.ReadAllText(file), Is.EqualTo(before));
            Directory.Delete(file + ".writing"); Assert.That(game.RenameCafe("Новое имя", out _), Is.True);
            Assert.That(Reload().CafeName, Is.EqualTo("Новое имя"));
        }
        [Test] public void PointerClickOpensEditorCancelDiscardsAndSubmitSaves()
        {
            BuildView(); Click("CafeName"); Assert.That(Find("CafeNameDialog").activeInHierarchy, Is.True);
            var input = Find("CafeNameInput").GetComponent<InputField>(); input.text = "Черновик";
            Click("CancelCafeName"); Assert.That(game.CafeName, Is.EqualTo("Блинопёк"));
            Click("CafeName"); Assert.That(input.text, Is.EqualTo("Блинопёк")); input.text = "";
            Assert.That(Find("SaveCafeName").GetComponent<Button>().interactable, Is.False);
            input.text = "Тёплая блинная"; input.onSubmit.Invoke(input.text); view.Refresh();
            Assert.That(Find("CafeNameDialog").activeSelf, Is.False);
            Assert.That(Find("CafeName").GetComponent<Text>().text, Is.EqualTo("Тёплая блинная"));
            Assert.That(game.Paused, Is.True); Assert.That(game.Session, Is.Null);
            Assert.That(Reload().CafeName, Is.EqualTo("Тёплая блинная"));
        }
        [Test] public void DialogStaysOpenOnSaveFailureAndBlocksStartingDay()
        {
            BuildView(); Click("CafeName"); Find("CafeNameInput").GetComponent<InputField>().text = "Ёлки";
            Directory.CreateDirectory(file + ".writing"); Click("SaveCafeName");
            Assert.That(Find("CafeNameDialog").activeSelf, Is.True); Assert.That(Find("NameError").GetComponent<Text>().text, Is.Not.Empty);
            Find("ContinueShift").GetComponent<Button>().onClick.Invoke(); Assert.That(game.Session, Is.Null);
            Directory.Delete(file + ".writing"); Click("SaveCafeName"); Assert.That(game.CafeName, Is.EqualTo("Ёлки"));
        }
        [Test] public void LongNameFitsAndRichTextRemainsLiteral()
        {
            BuildView(); var label = Find("CafeName").GetComponent<SignboardText>();
            Assert.That(label.supportRichText, Is.False);
            Assert.That(game.RenameCafe(new string('Ш', 32), out _), Is.True); view.Refresh(); Canvas.ForceUpdateCanvases();
            var mesh = new Mesh();
            using (var vertices = new VertexHelper()) {
                typeof(SignboardText).GetMethod("OnPopulateMesh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(VertexHelper) }, null).Invoke(label, new object[] { vertices });
                vertices.FillMesh(mesh);
            }
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
            Assert.That(mesh.bounds.size.x, Is.LessThanOrEqualTo(label.rectTransform.rect.width));
            Assert.That(mesh.bounds.size.y, Is.LessThanOrEqualTo(69)); Object.DestroyImmediate(mesh);
        }
    }
}
