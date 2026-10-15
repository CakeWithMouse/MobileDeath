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
        public List<GameCommand> commands = new List<GameCommand>();
        internal SavedGame WithCafeName(string value)
        {
            var copy = (SavedGame)MemberwiseClone();
            copy.cafeName = value;
            return copy;
        }
    }
}
