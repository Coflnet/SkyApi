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
            Highlight(data.mods[flip.index]);
        }
    }
}
