using LootUp.Core.Characters;
using UnityEngine;

namespace LootUp.Core.Items
{
    public sealed class AddTimeItemEffect : IItemEffect
    {
        public bool CanExecute(ItemDefinition definition)
        {
            return definition != null && (definition.ItemType == ItemType.Time || definition.EffectKey == ItemEffectKeys.AddTime);
        }

        public ItemEffectResult Execute(ItemDefinition definition, ItemEffectContext context)
        {
            if (definition == null || context.TopHUDController == null)
            {
                return ItemEffectResult.None;
            }

            int passCount = Mathf.Max(1, context.RequiredPassCount);
            int characterLevel = context.PlayerCharacterRuntime != null
                ? context.PlayerCharacterRuntime.Progression.Level
                : 1;
            float timeAmount = CharacterLevelGrowthPolicy.ApplySupportItemEffect(
                Mathf.Max(0, definition.EffectValue * passCount),
                characterLevel);
            if (ArtifactEffectResolver.RollPercent(ArtifactEffectResolver.Resolve().TimeItemDoubleChancePercent))
            {
                timeAmount *= 2;
            }

            context.TopHUDController.AddTime(timeAmount);
            int displayedAmount = Mathf.RoundToInt(timeAmount);
            context.TopHUDController.SetItemStatus($"+{displayedAmount}s TIME");
            return new ItemEffectResult(ItemEffectOutcome.TimeAdded, displayedAmount);
        }
    }
}
