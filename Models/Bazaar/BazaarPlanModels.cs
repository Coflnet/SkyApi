using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Api.Models.Bazaar;

/// <summary>Optional overrides of the personal bazaar plan; every field has a smart default.</summary>
public class BazaarPlanRequest
{
    /// <summary>Requested modes "active" and/or "investing" (case-insensitive); defaults to active only.</summary>
    public List<string> Modes { get; set; }

    /// <summary>Hold horizon for investing in hours, required when investing is requested, greater than 0 and at most 48. Never clamped.</summary>
    public double? InvestingHorizonHours { get; set; }

    /// <summary>Hold horizon of active orders in hours, defaults to 1.</summary>
    public double? ActiveHorizonHours { get; set; }

    /// <summary>Coins to plan with; defaults to two thirds of the purse of the verified account.</summary>
    public double? BudgetCoins { get; set; }

    /// <summary>Attested sell tax as fraction (for example 0.0125); the engine assumes a rate when omitted.</summary>
    public double? SellTaxRate { get; set; }
}

/// <summary>Request to track one order of a previously returned plan in game.</summary>
public class BazaarPlanTrackRequest
{
    /// <summary>Identifier of the quote the order was returned with.</summary>
    public string QuoteId { get; set; }

    /// <summary>Tag of the planned order to track.</summary>
    public string ItemTag { get; set; }
}

/// <summary>Request to re-plan and track the buy order of one item in game, without a quote of this service.</summary>
public class BazaarPlanTrackItemRequest
{
    /// <summary>Tag of the item to track, required.</summary>
    public string ItemTag { get; set; }

    /// <summary>Requested modes, same as for the plan.</summary>
    public List<string> Modes { get; set; }

    /// <summary>Investing horizon in hours, same rules as for the plan.</summary>
    public int? InvestingHorizonHours { get; set; }

    /// <summary>Coins to plan with, same rules as for the plan.</summary>
    public long? BudgetCoins { get; set; }

    /// <summary>Attested sell tax fraction.</summary>
    public double? SellTaxRate { get; set; }

    /// <summary>Entry price the caller saw; a different fresh entry is flagged in the response.</summary>
    public double? ExpectedEntryPrice { get; set; }
}

/// <summary>Terms an item was tracked with.</summary>
public class BazaarPlanTrackItemResponse
{
    /// <summary>Tag of the item.</summary>
    public string ItemTag { get; set; }

    /// <summary>Display name of the item.</summary>
    public string ItemName { get; set; }

    /// <summary>Quantity of the tracked cycle.</summary>
    public int OriginalQuantity { get; set; }

    /// <summary>Fresh buy price per unit.</summary>
    public double EntryPrice { get; set; }

    /// <summary>Fresh sell price per unit.</summary>
    public double? ExitPrice { get; set; }

    /// <summary>Estimated profit after tax in coins.</summary>
    public double EstimatedNetProfit { get; set; }

    /// <summary>Hours the capital stays locked at most.</summary>
    public double CapitalLockHorizonHours { get; set; }

    /// <summary>Quote the tracked terms come from.</summary>
    public string QuoteId { get; set; }

    /// <summary>True when the fresh entry price differs from the expected one by more than max(0.1, 2%).</summary>
    public bool PriceChanged { get; set; }
}

/// <summary>Identity and validity of the quote an allocation was computed from.</summary>
public class BazaarPlanQuote
{
    /// <summary>Identifier of the quote.</summary>
    public string QuoteId { get; set; }

    /// <summary>Identifier of the request the quote answers.</summary>
    public string RequestId { get; set; }

    /// <summary>Identifier of the market snapshot the plan was computed from.</summary>
    public string MarketFrameId { get; set; }

    /// <summary>When the plan was computed.</summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>Until when the plan may be acted on.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Identifier of the allocation policy.</summary>
    public string PolicyId { get; set; }

    /// <summary>Version of the allocation policy.</summary>
    public int PolicyVersion { get; set; }

    /// <summary>Version of the fill model, null when none was used.</summary>
    public string ModelVersion { get; set; }

    /// <summary>Version of the planning engine.</summary>
    public string EngineVersion { get; set; }

    /// <summary>Rule that decided which orders were admitted.</summary>
    public string AdmissionRule { get; set; }
}

/// <summary>Budget and slot accounting of a plan.</summary>
public class BazaarPlanPortfolio
{
    /// <summary>Budget the engine planned with, after open buy orders were deducted.</summary>
    public double BudgetCoins { get; set; }

    /// <summary>Coins committed by the planned orders.</summary>
    public double SpentCoins { get; set; }

    /// <summary>Budget that stays unspent.</summary>
    public double RemainingBudgetCoins { get; set; }

    /// <summary>Coins held back by the caller.</summary>
    public double? ReserveCoins { get; set; }

    /// <summary>Who owns the reserve.</summary>
    public string ReserveOwner { get; set; }

    /// <summary>Free order slots before the plan.</summary>
    public int FreeSlotsBefore { get; set; }

    /// <summary>Free order slots after the plan.</summary>
    public int FreeSlotsAfter { get; set; }

    /// <summary>Free inventory slots before the plan, null when unknown.</summary>
    public int? FreeInventorySlotsBefore { get; set; }

    /// <summary>Free inventory slots after the plan, null when unknown.</summary>
    public int? FreeInventorySlotsAfter { get; set; }

    /// <summary>Coins already bound in held items.</summary>
    public double HeldExposureCoins { get; set; }

    /// <summary>Items left out because they are already held, ordered or pending.</summary>
    public List<string> SuppressedItemTags { get; set; }
}

