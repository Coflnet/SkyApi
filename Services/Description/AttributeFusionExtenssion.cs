using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.Api.Services.Description;

public class AttributeFusionExtenssion : CustomModifier
{
    public void Apply(DataContainer data)
    {
        data.mods[49].Add(new($"{McColorCodes.GREEN}Also checkout"));
        data.mods[49].Add(new($"{McColorCodes.GOLD}/cofl attributeupgrade"));
        data.mods[49].Add(new($"{McColorCodes.GOLD}/cofl cheapattrib"));
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        
    }
}