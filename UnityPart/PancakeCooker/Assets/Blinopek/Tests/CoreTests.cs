using System.Collections.Generic;
using NUnit.Framework;

namespace Blinopek.Tests
{
    public sealed class CoreTests
    {
        public static IEnumerable<string> Names => CoreScenarios.Cases.Keys;
        [TestCaseSource(nameof(Names))]
        public void GameplayContract(string name) { CoreScenarios.Cases[name](); }
    }
}
