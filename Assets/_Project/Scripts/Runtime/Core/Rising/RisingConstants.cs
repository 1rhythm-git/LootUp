using UnityEngine;

namespace LootUp.Core.Rising
{
    public static class RisingConstants
    {
        public const string Word = "RISING";
        public const int LetterCount = 6;
        public const string UniqueLetters = "RISNG";
        public const int LetterTypeCount = 5;
        public const int FullCollectionMask = (1 << LetterCount) - 1;

        private static readonly Color[] LetterColors =
        {
            new Color(0.98f, 0.24f, 0.28f, 1f),
            new Color(0.18f, 0.82f, 1f, 1f),
            new Color(1f, 0.82f, 0.16f, 1f),
            new Color(0.92f, 0.34f, 0.96f, 1f),
            new Color(0.28f, 0.9f, 0.42f, 1f),
            new Color(1f, 0.48f, 0.14f, 1f)
        };

        public static char GetLetter(int index)
        {
            int safeIndex = Mathf.Clamp(index, 0, LetterCount - 1);
            return Word[safeIndex];
        }

        public static Color GetColor(int index)
        {
            int safeIndex = Mathf.Clamp(index, 0, LetterColors.Length - 1);
            return LetterColors[safeIndex];
        }

        public static char GetLetterByType(int letterTypeIndex)
        {
            int safeIndex = Mathf.Clamp(letterTypeIndex, 0, LetterTypeCount - 1);
            return UniqueLetters[safeIndex];
        }

        public static int GetFirstSlotIndex(int letterTypeIndex)
        {
            char letter = GetLetterByType(letterTypeIndex);
            for (int i = 0; i < LetterCount; i++)
            {
                if (Word[i] == letter)
                {
                    return i;
                }
            }

            return 0;
        }

        public static bool SlotMatchesLetterType(int slotIndex, int letterTypeIndex)
        {
            int safeSlotIndex = Mathf.Clamp(slotIndex, 0, LetterCount - 1);
            return Word[safeSlotIndex] == GetLetterByType(letterTypeIndex);
        }
    }
}
