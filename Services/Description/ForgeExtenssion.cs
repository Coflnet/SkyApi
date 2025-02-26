using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.Api.Services.Description;

public class ForgeExtenssion : CustomModifier
{
    public void Apply(DataContainer data)
    {
        data.mods[49].Add(new($"{McColorCodes.GREEN}Also checkout"));
        data.mods[49].Add(new($"{McColorCodes.GOLD}/cofl forge"));
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        
    }
}
