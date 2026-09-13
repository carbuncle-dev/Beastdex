// SPDX-License-Identifier: GPL-3.0-only
namespace Beastdex.Models;

public enum BestiarySort { NextCatch, LowestLevel, BestiaryNumber, Name }

/// <summary>A single source in the unified list. A bestiary hint adds context, not capture proof.</summary>
public sealed record CaptureOption(uint BeastId, SpawnPoint Point, SpawnGroup? Group,
    bool MatchesBestiary, bool IsHintOnly, string Key)
{
    public bool IsReported => Group?.IsReported == true;
    public bool IsCandidate => !IsHintOnly && !IsReported;
    public string EvidenceLabel => IsHintOnly ? "Bestiary hint" : IsReported
        ? MatchesBestiary ? "Bestiary + community" : "Community source"
        : MatchesBestiary ? "Bestiary area / candidate" : "Unverified candidate";
}

public sealed record BeastPlan(BeastInfo Beast, CaptureOption[] Options, CaptureOption? Preferred);
public sealed record NextCatch(BeastPlan Plan, CaptureOption Source)
{
    public uint BeastId => Plan.Beast.RowId;
}

public sealed record TeleportChoice(TravelDestination? Destination, double? Distance,
    TravelDestination[] Choices, string Reason)
{
    public bool NeedsChoice => Destination == null && Choices.Length > 0;
}
