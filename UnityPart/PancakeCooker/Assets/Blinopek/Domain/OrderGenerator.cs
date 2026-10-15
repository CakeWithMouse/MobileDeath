using System;
using System.Collections.Generic;

namespace Blinopek.Domain
{
    // Keep the seeded random sequence stable: saved sessions regenerate their orders on replay.
    internal sealed class OrderGenerator
    {
        private readonly Profile profile;
        private readonly BalanceRules rules;
        private uint random;
        private int guestNumber;
        public OrderGenerator(Profile profile, BalanceRules rules, int seed)
        {
            this.profile = profile;
            this.rules = rules;
            random = (uint)seed;
            if (random == 0)
                random = 1;
        }

        private int Random(int max)
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return (int)(random % (uint)max);
        }

        public OrderState Next(bool tutorialCompleted)
        {
            guestNumber++;
            var portions = new List<PortionSpec>();
            bool lesson = !tutorialCompleted && guestNumber == 1;
            int count = lesson || profile.day < rules.multiPancakeDay ? 1 : 1 + Random(rules.maxPancakes);
            var owned = new List<int>();
            for (int i = 0; i < 5; i++)
                if (profile.Owns((Ingredient)i))
                    owned.Add(i);
            if (owned.Count == 0)
                throw new InvalidOperationException("A profile must own at least one ingredient.");
            int price = 0;
            for (int i = 0; i < count; i++)
            {
                int bits = lesson ? 1 : 1 << owned[Random(owned.Count)];
                if (!lesson && profile.day >= 3 && owned.Count > 1 && Random(3) == 0)
                    bits |= 1 << owned[Random(owned.Count)];
                Fold shape = lesson || profile.day == 1 ? Fold.Triangle : (Fold)Random(3);
                portions.Add(new PortionSpec(bits, shape));
                price += rules.basePrice;
                for (int j = 0; j < 5; j++)
                    if ((bits & (1 << j)) != 0)
                        price += rules.ingredientPrice;
            }

            int patience = rules.patiencePerPancakeTicks * count;
            if (profile.patienceUpgrade)
                patience = patience * 3 / 2;
            return new OrderState(guestNumber, (guestNumber - 1) % 6, portions, price, patience);
        }
    }
}
