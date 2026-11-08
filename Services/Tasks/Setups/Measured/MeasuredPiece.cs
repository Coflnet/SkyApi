using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Measured;

/// <summary>
/// One item of a measured setup, a gear piece or the pet, with the spec the acquisition looks for.
/// <see cref="SpecKey"/> is equal for equal requirements, it decides ownership and deduplicates lookups.
/// </summary>
public sealed class MeasuredPiece
{
    public const string PetSlot = "pet";

    public string Slot { get; private init; }
    public string Tag { get; private init; }
    public GearPiece Piece { get; private init; }
    public GearPet Pet { get; private init; }
    public GearItemSpec ItemSpec { get; private init; }
    public GearPetSpec PetSpec { get; private init; }
    public string SpecKey { get; private init; }
    /// <summary>False for a tag only piece: too few players share its modifiers, so it is priced without any</summary>
    public bool ModifiersMeasured { get; private init; }

    public static MeasuredPiece From(string slot, GearPiece piece)
    {
        var spec = ToSpec(piece);
        var target = GearTarget.From(spec);
        return new MeasuredPiece
        {
            Slot = slot, Tag = target.Tag, Piece = piece, ItemSpec = spec, SpecKey = target.Key,
            ModifiersMeasured = HasModifiers(piece),
        };
    }

    public static MeasuredPiece From(GearPet pet)
    {
        var spec = ToSpec(pet);
        var target = GearTarget.From(spec);
        return new MeasuredPiece
        {
            Slot = PetSlot, Tag = target.Tag, Pet = pet, PetSpec = spec, SpecKey = target.Key,
            ModifiersMeasured = pet.Tier != null || pet.Level > 0 || pet.HeldItem != null,
        };
    }

    /// <summary>The slots in a stable order and the pet last, pieces without a tag are skipped</summary>
    public static List<MeasuredPiece> Of(GearCluster cluster)
    {
        var pieces = (cluster.Slots ?? new())
            .Where(s => !string.IsNullOrWhiteSpace(s.Value?.Tag))
            .OrderBy(s => SlotOrder(s.Key)).ThenBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => From(s.Key, s.Value))
            .ToList();
        if (!string.IsNullOrWhiteSpace(cluster.Pet?.Type))
            pieces.Add(From(cluster.Pet));
        return pieces;
    }

    private static readonly string[] Order = ["tool", "helmet", "chestplate", "leggings", "boots", "necklace", "cloak", "belt", "gloves"];

    private static int SlotOrder(string slot)
    {
        var index = Array.IndexOf(Order, slot);
        return index < 0 ? Order.Length : index;
    }

    private static GearItemSpec ToSpec(GearPiece piece) => new()
    {
        Tag = piece.Tag,
        Rarity = piece.Rarity,
        Reforge = piece.Reforge,
        Stars = piece.Stars,
        HotPotatoCount = piece.HotPotatoCount,
        Recombobulated = piece.Recombobulated,
        Enchants = piece.Enchants ?? new(),
        Gems = piece.Gems ?? new(),
        Parts = piece.Parts ?? new(),
        Counters = piece.Counters ?? new(),
        Applied = piece.Applied ?? new(),
        ItemType = piece.ItemType,
    };

    private static GearPetSpec ToSpec(GearPet pet) => new()
    {
        Type = pet.Type,
        Tier = pet.Tier,
        Level = pet.Level,
        HeldItem = pet.HeldItem,
    };

    /// <summary>A tag only piece has no rarity either, every normalized piece read from an item has one</summary>
    private static bool HasModifiers(GearPiece piece)
        => piece.Rarity != null || piece.Reforge != null || piece.Stars != null || piece.HotPotatoCount != null
            || piece.Recombobulated != null || NotEmpty(piece.Enchants) || NotEmpty(piece.Gems) || NotEmpty(piece.Parts)
            || NotEmpty(piece.Counters) || NotEmpty(piece.Applied);

    private static bool NotEmpty(ICollection values) => values is { Count: > 0 };
}
