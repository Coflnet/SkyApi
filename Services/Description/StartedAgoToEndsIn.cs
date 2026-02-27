using System.Linq;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.Api.Services.Description;

public class StartedAgoToEndsIn : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        if (data.itemListings.Count == 0)
            return;
        if (data.inventory.Settings.DisableAuctionStartedTime)
            return; // user doesn't want this feature
        for (int i = 0; i < data.auctionRepresent.Count; i++)
        {
            var item = data.auctionRepresent[i];
            var desc = item.desc;
            var auction = item.auction;
            // get line index with .StartsWith("§7Ends in") 
            if (auction?.FlatenedNBT == null || !auction.FlatenedNBT.TryGetValue("uid", out var uid))
                continue;
            var lastStart = data.itemListings[uid].OrderByDescending(a => a.start).FirstOrDefault();
            var startTime = lastStart?.start;
            var position = desc.Length - 1;
            position += auction.Enchantments.Where(e => e.Type == Core.Enchantment.EnchantmentType.efficiency
                                                || e.Type == Core.Enchantment.EnchantmentType.respiration
                                                || e.Type == Core.Enchantment.EnchantmentType.aqua_affinity
                                                || e.Type == Core.Enchantment.EnchantmentType.depth_strider).Count();
            if (auction.FlatenedNBT.ContainsKey("color"))
                position++;
            if (desc.Last() == "§eClick to inspect!")
                position -= 2;

            var line = desc.Select((l, i) => (l, i)).FirstOrDefault(l => l.l.StartsWith("§7Ends in"));
            if (line.i == 0)
            {
                continue;
            }
            if (startTime == null || startTime < DateTime.UtcNow - TimeSpan.FromDays(14))
            {
                Console.WriteLine($"Found new auction {auction.ItemName} - {uid}" + string.Join("\n", desc));
                data.mods[i].Insert(0, new DescModification($"{McColorCodes.DARK_GRAY}Started {McColorCodes.GRAY}less than a minute {McColorCodes.DARK_GRAY}ago")
                {
                    Line = position,
                    Type = DescModification.ModType.REPLACE
                });
                continue;
            }
            var timeSinceStart = DateTime.UtcNow - startTime;
            var formatted = ModDescriptionService.FormatTime(timeSinceStart.Value);
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
