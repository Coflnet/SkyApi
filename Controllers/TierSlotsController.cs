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

namespace Coflnet.Sky.Api.Controller;

/// <summary>Manage purchased capacity without transferring billing ownership.</summary>
[ApiController, Authorize]
[Route("api/premium/slots")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class TierSlotsController(GoogletokenService tokens, ITierSlotsApi slots,
    PlayerNameService names, UserApi users) : ControllerBase
{
    [HttpGet("products"), AllowAnonymous]
    public Task<List<PurchaseableProduct>> GetProducts() => slots.ApiTierSlotsProductsGetAsync();

    [HttpGet]
    public async Task<List<TierSlotAccess>> GetOwned() => await slots.ApiTierSlotsOwnerOwnerIdGetAsync(await tokens.GetUserId(this));

    [HttpGet("assigned")]
    public async Task<List<TierSlotAccess>> GetAssigned() => await slots.ApiTierSlotsAccessGetAsync(await tokens.GetUserId(this));

    [HttpPut("{id:long}/assignment")]
    public async Task<IActionResult> Assign(long id, SlotRecipient recipient)
    {
        var ownerId = await tokens.GetUserId(this, true);
        // Check ownership before resolving an email address or Minecraft name.
        if (!(await slots.ApiTierSlotsOwnerOwnerIdGetAsync(ownerId)).Any(s => s.Id == id))
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
        await slots.ApiTierSlotsOwnerOwnerIdIdAssignmentPutAsync(ownerId, id,
            new TierSlotAssignment(userId, uuid, varVersion: recipient.Version));
        return NoContent();
    }
}

public class SlotRecipient
{
    [EmailAddress, MaxLength(254)]
    public string Email { get; set; }
    [MaxLength(36)]
    public string MinecraftAccount { get; set; }
    public long Version { get; set; }
}
