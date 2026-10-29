using Blinopek.Domain;

namespace Blinopek.Application
{
    // The player's first shift: the usual simulation with forgiving rules, so it ends in success.
    // The first guest of this shift is the "Первый блин комом" lesson built into DaySession.
    public static class NewbieSession
    {
        public static bool IsRequiredFor(Profile profile) => profile.matchesPlayed == 0;

        // Copies the normal rules: the shared balance asset must never be changed.
        public static BalanceRules CreateRules(BalanceRules normal)
        {
            var rules = normal.Copy();
            rules.version = normal.version + "-newbie";
            rules.maxPancakes = 1; // One pancake per guest.
            rules.patiencePerPancakeTicks = normal.patiencePerPancakeTicks * 2; // Guests wait twice as long.
            rules.burnTicks = normal.burnTicks * 2; // A wider window to flip in time.
            rules.refundThreshold = 0; // Mistakes lower tips but never cost a refund.
            rules.rent = 0; // The shift cannot end with a loss.
            return rules;
        }
    }
}
