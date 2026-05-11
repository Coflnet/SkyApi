using fNbt.Tags;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Description.Tests;

[TestFixture]
public class DonutInventoryItemParserTests
{
    [TestCase("ᴀᴜᴄᴛɪᴏɴ (Page 1)", 0, true)]
    [TestCase("ᴀᴜᴄᴛɪᴏɴ (Page 1)", 44, true)]
    [TestCase("ᴀᴜᴄᴛɪᴏɴ (Page 1)", 47, false)]
    [TestCase("ᴀᴜᴄᴛɪᴏɴ (Page 1)", 81, false)]
    [TestCase("Auction House", 0, false)]
    [TestCase("Your Items", 0, false)]
    [TestCase(null, 0, false)]
    public void ShouldMatchAuctionSlot_OnlyAllowsAuctionListingSlots(string? chestName, int slot, bool expected)
    {
        Assert.That(DonutInventoryItemParser.ShouldMatchAuctionSlot(chestName, slot), Is.EqualTo(expected));
    }

    [TestCase("ᴀᴜᴄᴛɪᴏɴ (Page 1)", 1)]
    [TestCase("Auction (Page 12)", 12)]
    [TestCase("Auction House", null)]
    public void ParseAuctionPageNumber_ReadsPageNumbers(string? chestName, int? expectedPage)
    {
        Assert.That(DonutInventoryItemParser.ParseAuctionPageNumber(chestName), Is.EqualTo(expectedPage));
    }

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

    [Test]
    public void ParseSlot_MergesStoredEnchantments()
    {
        var slot = DonutInventoryItemParser.ParseSlot(
            new NbtCompound(string.Empty)
            {
                new NbtString("id", "minecraft:enchanted_book"),
                new NbtInt("count", 1),
                new NbtCompound("components")
                {
                    new NbtCompound("minecraft:stored_enchantments")
                    {
                        new NbtInt("minecraft:unbreaking", 3)
                    }
                }
            });

        Assert.That(slot.Item, Is.Not.Null);
        Assert.That(slot.Item!.Enchants, Is.Not.Null);
        Assert.That(slot.Item.Enchants!["unbreaking"], Is.EqualTo(3));
    }

    [Test]
    public void ParseSlot_ReadsMatchingExtraData()
    {
        var slot = DonutInventoryItemParser.ParseSlot(
            new NbtCompound(string.Empty)
            {
                new NbtString("id", "minecraft:splash_potion"),
                new NbtInt("count", 1),
                new NbtCompound("components")
                {
                    new NbtInt("minecraft:damage", 9),
                    new NbtInt("minecraft:repair_cost", 7),
                    new NbtCompound("minecraft:potion_contents")
                    {
                        new NbtString("potion", "minecraft:long_invisibility")
                    },
                    new NbtCompound("minecraft:custom_data")
                    {
                        new NbtCompound("VV|Protocol1_21_11To26_1|original_hashes")
                        {
                            new NbtInt("0", -952227886),
                            new NbtInt("6", 946104624),
                            new NbtInt("11", -1456672447),
                            new NbtInt("49", -1873371881),
                            new NbtInt("id", 1291),
                            new NbtIntArray("removed")
                        },
                        new NbtCompound("PublicBukkitValues")
                        {
                            new NbtLong("minecraft:expirable", 1778312006616L),
                            new NbtLong("minecraft:amethystdriller", 1778312006616L),
                            new NbtInt("minecraft:auctionsecurity", 1),
                            new NbtLong("minecraft:wi", 1778158946639L)
                        }
                    }
                }
            });

        Assert.That(slot.Item, Is.Not.Null);
        Assert.That(slot.Item!.ExtraData, Is.Not.Null);
        Assert.That(slot.Item.ExtraData!["potionId"], Is.EqualTo("long_invisibility"));
        Assert.That(slot.Item.ExtraData["damage"], Is.EqualTo(9));
        Assert.That(slot.Item.ExtraData["repairCost"], Is.EqualTo(7));
        Assert.That(slot.Item.ExtraData["originalHashes"], Is.EqualTo("0=-952227886;11=-1456672447;49=-1873371881;6=946104624;id=1291;removed="));
        Assert.That(slot.Item.ExtraData["pbv:expirable"], Is.EqualTo(1778312006616L));
        Assert.That(slot.Item.ExtraData["pbv:amethystdriller"], Is.EqualTo(1778312006616L));
    }

    private static NbtList CreateCompoundList(string name, params NbtCompound[] entries)
    {
        var list = new NbtList(name);
        foreach (var entry in entries)
            list.Add(entry);

        return list;
    }
}