/// <summary>Confidence that the full original quantity is bought and sold by the deadline.</summary>
public class BazaarPlanConfidence
{
    /// <summary>Identifier of the evaluated event.</summary>
    public string EventId { get; set; }

    /// <summary>Fraction between 0 and 1; null means unknown and is never a sentinel for 0 or 1.</summary>
    public double? Probability { get; set; }

    /// <summary>Lower confidence bound of the probability as fraction; null when unknown.</summary>
    public double? LowerBound { get; set; }

    /// <summary>Confidence level the bound refers to.</summary>
    public double ConfidenceLevel { get; set; }

    /// <summary>Domain the evidence covers.</summary>
    public string SupportDomainId { get; set; }

    /// <summary>Probability bar (fraction) the engine used to qualify the order; null when not reported.</summary>
    public double? QualifyThreshold { get; set; }

    /// <summary>Where the evidence comes from: "simulation" or "prospective"; null when not reported.</summary>
    public string EvidenceSource { get; set; }

    /// <summary>Identifier of the model.</summary>
    public string ModelId { get; set; }

    /// <summary>Identifier of the calibration.</summary>
    public string CalibrationId { get; set; }

    /// <summary>Qualified, NotQualified, SimulationOnly, InsufficientEvidence, UnsupportedDomain, StaleInputs, Unavailable or NotApplicable.</summary>
    public string Status { get; set; }

    /// <summary>Machine readable reasons for the status.</summary>
    public List<string> ReasonCodes { get; set; }
}

/// <summary>One planned order, mapped one to one from the engine recommendation.</summary>
public class BazaarPlanOrder
{
    /// <summary>Tag of the item.</summary>
    public string ItemTag { get; set; }

    /// <summary>Display name of the item.</summary>
    public string ItemName { get; set; }

    /// <summary>True for a sell offer.</summary>
    public bool IsSell { get; set; }

    /// <summary>Price per unit of this order.</summary>
    public double PricePerUnit { get; set; }

    /// <summary>Amount of this order.</summary>
    public double Amount { get; set; }

    /// <summary>Active or Investing.</summary>
    public string Mode { get; set; }

    /// <summary>Quantity of the complete buy and sell cycle.</summary>
    public int OriginalQuantity { get; set; }

    /// <summary>Planned buy price per unit.</summary>
    public double EntryPrice { get; set; }

    /// <summary>Planned sell price per unit.</summary>
    public double? ExitPrice { get; set; }

    /// <summary>Sell tax fraction used for the profit.</summary>
    public double SellTaxRate { get; set; }

    /// <summary>Where the tax rate comes from, AccountAttested or Assumption.</summary>
    public string TaxSource { get; set; }

    /// <summary>Estimated profit after tax in coins.</summary>
    public double EstimatedNetProfit { get; set; }

    /// <summary>Hours the capital stays locked at most.</summary>
    public double CapitalLockHorizonHours { get; set; }

    /// <summary>Rule that fixes the deadline.</summary>
    public string DeadlineSemantics { get; set; }

    /// <summary>Fill confidence of the cycle.</summary>
    public BazaarPlanConfidence Confidence { get; set; }
}

/// <summary>Account state the plan was computed with.</summary>
public class BazaarPlanAccountState
{
    /// <summary>True when open orders could not be read or only estimates are known.</summary>
    public bool OpenOrdersEstimated { get; set; }

    /// <summary>Items with an open buy order.</summary>
    public List<string> OpenBuyItemTags { get; set; }

    /// <summary>Items held through sell orders or unclaimed buys.</summary>
    public List<string> HeldItemTags { get; set; }

    /// <summary>Free bazaar order slots.</summary>
    public int? FreeOrderSlots { get; set; }

    /// <summary>Revision of the account state.</summary>
    public string Revision { get; set; }
}

/// <summary>A personal bazaar plan.</summary>
public class BazaarPlanResponse
{
    /// <summary>Quote the plan belongs to.</summary>
    public BazaarPlanQuote Quote { get; set; }

    /// <summary>Overall engine status.</summary>
    public string Status { get; set; }

    /// <summary>Investing status, for example EmptyQualified or Unavailable; null when investing was not requested.</summary>
    public string InvestingStatus { get; set; }

    /// <summary>Reasons for the investing status.</summary>
    public List<string> InvestingReasonCodes { get; set; }

    /// <summary>Budget accounting.</summary>
    public BazaarPlanPortfolio Portfolio { get; set; }

    /// <summary>Planned orders.</summary>
    public List<BazaarPlanOrder> Orders { get; set; }

    /// <summary>Advisory investing candidates that are not part of the plan; empty for free users.</summary>
    public List<BazaarPlanOrder> UnqualifiedInvestingCandidates { get; set; }

    /// <summary>Canonical modes that were planned.</summary>
    public List<string> Modes { get; set; }

    /// <summary>Investing horizon that was used.</summary>
    public double? InvestingHorizonHours { get; set; }

    /// <summary>Budget before open buy orders were deducted.</summary>
    public double BudgetCoins { get; set; }

    /// <summary>"request" or "purse-estimate".</summary>
    public string BudgetSource { get; set; }

    /// <summary>Account state the plan was computed with.</summary>
    public BazaarPlanAccountState AccountState { get; set; }

    /// <summary>Tier of the user.</summary>
    public string Tier { get; set; }

    /// <summary>Number of orders only visible with Starter Premium.</summary>
    public int HiddenCount { get; set; }

    /// <summary>Where to get Starter Premium, set when orders are hidden.</summary>
    public string PremiumUrl { get; set; }

    /// <summary>Non fatal problems such as orders_unavailable.</summary>
    public List<string> Warnings { get; set; }
}
