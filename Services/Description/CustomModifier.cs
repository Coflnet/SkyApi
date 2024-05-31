using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services.Description;
public interface CustomModifier
{
    void Apply(DataContainer data);
    void Modify(ModDescriptionService.PreRequestContainer preRequest);
}