using System;
using System.Collections;
using System.Collections.Generic;
using Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Measured;

/// <summary>
/// One item of a measured build, a gear piece or the pet of PlayerState's piece catalog, with the spec the acquisition
/// looks for. <see cref="SpecKey"/> is equal for equal requirements, it deduplicates lookups.
/// </summary>
public sealed class MeasuredPiece
{
    public const string PetSlot = "pet";

    /// <summary>The catalog id ("slot:TAG:hash" or "pet:..."), null for a piece not from the catalog</summary>
    public string Id { get; private init; }
    /// <summary>The catalog entry with the score contribution and the effects, null for a piece not from the catalog</summary>
    public GearCatalogPiece Catalog { get; private init; }
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

    /// <summary>A catalog entry, null when it has neither an item with a tag nor a pet with a type</summary>
    public static MeasuredPiece Of(GearCatalogPiece entry)
    {
        var piece = FromEntry(entry);
        if (piece == null)
            return null;
        return new MeasuredPiece
        {
            Id = entry.Id, Catalog = entry, Slot = piece.Slot, Tag = piece.Tag, Piece = piece.Piece, Pet = piece.Pet,
            ItemSpec = piece.ItemSpec, PetSpec = piece.PetSpec, SpecKey = piece.SpecKey, ModifiersMeasured = piece.ModifiersMeasured,
        };
    }

    private static MeasuredPiece FromEntry(GearCatalogPiece entry)
    {
        if (!string.IsNullOrWhiteSpace(entry?.Item?.Tag) && !string.IsNullOrWhiteSpace(entry.Slot))
            return From(entry.Slot, entry.Item);
        return string.IsNullOrWhiteSpace(entry?.Pet?.Type) ? null : From(entry.Pet);
    }

    /// <summary>Slots in a stable display order (tool, armor, equipment), the pet and unknown slots last</summary>
    public static int SlotOrder(string slot)
    {
        var index = Array.IndexOf(Order, slot);
        return index < 0 ? Order.Length : index;
    }

    private static readonly string[] Order = ["tool", "helmet", "chestplate", "leggings", "boots", "necklace", "cloak", "belt", "gloves"];

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
