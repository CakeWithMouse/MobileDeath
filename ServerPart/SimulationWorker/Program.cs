using System.Text.Json;
using Blinopek.Domain;

var json = new JsonSerializerOptions { IncludeFields = true };
try
{
    var input = Console.In.ReadToEnd();
    if (input.Length > 8 * 1024 * 1024) throw new ArgumentException("JOURNAL_TOO_LARGE");
    var request = JsonSerializer.Deserialize<ReplayRequest>(input, json) ?? throw new ArgumentException("INVALID_REQUEST");
    request.rules.Validate();
    if (request.commands.Length > 50000) throw new ArgumentException("JOURNAL_TOO_LARGE");
    var session = new DaySession(request.profile, request.rules, request.seed, false, false);
    long ticks = 0;
    foreach (var command in request.commands)
    {
        if (command.kind == ActionKind.Tick) ticks += command.a;
        if (ticks > 200000 || !session.Execute(command)) throw new ArgumentException("INVALID_COMMAND");
    }
    if (session.Phase != DayPhase.Result) throw new ArgumentException("DAY_NOT_FINISHED");
    var profile = request.profile.Copy();
    profile.demonstrationSeen |= session.DemonstrationSeen;
    profile.tutorialComplete |= session.TutorialCompleted;
    if (session.Success)
    {
        profile.coins = checked(profile.coins + session.Ledger.Profit);
        profile.scrapers = session.Scrapers;
        profile.day = checked(profile.day + 1);
        foreach (var review in session.Reviews) { profile.ratingTotal = checked(profile.ratingTotal + review.Total); profile.ratingCount++; }
    }
    if (profile.coins < 0 || profile.honey < 0 || profile.scrapers < 0) throw new ArgumentException("INVALID_BALANCE");
    Console.Write(JsonSerializer.Serialize(new { ok = true, profile, success = session.Success, ticks }, json));
}
catch (Exception ex) when (ex is ArgumentException || ex is JsonException || ex is OverflowException)
{
    Console.Write(JsonSerializer.Serialize(new { ok = false, code = "INVALID_JOURNAL" }));
}
sealed class ReplayRequest
{
    public Profile profile = new();
    public BalanceRules rules = new();
    public int seed;
    public GameCommand[] commands = Array.Empty<GameCommand>();
}
