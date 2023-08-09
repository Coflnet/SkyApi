using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Commands.MC;
using System.Linq;

namespace SkyApi.Services.Description
{
    public class BitsCoinValue : CustomModifier
    {
        public void Apply(DataContainer data)
        {
            for (int i = 0; i < data.auctionRepresent.Count; i++)
            {
                var desc = data.auctionRepresent[i].desc;
                var price = data.res?[i];
                if (desc == null || price == null)
                {
                    continue;
                }
                if (desc.Count() == 0)
                {
                    continue;
                }
                if (price != null && price.Median != 0 && hasBitsValue(desc, out int bits))
                {
                    var prefix = price.ItemKey == price.MedianKey ? "" : "~";
                    string text = $"{McColorCodes.GRAY}Coins per bit: {McColorCodes.AQUA}{prefix}{data.modService.FormatNumber(price.Median/bits)} ";

                    data.mods[i].Insert(0, new DescModification(DescModification.ModType.APPEND, 1, text));
                }
            }
        }
        private bool hasBitsValue(IEnumerable<string> description, out int bits)
        {
            bits = 1;
            foreach (string descLine in description)
            {
                if (!descLine.EndsWith("Bits"))
                {
                    continue;
                }
                string commaSanitizedMatch = descLine.Substring(2, descLine.Length - 7).Replace(",", "");
                if (int.TryParse(commaSanitizedMatch, out bits))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
