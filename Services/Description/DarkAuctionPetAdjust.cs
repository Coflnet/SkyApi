namespace Coflnet.Sky.Api.Services.Description;

public class DarkAuctionPetAdjust : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        return;
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        if (!preRequest.inventory.ChestName.Contains("- Round"))
            return;
        if (!preRequest.auctionRepresent[4].auction.FlatenedNBT.TryGetValue("exp", out var prevExp))
            return;
        preRequest.auctionRepresent[4].auction.FlatenedNBT["exp"] = "0";
        Console.WriteLine($"Dark auction pet exp reset from {prevExp} to 0 on {preRequest.inventory.ChestName} {preRequest.auctionRepresent[4].auction.ItemName}");
    }
}