using System.Threading.Tasks;

namespace LootUp.Core.Characters
{
    public interface ICharacterAcquisitionService
    {
        bool IsOnline { get; }

        Task<CharacterAcquisitionResult> AcquireAsync(
            CharacterAcquisitionRequest request);
    }
}
