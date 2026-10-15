using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;
using NUnit.Framework;
using UnityEngine;

namespace Blinopek.Tests
{
    public sealed class PersistenceTests
    {
        [Serializable] private sealed class Envelope { public string checksum, payload; }
        private string directory, file;
        [SetUp] public void Setup()
        {
            directory = Path.GetFullPath(Path.Combine("Library", "BlinopekTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory); file = Path.Combine(directory, "save.json");
        }
        [TearDown] public void Cleanup()
        {
            string root = Path.GetFullPath(Path.Combine("Library", "BlinopekTests")) + Path.DirectorySeparatorChar;
            if (directory.StartsWith(root, StringComparison.OrdinalIgnoreCase)) Directory.Delete(directory, true);
        }
        [Test] public void DayRestartIsAtomicAndSurvivesFileReload()
        {
            var rules = new BalanceRules(); var store = new FileGameStore(file);
            var game = new GameController(store, new LocalDevelopmentGateway(), rules); game.StartDay(34);
            game.Dispatch(new GameCommand(ActionKind.Accept));
            game.Dispatch(new GameCommand(ActionKind.BeginStroke, -1, 500, 500));
            game.SetPause(PauseReason.Menu, true);
            var original = game.Session; string before = File.ReadAllText(file);
            Directory.CreateDirectory(file + ".writing");
            Assert.That(game.RestartDay(), Is.False);
            Assert.That(game.Session, Is.SameAs(original)); Assert.That(game.Pauses, Is.EqualTo(PauseReason.Menu));
            Assert.That(File.ReadAllText(file), Is.EqualTo(before)); Assert.That(game.StorageError, Is.Not.Null);
            Directory.Delete(file + ".writing");
            Assert.That(game.RestartDay(), Is.True); Assert.That(game.StorageError, Is.Null);
            var restored = new GameController(new FileGameStore(file), new LocalDevelopmentGateway(), rules);
            Assert.That(restored.Session.ElapsedTicks, Is.Zero); Assert.That(restored.Session.Pancake, Is.Null);
            Assert.That(restored.Session.Order.Phase, Is.EqualTo(OrderPhase.Offered));
            Assert.That(restored.Session.FreeCooking && restored.Session.ManualServing, Is.True);
            Assert.That(JsonUtility.ToJson(restored.Profile), Is.EqualTo(JsonUtility.ToJson(original.StartProfile)));
        }

        [Test] public void JsonRoundTripKeepsTableUpgradeAndLegacyProfilesStartLight()
        {
            Assert.That(JsonUtility.FromJson<Profile>("{\"day\":3,\"coins\":3000}").darkTablesUpgrade, Is.False);
            var store = new FileGameStore(file);
            store.Save(new SavedGame { profile = new Profile { darkTablesUpgrade = true }, rules = new BalanceRules(), awaitingDay = true });
            var restored = new GameController(new FileGameStore(file), new LocalDevelopmentGateway(), new BalanceRules());
            Assert.That(restored.StorageBlocked, Is.False);
            Assert.That(restored.Profile.darkTablesUpgrade, Is.True);
        }
        [Test] public void JsonRoundTripReplaysOpenStrokeAndEndsPointerCapture()
        {
            var store = new FileGameStore(file); var game = new GameController(store, new LocalDevelopmentGateway(), new BalanceRules());
            game.StartDay(34); game.Dispatch(new GameCommand(ActionKind.Accept));
            game.Dispatch(new GameCommand(ActionKind.BeginStroke, -1, 500, 500)); game.Dispatch(new GameCommand(ActionKind.Stroke, 700, 500)); game.Save();
            int pixels = game.Session.Pancake.Batter.Count;
            var restored = new GameController(new FileGameStore(file), new LocalDevelopmentGateway(), new BalanceRules());
            Assert.That(restored.Session.Pancake.Batter.Count, Is.EqualTo(pixels));
            Assert.That(restored.Dispatch(new GameCommand(ActionKind.BeginStroke, -1, 500, 500)), Is.True, "Restored gesture must not lock the new pointer");
            Assert.That(restored.Session.Ledger.Revenue, Is.Zero, "Tutorial must not pay on each restore");
        }
        [Test] public void JsonRoundTripKeepsPanAndPlateSeparate()
        {
            var rules = new BalanceRules { readyTicks = 2, burnTicks = 6 };
            var store = new FileGameStore(file);
            store.Save(new SavedGame { profile = new Profile { tutorialComplete = true, demonstrationSeen = true }, rules = rules });
            var game = new GameController(store, new LocalDevelopmentGateway(), rules); game.StartDay(34);
            Action<GameCommand> send = c => Assert.That(game.Dispatch(c), Is.True, c.kind.ToString());
            CoreScenarios.Paint(send, -1); send(new GameCommand(ActionKind.FinishBatter)); send(new GameCommand(ActionKind.Tick, 2));
            send(new GameCommand(ActionKind.Flip)); send(new GameCommand(ActionKind.Tick, 2)); send(new GameCommand(ActionKind.MoveToPlate));
            send(new GameCommand(ActionKind.BeginFold)); send(new GameCommand(ActionKind.FoldGesture, 0, 400));
            CoreScenarios.Paint(send, -1); send(new GameCommand(ActionKind.FinishBatter)); send(new GameCommand(ActionKind.Tick, 3)); game.Save();
            var restored = new GameController(new FileGameStore(file), new LocalDevelopmentGateway(), rules);
            Assert.That(restored.Session.PanPancake.HeatA, Is.EqualTo(3));
            Assert.That(restored.Session.PlatePancake.Stage, Is.EqualTo(PancakeStage.Folding));
            Assert.That(restored.Session.PlatePancake.Folds.Count, Is.EqualTo(1));
            Assert.That(restored.Session.PlatePancake.HeatA, Is.EqualTo(2));
            Assert.That(restored.Session.PanPancake.Id, Is.Not.EqualTo(restored.Session.PlatePancake.Id));
            Assert.That(restored.Session.Balance, Is.EqualTo(game.Session.Balance));
            Assert.That(restored.Dispatch(new GameCommand(ActionKind.FoldGesture, 400, 0)), Is.True);
            Assert.That(restored.Session.PanPancake.HeatA, Is.EqualTo(3));
            Assert.That(restored.Session.ReadyPancakes.Count, Is.EqualTo(1));
        }
        [Test] public void DecorationIntervalSurvivesJsonAndRebalancing()
        {
            var rules = new BalanceRules { guestIntervalTicks = 40, guestArrivalBaseTicks = 80, guestArrivalMinTicks = 40, guestArrivalReductionPerDecorationPointTicks = 2, darkTablesDecorationPoints = 10, patiencePerPancakeTicks = 4 };
            var store = new FileGameStore(file);
            store.Save(new SavedGame { profile = new Profile { tutorialComplete = true, darkTablesUpgrade = true }, rules = rules, awaitingDay = true });
            var game = new GameController(store, new LocalDevelopmentGateway(), rules); Assert.That(game.StartDay(7), Is.True);
            game.Dispatch(new GameCommand(ActionKind.Accept)); game.Dispatch(new GameCommand(ActionKind.Tick, 4));
            game.Dispatch(new GameCommand(ActionKind.Tick, 45)); game.Save();
            rules.guestArrivalBaseTicks = 200; rules.darkTablesDecorationPoints = 30;
            var restored = new GameController(new FileGameStore(file), new LocalDevelopmentGateway(), rules);
            Assert.That(restored.Session.DecorationPoints, Is.EqualTo(10));
            Assert.That(restored.Session.GuestArrivalIntervalTicks, Is.EqualTo(60));
            Assert.That(restored.Session.GuestHasLeft, Is.True);
            restored.Dispatch(new GameCommand(ActionKind.Tick, 14));
            Assert.That(restored.Session.Order.Id, Is.EqualTo(1));
            restored.Dispatch(new GameCommand(ActionKind.Tick, 1));
            Assert.That(restored.Session.Order.Id, Is.EqualTo(2));
            var again = new GameController(new FileGameStore(file), new LocalDevelopmentGateway(), rules);
            Assert.That(again.Session.Order.Id, Is.EqualTo(2));
            Assert.That(again.Session.Balance, Is.EqualTo(restored.Session.Balance));
        }

        [Test] public void JsonWithoutDecorationBalanceKeepsLegacyArrivalTiming()
        {
            var rules = new BalanceRules { guestIntervalTicks = 20, patiencePerPancakeTicks = 4 };
            var store = new FileGameStore(file);
            store.Save(new SavedGame { profile = new Profile { tutorialComplete = true, darkTablesUpgrade = true }, rules = rules, awaitingDay = true });
            var game = new GameController(store, new LocalDevelopmentGateway(), rules); Assert.That(game.StartDay(7), Is.True);
            game.Dispatch(new GameCommand(ActionKind.Accept)); game.Dispatch(new GameCommand(ActionKind.Tick, 24)); game.Save();
            var envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(file));
            envelope.payload = envelope.payload.Replace("\"guestArrivalBaseTicks\":0,", "").Replace("\"guestArrivalMinTicks\":0,", "")
                .Replace("\"guestArrivalReductionPerDecorationPointTicks\":0,", "").Replace("\"darkTablesDecorationPoints\":10,", "");
            using (var hash = SHA256.Create()) envelope.checksum = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(envelope.payload)));
            File.WriteAllText(file, JsonUtility.ToJson(envelope));
            var nextRules = new BalanceRules { guestArrivalBaseTicks = 80, guestArrivalMinTicks = 40, guestArrivalReductionPerDecorationPointTicks = 2 };
            var restored = new GameController(new FileGameStore(file), new LocalDevelopmentGateway(), nextRules);
            Assert.That(restored.Session.GuestArrivalIntervalTicks, Is.EqualTo(20));
            Assert.That(restored.Session.Order.Id, Is.EqualTo(2));
            Assert.That(restored.Session.Balance, Is.EqualTo(game.Session.Balance));
        }

