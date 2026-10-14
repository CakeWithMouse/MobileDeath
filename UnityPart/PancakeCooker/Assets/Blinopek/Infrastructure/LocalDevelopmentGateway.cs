using Blinopek.Application;
using Blinopek.Domain;

namespace Blinopek.Infrastructure
{
    // Explicit development-only adapter. No auth, server balances, advertising or IAP side effects.
    public sealed class LocalDevelopmentGateway : IDayGateway
    {
        public string Environment => "local-development";
        public Profile Settle(DaySession session, Profile current)
        {
            var result = current.Copy();
            result.demonstrationSeen |= session.DemonstrationSeen;
            result.tutorialComplete |= session.TutorialCompleted;
            if (!session.Success) return result;
            result.coins += session.Ledger.Profit;
            result.scrapers = session.Scrapers;
            result.day = session.StartProfile.day + 1;
            foreach (var review in session.Reviews) { result.ratingTotal += review.Total; result.ratingCount++; }
            return result;
        }
    }
}
