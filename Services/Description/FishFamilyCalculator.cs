using System.Linq;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Core;
using fNbt.Tags;

namespace Coflnet.Sky.Api.Services.Description;

public class FishFamilyCalculator : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        var cheapestLeft = data.PriceEst.Zip(data.auctionRepresent, (price, auction) => (price, auction.auction?.ItemName, auction.desc))
            .Where(x =>x.ItemName != null && x.desc != null && x.desc.Length == 0 && x.price?.Median > 0)
            .OrderBy(x =>( x.price?.Median ?? 1_000_000) + (x.price?.Lbin.Price ?? 10_000_000))
            .Take(3)
            .ToList();
        var info = new List<Models.Mod.DescModification>();
        info.Add(new("Cheapest fish to add:"));
        if(cheapestLeft.Count == 0)
        {
            info.Add(new("No fish found"));
            info.Add(new($"{McColorCodes.GRAY}No auction active"));
            data.mods.Add(info);
            return;
        }
        info.Add(new($"{McColorCodes.GRAY}1. " + cheapestLeft[0].ItemName));
        if (cheapestLeft.Count > 1)
        {
            info.Add(new($"{McColorCodes.GRAY}2. " + cheapestLeft[1].ItemName));
        }
        if (cheapestLeft.Count > 2)
        {
            info.Add(new($"{McColorCodes.GRAY}3. " + cheapestLeft[2].ItemName));
        }
        data.mods.Add(info);
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {

        var nbt = NBT.File(Convert.FromBase64String(preRequest.inventory.FullInventoryNbt));
        var auctionRepresent = nbt.RootTag.Get<NbtList>("i").Select(t =>
        {
            var compound = t as NbtCompound;
            var name = NBT.GetName(compound);
            return (new SaveAuction()
            {
                ItemName = name,
                Tag = name.Replace("§c","").ToUpper().Replace(" ", "_"),
                Tier = Tier.SPECIAL,
                Count = 1
            },new string[0]);
        }).Take(5 * 9).ToList();


        for (int i = 0; i < 5 * 9; i++)
        {
            preRequest.auctionRepresent[i] = auctionRepresent[i];
        }
        
        foreach (var auction in preRequest.auctionRepresent.Take(5 * 9))
        {
            var item = auction.auction;
            Console.WriteLine($"Fish Family Calculator: {item?.ItemName} -> {item?.Tag}");
        }
    }
}