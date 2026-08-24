using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>Represents an auction note text.</summary>
public class AuctionNoteText : ICustomModifier
{
    /// <inheritdoc/>
    public void Apply(DataContainer data)
    {
        data.mods.Add([new DescModification($"{McColorCodes.GRAY}Recommended Auction price from SkyCofl"),
        new("Keep it at minimum to save listing tax"),
        new($"{McColorCodes.GRAY}switch to BIN creation to get other estimate")] );
    }

    /// <inheritdoc/>
    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        // No pre-request modifications needed for notes
    }
}
