using System;
using System.Collections.Generic;
using Blinopek.Domain;

namespace Blinopek.Application
{
    // Journal replay never dispatches live presentation events or performs external settlement.
    internal static class SessionJournal
    {
        public static DaySession Replay(SavedGame saved)
        {
            var session = new DaySession(saved.sessionStart, saved.rules, saved.seed, false, false);
            foreach (var command in saved.commands)
            {
                if (!session.Execute(command))
                    throw new InvalidOperationException("Invalid command in saved session: " + command.kind);
            }

            return session;
        }

        public static void Append(List<GameCommand> commands, GameCommand command)
        {
            var last = commands.Count == 0 ? null : commands[commands.Count - 1];
            if (command.kind == ActionKind.Tick && last != null && last.kind == ActionKind.Tick && last.a + command.a <= GameCommand.MaxTickCount)
            {
                last.a += command.a;
                return;
            }

            // A caller may reuse or edit its command after dispatch.
            commands.Add(command.Copy());
        }
    }
}
