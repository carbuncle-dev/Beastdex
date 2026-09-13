// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;

namespace Beastdex.Services;

public readonly record struct LiveCaptureNpc(uint TerritoryId, uint DutyId, uint NameId, uint BaseId,
    int Level, uint? FateId);
public sealed record BeastMarkerRule(uint BeastId, SpawnPoint Source);

/// <summary>Strict report-to-live-NPC matching. No family, model or substring-based markers.</summary>
public static class BeastTargetMatching
{
    public static BeastMarkerRule[] BuildRules(IEnumerable<BeastPlan> plans, WorldIndex index) => plans
        .SelectMany(plan => plan.Options.Where(o => o.IsReported && !o.IsHintOnly)
            .Select(o => new BeastMarkerRule(plan.Beast.RowId, EncounterPolicy.Normalize(o.Point, index))))
        .Where(r => r.Source.TerritoryId != 0 && ReportIdentity.HasNameIdentity(r.Source) && r.Source.Level is > 0 &&
            (r.Source.Encounter is EncounterKind.Regular or EncounterKind.Fate or EncounterKind.Hunt) &&
            !ReportIdentity.HasConflictingHuntIdentity(r.Source, index) &&
            (r.Source.Encounter != EncounterKind.Fate || r.Source.FateId is > 0))
        .ToArray();

    public static bool Matches(BeastMarkerRule rule, LiveCaptureNpc npc, WorldIndex index,
        XbmCaptureSnapshot captures, int? effectiveBstLevel, bool missingOnly, bool withinLevel)
    {
        var source = rule.Source;
        if (missingOnly && (!captures.Available || captures.CapturedRowIds.Contains(rule.BeastId))) return false;
        if (withinLevel && (effectiveBstLevel is not > 0 || npc.Level > effectiveBstLevel.Value)) return false;
        if (npc.Level <= 0 || source.Level != npc.Level || source.TerritoryId != npc.TerritoryId ||
            TravelPlanning.DutyId(source, index) != npc.DutyId || !ReportIdentity.MatchesName(source, npc.NameId)) return false;
        if (source.BaseIdReliable && source.BaseId != 0 && source.BaseId != npc.BaseId) return false;
        // A regular report cannot mark a FATE copy. Unknown live FATE state stays unknown.
        return source.Encounter switch
        {
            EncounterKind.Regular => npc.FateId == 0 && !index.HuntNameIds.Contains(npc.NameId) &&
                !index.HuntBaseIds.Contains(npc.BaseId),
            // A report naming a FATE without a mapped event ID does not authorize all
            // same-name, same-level copies in other events in this territory.
            EncounterKind.Fate => source.FateId is > 0 && source.FateId == npc.FateId,
            EncounterKind.Hunt => npc.FateId == 0,
            _ => false,
        };
    }
}
