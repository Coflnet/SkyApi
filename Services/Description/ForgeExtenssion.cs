using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>Extends forge item descriptions.</summary>
public class ForgeExtenssion : ICustomModifier
{
    /// <inheritdoc/>
    public void Apply(DataContainer data)
    {
        data.mods[49].Add(new($"{McColorCodes.GREEN}Also checkout"));
        data.mods[49].Add(new($"{McColorCodes.GOLD}/cofl forge"));
    }

    /// <inheritdoc/>
    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        
    }
}
