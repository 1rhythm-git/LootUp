using System.Threading.Tasks;

namespace LootUp.Core.Characters
{
    public interface ICharacterProgressionSyncService
    {
        bool IsOnline { get; }
        Task<CharacterProgressionSynchronizationResult> SynchronizeAsync(
            CharacterProgressionServerSnapshot localSnapshot);
        Task<CharacterProgressionOperationResult> ApplyExperienceAsync(
            CharacterExperienceRequest request);
        Task<CharacterProgressionMutationResult> SetOwnershipAsync(
            string characterId,
            bool isOwned);
        Task<CharacterProgressionMutationResult> SetLoadoutAsync(
            string selectedCharacterId,
            string equippedCharacterId);
    }
}