        [Test] public void CorruptedPrimaryRecoversBackupAndRetainsCorruptEvidence()
        {
            var store = new FileGameStore(file); var saved = new SavedGame { profile = new Profile(), rules = new BalanceRules() };
            store.Save(saved); saved.profile.coins = 7000; store.Save(saved); File.WriteAllText(file, "broken payload");
            var recovered = store.Load(); Assert.That(recovered.profile.coins, Is.EqualTo(3000)); Assert.That(store.RecoveryNotice, Is.Not.Null);
            store.Save(recovered); Assert.That(store.Load().profile.coins, Is.EqualTo(3000));
            Assert.That(Directory.GetFiles(directory, "*.corrupt-*").Length, Is.EqualTo(1));
            Assert.That(File.Exists(file + ".bak"), Is.True);
        }
        [Test] public void TwoBrokenCopiesBlockInsteadOfCreatingNewWallet()
        {
            File.WriteAllText(file, "broken"); File.WriteAllText(file + ".bak", "also broken");
            var game = new GameController(new FileGameStore(file), new LocalDevelopmentGateway(), new BalanceRules());
            Assert.That(game.StorageBlocked, Is.True); Assert.That(game.StartDay(10), Is.False); game.Save();
            Assert.That(File.ReadAllText(file), Is.EqualTo("broken"));
        }
        [Test] public void FutureSaveVersionDoesNotFallBackAndOverwriteNewerProgress()
        {
            var store = new FileGameStore(file); var saved = new SavedGame { profile = new Profile(), rules = new BalanceRules() };
            store.Save(saved); store.Save(saved); saved.schema = 99;
            string payload = JsonUtility.ToJson(saved), checksum;
            using (var hash = SHA256.Create()) checksum = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            string original = JsonUtility.ToJson(new Envelope { payload = payload, checksum = checksum }); File.WriteAllText(file, original);
            var game = new GameController(store, new LocalDevelopmentGateway(), new BalanceRules());
            Assert.That(game.StorageBlocked, Is.True); Assert.That(game.StartDay(1), Is.False); game.Save();
            Assert.That(File.ReadAllText(file), Is.EqualTo(original));
        }
    }
}
