using System.Linq;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.Api.Services.Description;

public class StartedAgoToEndsIn : CustomModifier
{
    public void Apply(DataContainer data)
    {
        for (int i = 0; i < data.auctionRepresent.Count; i++)
        {
            var item = data.auctionRepresent[i];
            var desc = item.desc;
            var auction = item.auction;
            // get line index with .StartsWith("§7Ends in") 
            if (auction?.FlatenedNBT == null || !auction.FlatenedNBT.TryGetValue("uid", out var uid))
                continue;
            var startTime = data.itemListings[uid].OrderByDescending(a => a.start).FirstOrDefault()?.start;
            var timeSinceStart = DateTime.UtcNow - startTime;
            
            var line = desc.Select((l, i) => (l, i)).FirstOrDefault(l => l.l.StartsWith("§7Ends in"));
            if (line.i == 0)
            {
                continue;
            }
            if(timeSinceStart == null)
            {
                Console.WriteLine($"Found new auction " + string.Join("\n", desc));
                continue;
            }
            var formatted = ModDescriptionService.FormatTime(timeSinceStart.Value);
            var position = desc.Length - 3;
            if (auction.Enchantments.Any(e => e.Type == Core.Enchantment.EnchantmentType.efficiency))
                position++;
            data.mods[i].Insert(0, new DescModification($"{McColorCodes.DARK_GRAY}Started {McColorCodes.GRAY}{formatted} {McColorCodes.DARK_GRAY}ago")
            {
                Line = position,
                Type = DescModification.ModType.REPLACE
            });
        }
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        return;
    }
}
