namespace Coflnet.Sky.Api.Models;
/// <summary>
/// Represents help text for some command
/// </summary>
public class CommandListEntry
{
    /// <summary>
    /// The commands label. ie /cofl {this}
    /// </summary>
    public string SubCommand;
    /// <summary>
    /// The descriptive help text to display
    /// </summary>
    public string Description;

    /// <summary>
    /// Constructs a new instance of <see cref="CommandListEntry"/>
    /// </summary>
    /// <param name="subCommand"></param>
    /// <param name="description"></param>
    public CommandListEntry(string subCommand, string description)
    {
        SubCommand = Sky.Commands.MC.McColorCodes.AQUA + subCommand;
        Description = Sky.Commands.MC.McColorCodes.GRAY + description;
    }
}
