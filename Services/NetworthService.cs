using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Netowrth;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services;
public class NetworthService
{
    private readonly ModDescriptionService modDescriptionService;

    public NetworthService(ModDescriptionService modDescriptionService)
    {
        this.modDescriptionService = modDescriptionService;
    }

    public async Task<NetworthBreakDown> GetNetworth(Profile profile)
    {
        var networth = new NetworthBreakDown();
        foreach (var member in profile.members)
        {
            var value = await GetMemberValue(member.Value);
            networth.Member.Add(member.Key, value);
        }
        networth.FullValue = networth.Member.Sum(m => m.Value.FullValue);

        if (profile.banking != null)
            networth.FullValue += (long)profile.banking.balance;

        return networth;
    }

    private async Task<MemberValue> GetMemberValue(Member member)
    {
        MemberValue networth = new();
        List<(string Key, SaveAuction auction)> flatten = GetItemsInInventories(member);
        var prices = await modDescriptionService.GetPrices(flatten.Select(f => f.auction).ToList());
        var combined = flatten.Zip(prices, (f, p) => (f.Key, f.auction, p)).ToList();
        var breakDownDict = networth.ValuePerCategory;
        foreach (var item in combined)
        {
            var price = item.p;
            if (price == null)
                continue;
            var auction = item.auction;
            var key = item.Key;
            var value = price.Median;
            if (breakDownDict.ContainsKey(key))
            {
                breakDownDict[key] += value;
            }
            else
            {
                breakDownDict.Add(key, value);
            }
        }
        // add essences
        foreach (var essence in member.currencies?.essence ?? new())
        {
            if (essence.Value.current == 0)
                continue;
            var price = modDescriptionService.GetItemprice("ESSENCE_" + essence.Key);
            breakDownDict.Add("essence " + essence.Key, price * essence.Value.current);
        }
        foreach (var item in member.inventory?.sacks_counts ?? new())
        {
            // the default is 0
            breakDownDict.TryGetValue("Sacks", out var current);
            var price = modDescriptionService.GetItemprice(item.Key);
            breakDownDict[item.Key] = current + price * item.Value;
        }
        if (member.currencies != null)
            breakDownDict.Add("coin purse", member.currencies.coin_purse);

        networth.FullValue = (long)breakDownDict.Sum(v => v.Value);
        return networth;
    }
    static void AddInventory(Dictionary<string, InventoryElem> inventories, Inventory inventory)
    {
        if (inventory == null)
            return;
        if (inventory.bag_contents != null)
        {
            inventories.Add("quiver", inventory.bag_contents.quiver);
            inventories.Add("fishing bag", inventory.bag_contents.fishing_bag);
            inventories.Add("talismans", inventory.bag_contents.talisman_bag);
            inventories.Add("potion bag", inventory.bag_contents.potion_bag);
        }
        inventories.Add("enderchest", inventory.ender_chest_contents ?? new());
        inventories.Add("armor", inventory.inv_armor ?? new());
        inventories.Add("equipment", inventory.equipment_contents ?? new());
        inventories.Add("personal vault", inventory.personal_vault_contents ?? new());
        inventories.Add("wardrobe", inventory.wardrobe_contents ?? new());
        foreach (var backpack in inventory.backpack_contents ?? new())
        {
            inventories.Add($"backpack {backpack.Key}", backpack.Value);
        }
    }
    public List<(string Key, SaveAuction auction)> GetItemsInInventories(Member member)
    {
        var inventories = new Dictionary<string, InventoryElem>();
        AddInventory(inventories, member.inventory);
        var riftInenvotry = new Dictionary<string, InventoryElem>();
        AddInventory(riftInenvotry, member.rift?.inventory);
        foreach (var item in riftInenvotry)
        {
            inventories.Add($"rift {item.Key}", item.Value);
        }
        var flatten = inventories.Where(i => !string.IsNullOrEmpty(i.Value.data))
            .SelectMany(i => modDescriptionService.GetAuctionsFromNbt(i.Value.data).Select(a => (i.Key, a.auction)))
            .ToList();
        foreach (var pet in member.pets_data.pets)
        {
            var auction = new Core.SaveAuction()
            {
                Tag = $"PET_{pet.type.ToUpper()}",
                Tier = Enum.Parse<Tier>(pet.tier),
                FlatenedNBT = new Dictionary<string, string>()
                {{"exp", pet.exp.ToString()},
                {"candyUsed", pet.candyUsed.ToString()}}
            };
            if (!string.IsNullOrEmpty(pet.skin))
                auction.FlatenedNBT["skin"] = pet.skin;
            if (!string.IsNullOrEmpty(pet.heldItem))
                auction.FlatenedNBT["heldItem"] = pet.heldItem;
            if (!string.IsNullOrEmpty(pet.uuid))
                auction.FlatenedNBT["uuid"] = pet.uuid;

            flatten.Add(("Pets", auction));
        }

        return flatten;
    }
}
