using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

public class CookieBitsValueInfo : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        var best = JsonConvert.DeserializeObject<BitService.Option>(data.Loaded["bestbit"].Result.ToString());
        
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        preRequest.ToLoad["bestbit"] = Task.Run(async () =>
        {
            var bitsService = DiHandler.GetService<BitService>();
            var conversion = await bitsService.GetOptions();
            var best = conversion.OrderByDescending(c=>c.CoinsPerBit).FirstOrDefault();
            return JsonConvert.SerializeObject(best);
        });
    }
}
