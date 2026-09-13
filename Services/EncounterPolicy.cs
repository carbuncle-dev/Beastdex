// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;

namespace Beastdex.Services;

/// <summary>Convenience tiers among level-ready sources; never assume an unknown spawn is common.</summary>
public enum CaptureSourceTier { CommonOverworld, Duty, Fate, Hunt, Uncertain }

public static class EncounterPolicy
{
    public static bool IsKnownHunt(SpawnPoint point, WorldIndex index) =>
        point.NameId != 0 && index.HuntNameIds.Contains(point.NameId) ||
        point.BaseIdReliable && point.BaseId != 0 && index.HuntBaseIds.Contains(point.BaseId);

    public static SpawnPoint Normalize(SpawnPoint point, WorldIndex index)
    {
        if (IsKnownHunt(point, index)) return point with { Encounter = EncounterKind.Hunt };
        if (point.FateId is > 0) return point with { Encounter = EncounterKind.Fate };
        return point; // Unknown/conditional stays unknown/conditional, even with FateId=0.
    }

    public static bool IsLevelReady(SpawnPoint point, int? bstLevel) =>
        bstLevel is > 0 && point.Level is > 0 && point.Level.Value <= bstLevel.Value;

    public static CaptureSourceTier Tier(CaptureOption option, WorldIndex index)
    {
        // A hint/name match cannot be promoted into an ordinary capture source.
        if (!option.IsReported || option.IsHintOnly) return CaptureSourceTier.Uncertain;
        var p = option.Point;
        // Explicit event/elite-hunt evidence overrides any generic "regular" or duty label.
        if (IsKnownHunt(p, index) || p.Encounter == EncounterKind.Hunt) return CaptureSourceTier.Hunt;
        if (p.FateId is > 0 || p.Encounter == EncounterKind.Fate) return CaptureSourceTier.Fate;
        if (p.Encounter != EncounterKind.Regular || p.FateId != 0) return CaptureSourceTier.Uncertain;
        if (TravelPlanning.DutyId(p, index) != 0) return CaptureSourceTier.Duty;
        if (p.TerritoryId != 0 && index.Territories.ContainsKey(p.TerritoryId) &&
            ReportIdentity.HasNameIdentity(p) && !ReportIdentity.HasConflictingHuntIdentity(p, index))
            // This is a reported regular source, not an inference from the absence of a
            // hunt/FATE record. Missing unrelated hunt rows must not veto every common source.
            return CaptureSourceTier.CommonOverworld;
        return CaptureSourceTier.Uncertain;
    }

    public static bool IsCommonOverworldWithinLevel(CaptureOption option, WorldIndex index, int? bstLevel) =>
        IsLevelReady(option.Point, bstLevel) && Tier(option, index) == CaptureSourceTier.CommonOverworld;

    public static string RecommendationLabel(CaptureOption option, int? bstLevel) =>
        option.IsHintOnly ? "HINT" : option.IsCandidate ? "CANDIDATE" :
        option.Point.Level is not > 0 ? "LEVEL ?" : bstLevel is not > 0 ? "LOWEST KNOWN" :
        IsLevelReady(option.Point, bstLevel) ? "PREFERRED" : "NEXT LEVEL";

    public static string RecommendationReason(CaptureOption option, WorldIndex index, int? bstLevel)
    {
        if (option.IsHintOnly) return "Only the bestiary area/duty hint is known, not a particular capture source.";
        if (option.IsCandidate) return "Name/identity candidate only; capture eligibility has not been reported.";
        if (option.Point.Level is not > 0) return "Enemy level is unknown; this is not a level-ready recommendation.";
        if (bstLevel is not > 0) return "BST level is unavailable. Showing the lowest known reported level, not a level-ready recommendation.";
        if (!IsLevelReady(option.Point, bstLevel))
            return $"No reported source is at or below BST {bstLevel}. This is a future target at enemy level {option.Point.Level}.";
        return Tier(option, index) switch
        {
            CaptureSourceTier.CommonOverworld => "Preferred: reported common overworld source at or below your BST level." +
                (!index.HuntCatalogAvailable ? " Local hunt cross-check is unavailable; the source's reported encounter type is used." : ""),
            CaptureSourceTier.Duty => "Preferred: dungeon/trial source at or below your BST level. No qualifying common overworld source is available; duties rank ahead of FATEs and hunts.",
            CaptureSourceTier.Fate => "Preferred fallback: a FATE source within your BST level. No qualifying common overworld or dungeon/trial source is available.",
            CaptureSourceTier.Hunt => "Last known encounter tier: an elite hunt within your BST level. No qualifying common overworld, dungeon/trial or FATE source is available.",
            _ => "Capture is reported, but encounter conditions or identity are unresolved. This source is not treated as common wildlife.",
        };
    }
}
