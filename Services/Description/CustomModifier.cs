using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services.Description;
/// <summary>
/// Interface for custom description modifiers.
/// </summary>
public interface ICustomModifier
{
    /// <summary>
    /// Adds or modifies description lines in the response.
    /// </summary>
    /// <param name="data">Data container containing auction representations and price estimates.</param>
    void Apply(DataContainer data);
    /// <summary>
    /// Allows sheduling additional/changing request data before sniper prices are fetched.
    /// </summary>
    /// <param name="preRequest"></param>
    void Modify(ModDescriptionService.PreRequestContainer preRequest);
}