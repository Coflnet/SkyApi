namespace Coflnet.Sky.Api.Services.Description;

/// <summary>Represents a player page flip highlight.</summary>
public class PlayerPageFlipHighlight : FlipOnNextPage
{
    /// <inheritdoc/>
    public override void Apply(DataContainer data)
    {
        var flips = GetFlipAble(data);
        foreach (var flip in flips)
        {
            Highlight(data.mods[flip.index]);
        }
    }
}
