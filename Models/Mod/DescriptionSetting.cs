

using System.Collections.Generic;

namespace Coflnet.Sky.Api.Models.Mod;

/// <summary>
/// Custom settings of what modifications to include in the response
/// </summary>
public class DescriptionSetting
{
    /// <summary>
    /// Lines and which elements to put into these lines
    /// </summary>
    public List<List<DescriptionField>> Fields { get; set; }
}
