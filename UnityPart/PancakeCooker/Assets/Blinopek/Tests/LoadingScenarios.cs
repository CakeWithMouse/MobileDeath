using System;
using System.Collections.Generic;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;

namespace Blinopek.Tests
{
    public static partial class CoreScenarios
    {
        private static SavedGame LoadingFixture(bool invalid = false)
        {
            var data = new SavedGame { profile = Profile(), sessionStart = Profile(), rules = Rules(), seed = 17 };
            data.commands.Add(new GameCommand(ActionKind.EnableFreeCooking));
            data.commands.Add(new GameCommand(ActionKind.EnableManualServing));
            for (int i = 0; i < 96; i++) data.commands.Add(new GameCommand(ActionKind.Tick, 1));
            if (invalid) data.commands.Add(new GameCommand(ActionKind.Tick, -1));
            return data;
        }
        private static void DeferredLoadingReplay()
        {
            var expected = new GameController(new MemoryStore { Data = LoadingFixture() }, new LocalDevelopmentGateway(), Rules());
            var store = new MemoryStore { Data = LoadingFixture() };
            var game = new GameController(store, new LocalDevelopmentGateway(), Rules(), true);
            Check(game.Session == null && store.Writes == 0, "Deferred construction published or wrote the session");
            int steps = 0; float previous = 0;
            foreach (float progress in game.RestoreSteps())
            {
                Check(progress >= previous && progress <= 1, "Restore progress moved backwards");
                previous = progress; steps++;
            }
            Check(steps >= 4 && previous == 1, "Replay did not yield bounded batches");
            Check(game.Session.ElapsedTicks == expected.Session.ElapsedTicks && game.Profile.coins == expected.Profile.coins &&
                game.Session.FreeCooking == expected.Session.FreeCooking && game.Session.ManualServing == expected.Session.ManualServing,
                "Deferred replay differs from synchronous restoration");
        }
        private static void DeferredLoadingFailure()
        {
            var store = new MemoryStore { Data = LoadingFixture(true) };
            var game = new GameController(store, new LocalDevelopmentGateway(), Rules(), true);
            bool failed = false;
            try { foreach (float progress in game.RestoreSteps()) { } }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && game.Session == null && store.Writes == 0, "Invalid replay published partial state or overwrote the save");
        }
        private static void DeferredLoadingBlockedStorage()
        {
            var game = new GameController(new BrokenStore(), new LocalDevelopmentGateway(), Rules(), true);
            foreach (float progress in game.RestoreSteps()) { }
            Check(game.StorageBlocked && game.Session == null && !game.StartDay(1), "Loading bypassed corrupt-save protection");
        }
    }
}
