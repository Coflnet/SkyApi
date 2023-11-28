namespace Coflnet.Sky.Api.Services.Description;

public class PlayerPageFlipHighlight : FlipOnNextPage
{
    public virtual void Apply(DataContainer data)
    {
        var flips = GetFlipAble(data);
        foreach (var flip in flips)
        {
            Highlight(data.mods[flip.index]);
        }
    }
}