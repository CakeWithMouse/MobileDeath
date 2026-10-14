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
