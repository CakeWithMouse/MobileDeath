using System;
using Blinopek.Tests;

int failed = 0;
foreach (var test in CoreScenarios.Cases)
{
    try { test.Value(); Console.WriteLine("PASS " + test.Key); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Key + "\n" + ex); }
}
Console.WriteLine($"{CoreScenarios.Cases.Count - failed}/{CoreScenarios.Cases.Count} passed");
return failed == 0 ? 0 : 1;
