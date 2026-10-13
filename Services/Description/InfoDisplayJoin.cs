using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Api.Models.Mod;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// The mod only shows the LAST extra list (a list beyond the item-slot lists) as the info panel, so a
/// modifier that appends its own list would hide the panel of the screen. This folds such a list into
/// the previous panel so both are visible.
/// </summary>
public static class InfoDisplayJoin
{
    /// <summary>
    /// Joins the lists added since <paramref name="addedFromIndex"/> with the extra list directly before
    /// them into one last list. Does nothing when nothing was added or no extra list (index at or after
    /// <paramref name="slotListCount"/>) existed before. The added lines come first followed by an empty
    /// separator line and the previous panel, or after the previous panel when
    /// <paramref name="appendAfterPanel"/> is set (invisible placeholders). The previous list is removed.
    /// </summary>
    public static void Apply(List<List<DescModification>> mods, int slotListCount, int addedFromIndex, bool appendAfterPanel = false)
    {
        if (mods == null || addedFromIndex <= slotListCount || addedFromIndex > mods.Count)
            return;
        var previous = mods[addedFromIndex - 1];
        if (previous == null || previous.Count == 0)
            return;
        var added = mods.Skip(addedFromIndex).Where(l => l != null && l.Count > 0).SelectMany(l => l).ToList();
        if (added.Count == 0)
            return;
        var joined = new List<DescModification>(previous.Count + added.Count + 1);
        if (appendAfterPanel)
        {
            joined.AddRange(previous);
            joined.AddRange(added);
        }
        else
        {
            joined.AddRange(added);
            joined.Add(new DescModification(""));
            joined.AddRange(previous);
        }
        mods.RemoveRange(addedFromIndex - 1, mods.Count - addedFromIndex + 1);
        mods.Add(joined);
    }
}
