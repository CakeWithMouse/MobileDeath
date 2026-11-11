using System;
using System.Collections.Generic;
using Blinopek.Domain;

namespace Blinopek.Application
{
    [Serializable]
    public sealed class SavedGame
    {
        public int schema = 1;
        public string environment = "local-development";
        public Profile profile;
        public Profile sessionStart;
        public BalanceRules rules;
        public int seed;
        public bool settled;
        public bool awaitingDay;
        public string cafeName;
        public string ownerId, attemptId, pendingKind;
        public int revision, ackSeq, batchCount, pendingBatchSize;
        public NetworkOperation pendingOperation;
        public List<GameCommand> commands = new List<GameCommand>();
        internal SavedGame WithCafeName(string value)
        {
            var copy = (SavedGame)MemberwiseClone();
            copy.cafeName = value;
            return copy;
        }
    }
}
