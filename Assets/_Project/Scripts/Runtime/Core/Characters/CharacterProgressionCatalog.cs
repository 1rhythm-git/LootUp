using System;
using System.Collections.Generic;

namespace LootUp.Core.Characters
{
    public static class CharacterProgressionCatalog
    {
        private static readonly CharacterProgressionRecord[] Defaults =
        {
            new CharacterProgressionRecord("default", 1, 0, true, false),
            new CharacterProgressionRecord("alice", 1, 0, true, false),
            new CharacterProgressionRecord("landy", 1, 0, true, false),
            new CharacterProgressionRecord("ninja", 1, 0, true, false)
        };

        public static IReadOnlyList<CharacterProgressionRecord> Records =>
            Defaults;

        public static bool Contains(string characterId)
        {
            string normalizedId = CharacterIdMigration.Normalize(characterId);
            for (int i = 0; i < Defaults.Length; i++)
            {
                if (string.Equals(
                    Defaults[i].CharacterId,
                    normalizedId,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
