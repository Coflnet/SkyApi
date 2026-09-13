using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using Coflnet.Payments.Client.Model;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.PlayerName;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Api.Controller;

/// <summary>Manage purchased capacity without transferring billing ownership.</summary>
[ApiController, Authorize]
[Route("api/premium/slots")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class TierSlotsController(GoogletokenService tokens, ITierSlotsApi slots,
    PlayerNameService names, UserApi users, IProductsApi products) : ControllerBase
{
    [HttpGet("products"), AllowAnonymous]
    public async Task<List<PurchaseableProduct>> GetProducts()
    {
        var prepaid = slots.ApiTierSlotsProductsGetAsync();
        // Recurring plans are stored in Payments' separate provider-product catalog.
        var subscriptions = products.ProductsTopupGetAsync(amount: int.MaxValue, includeDisabled: false);
        await Task.WhenAll(prepaid, subscriptions);
        return prepaid.Result.Concat(subscriptions.Result
            .Where(p => p.SlotCount > 0 && p.ProviderSlug == "lemonsqueezy" && p.Slug.StartsWith("l_"))
            .Select(p => new PurchaseableProduct(p.Id, p.Title, p.Slug, p.Description, p.Cost,
                p.OwnershipSeconds, p.SlotCount, p.SlotTier, p.Type)))
            .OrderBy(p => p.Cost).ToList();
    }

    [HttpGet]
    public async Task<List<OwnedTierSlot>> GetOwned()
    {
        var owned = await slots.ApiTierSlotsOwnerOwnerIdGetAsync(await tokens.GetUserId(this));
        var recipientIds = owned.Select(s => int.TryParse(s.AssignedUserId, out var id) ? id : 0).ToList();
        using var context = new HypixelContext();
        var emails = await context.Users.Where(u => recipientIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id.ToString(), u => u.Email);
        var uuids = owned.Select(s => s.MinecraftUuid).Where(uuid => !string.IsNullOrEmpty(uuid)).Distinct().ToList();
        var minecraftNames = uuids.Count == 0 ? new Dictionary<string, string>() : await names.GetNames(uuids);
        return owned.Select(slot => new OwnedTierSlot(slot.Id.ToString(), slot.Tier, slot.Expires,
            slot.AssignedUserId, slot.MinecraftUuid, slot.VarVersion,
            emails.GetValueOrDefault(slot.AssignedUserId ?? ""), minecraftNames.GetValueOrDefault(slot.MinecraftUuid ?? ""))).ToList();
    }

    [HttpGet("assigned")]
    public async Task<List<TierSlotAccess>> GetAssigned() => await slots.ApiTierSlotsAccessGetAsync(await tokens.GetUserId(this));

    [HttpPut("{id}/assignment")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(string), 400)]
    public async Task<IActionResult> Assign(string id, SlotRecipient recipient)
    {
        if (!long.TryParse(id, out var slotId))
            return BadRequest("Invalid slot ID");
        var ownerId = await tokens.GetUserId(this);
        // Check ownership before resolving an email address or Minecraft name.
        if (!(await slots.ApiTierSlotsOwnerOwnerIdGetAsync(ownerId)).Any(s => s.Id == slotId))
            return NotFound();
        string userId = null;
        if (!string.IsNullOrWhiteSpace(recipient.Email))
        {
            var user = await UserService.Instance.GetUserByEmail(recipient.Email.Trim().ToLowerInvariant());
            if (user == null)
                return BadRequest("The recipient must first sign in to SkyCofl with that email.");
            userId = user.Id.ToString();
            await users.UserUserIdGetAsync(userId);
        }
        string uuid = null;
        if (!string.IsNullOrWhiteSpace(recipient.MinecraftAccount))
        {
            var account = recipient.MinecraftAccount.Trim();
            uuid = Guid.TryParse(account, out var parsed) ? parsed.ToString("N") : await names.GetUuid(account);
            if (!Guid.TryParse(uuid, out parsed) || parsed == Guid.Empty)
                return BadRequest("Minecraft account not found");
            uuid = parsed.ToString("N");
        }
        try
        {
            await slots.ApiTierSlotsOwnerOwnerIdIdAssignmentPutAsync(ownerId, slotId,
                new TierSlotAssignment(userId, uuid, varVersion: recipient.Version));
        }
        catch (Coflnet.Payments.Client.Client.ApiException ex) when (ex.ErrorCode is >= 400 and <= 599)
        {
            return new ContentResult { StatusCode = ex.ErrorCode, ContentType = "application/json", Content = ex.ErrorContent as string };
        }
        return NoContent();
    }
}

// CockroachDB slot IDs can exceed JavaScript's safe integer range.
public record OwnedTierSlot(string Id, string Tier, DateTime Expires, string AssignedUserId,
    string MinecraftUuid, long Version, string RecipientEmail, string MinecraftName);

public class SlotRecipient
{
    [EmailAddress, MaxLength(254)]
    public string Email { get; set; }
    [MaxLength(36)]
    public string MinecraftAccount { get; set; }
    public long Version { get; set; }
}
