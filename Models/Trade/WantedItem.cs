using System.ComponentModel;

namespace Coflnet.Sky.Api.Models;

public class WantedItem
{
    public string Tag { get; set; }
    public string ItemName { get; set; }
    public Dictionary<string, string> Filters { get; set; }
}