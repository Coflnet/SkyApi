using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>Represents a player page flip highlight.</summary>
public class PlayerPageFlipHighlight : FlipOnNextPage
{
    /// <inheritdoc/>
    public override void Apply(DataContainer data)
    {
        foreach (var flip in GetFlipAble(data))
        {
            if (flip.profit <= 0)
                continue;
            var item = data.mods[flip.index];
            item.Add(new DescModification($"Med profit: {McColorCodes.GOLD}{data.modService.FormatNumber(flip.profit)}"));
            Highlight(item);
        }
    }
}
