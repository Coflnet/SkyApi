namespace Coflnet.Sky.Api.Models;

/// <summary>The country of the caller</summary>
public class CountryResponse
{
    /// <summary>Two letter upper case ISO country, null when unknown</summary>
    public string Country { get; set; }
}
