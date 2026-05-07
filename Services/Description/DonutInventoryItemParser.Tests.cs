using fNbt.Tags;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Description.Tests;

[TestFixture]
public class DonutInventoryItemParserTests
{
    [Test]
    public void ParseSlot_ReadsTrimFromModernComponents()
    {
        var slot = DonutInventoryItemParser.ParseSlot(
            new NbtCompound(string.Empty)
            {
                new NbtString("id", "minecraft:diamond_chestplate"),
                new NbtByte("Count", (byte)1),
                new NbtCompound("components")
                {
                    new NbtCompound("minecraft:trim")
                    {
                        new NbtString("material", "minecraft:amethyst"),
                        new NbtString("pattern", "minecraft:sentry")
                    }
                }
            });

        Assert.That(slot.Item, Is.Not.Null);
        Assert.That(slot.Item!.ItemId, Is.EqualTo("minecraft:diamond_chestplate"));
        Assert.That(slot.Item.Trim, Is.Not.Null);
        Assert.That(slot.Item.Trim!.Material, Is.EqualTo("amethyst"));
        Assert.That(slot.Item.Trim.Pattern, Is.EqualTo("sentry"));
    }

    [Test]
    public void ParseSlot_FallsBackToLegacyTrimTag()
    {
        var slot = DonutInventoryItemParser.ParseSlot(
            new NbtCompound(string.Empty)
            {
                new NbtString("id", "minecraft:diamond_helmet"),
                new NbtByte("Count", (byte)1),
                new NbtCompound("tag")
                {
                    new NbtCompound("Trim")
                    {
                        new NbtString("material", "minecraft:redstone"),
                        new NbtString("pattern", "minecraft:spire")
                    }
                }
            });

        Assert.That(slot.Item, Is.Not.Null);
        Assert.That(slot.Item!.Trim, Is.Not.Null);
        Assert.That(slot.Item.Trim!.Material, Is.EqualTo("redstone"));
        Assert.That(slot.Item.Trim.Pattern, Is.EqualTo("spire"));
    }

    [Test]
    public void ParseSlot_ReadsModernComponentFieldsFromDonutInventory()
    {
        var slot = DonutInventoryItemParser.ParseSlot(
            new NbtCompound(string.Empty)
            {
                new NbtString("id", "minecraft:filled_map"),
                new NbtInt("count", 1),
                new NbtCompound("components")
                {
                    new NbtInt("minecraft:map_id", 4546694),
                    new NbtCompound("minecraft:custom_name")
                    {
                        new NbtString("text", "Map"),
                        new NbtByte("italic", 0)
                    },
                    CreateCompoundList(
                        "minecraft:lore",
                        new NbtCompound((string)null!)
                        {
                            new NbtString("color", "#00FF00"),
                            new NbtString("text", "$1K"),
                            new NbtByte("italic", 0)
                        }),
                    new NbtCompound("minecraft:enchantments")
                    {
                        new NbtInt("minecraft:efficiency", 5),
                        new NbtInt("minecraft:fortune", 3)
                    },
                    new NbtCompound("minecraft:custom_data")
                    {
                        new NbtCompound("PublicBukkitValues")
                        {
                            new NbtInt("minecraft:copyid", 4546694)
                        }
                    }
                }
            });

        Assert.That(slot.Item, Is.Not.Null);
        Assert.That(slot.Item!.DisplayName, Is.EqualTo("Map"));
        Assert.That(slot.Item.Count, Is.EqualTo(1));
        Assert.That(slot.Item.Lore, Is.EqualTo(new[] { "$1K" }));
        Assert.That(slot.Item.MapId, Is.EqualTo(4546694));
        Assert.That(slot.Item.CopyId, Is.EqualTo(4546694));
        Assert.That(slot.Item.VisiblePrice, Is.EqualTo(1000));
        Assert.That(slot.Item.Enchants, Is.Not.Null);
        Assert.That(slot.Item.Enchants!["efficiency"], Is.EqualTo(5));
        Assert.That(slot.Item.Enchants!["fortune"], Is.EqualTo(3));
    }

    [Test]
    public void ParseSlot_ReadsNestedLoreTextExtras()
    {
        var slot = DonutInventoryItemParser.ParseSlot(
            new NbtCompound(string.Empty)
            {
                new NbtString("id", "minecraft:cooked_beef"),
                new NbtInt("count", 14),
                new NbtCompound("components")
                {
                    CreateCompoundList(
                        "minecraft:lore",
                        new NbtCompound((string)null!)
                        {
                            new NbtString("text", string.Empty),
                            CreateCompoundList(
                                "extra",
                                new NbtCompound((string)null!)
                                {
                                    new NbtString("text", "Worth: "),
                                    new NbtString("color", "gray"),
                                    new NbtByte("italic", 0)
                                },
                                new NbtCompound((string)null!)
                                {
                                    new NbtString("text", "$70"),
                                    new NbtString("color", "#00FF00"),
                                    new NbtByte("italic", 0)
                                })
                        })
                }
            });

        Assert.That(slot.Item, Is.Not.Null);
        Assert.That(slot.Item!.Lore, Is.EqualTo(new[] { "Worth: $70" }));
        Assert.That(slot.Item.Count, Is.EqualTo(14));
        Assert.That(slot.Item.VisiblePrice, Is.EqualTo(70));
    }

    [Test]
    public void ParseSlot_ReadsMatchingHintsFromPublicBukkitValues()
    {
        var slot = DonutInventoryItemParser.ParseSlot(
            new NbtCompound(string.Empty)
            {
                new NbtString("id", "minecraft:filled_map"),
                new NbtInt("count", 1),
                new NbtByte("Slot", (byte)30),
                new NbtCompound("components")
                {
                    new NbtInt("minecraft:map_id", 4176629),
                    CreateCompoundList(
                        "minecraft:lore",
                        new NbtCompound((string)null!)
                        {
                            new NbtString("text", "$9.9K"),
                            new NbtString("color", "#00FF00"),
                            new NbtByte("italic", 0)
                        }),
                    new NbtCompound("minecraft:custom_data")
                    {
                        new NbtCompound("PublicBukkitValues")
                        {
                            new NbtInt("minecraft:copyid", 4279790),
                            new NbtInt("minecraft:auctionsecurity", 1),
                            new NbtLong("minecraft:wi", 1778096515448L),
                            new NbtLong("minecraft:checkcdown", 1772453068281L),
                            new NbtString("minecraft:gownerctid", "047de986-bdbc-4322-b88a-01c188903b43")
                        }
                    }
                }
            });

        Assert.That(slot.Slot, Is.EqualTo(30));
        Assert.That(slot.Item, Is.Not.Null);
        Assert.That(slot.Item!.MapId, Is.EqualTo(4176629));
        Assert.That(slot.Item.CopyId, Is.EqualTo(4279790));
        Assert.That(slot.Item.VisiblePrice, Is.EqualTo(9900));
        Assert.That(slot.Item.SellerUuidHint, Is.EqualTo("047de986-bdbc-4322-b88a-01c188903b43"));
        Assert.That(slot.Item.ObservedAtUnixMs, Is.EqualTo(1778096515448L));
        Assert.That(slot.Item.CooldownUntilUnixMs, Is.EqualTo(1772453068281L));
        Assert.That(slot.Item.AuctionSecurity, Is.EqualTo(1));
    }

    private static NbtList CreateCompoundList(string name, params NbtCompound[] entries)
    {
        var list = new NbtList(name);
        foreach (var entry in entries)
            list.Add(entry);

        return list;
    }
}