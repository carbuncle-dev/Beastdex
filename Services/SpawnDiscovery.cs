// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;

namespace Beastdex.Services;

public sealed record DiscoveryResult(Dictionary<uint, SpawnGroup[]> Groups,
    Dictionary<uint, SpawnGroup[]> HintAreaGroups);

/// <summary>Global search plus explicitly scoped report associations. No family/model-to-capture guesses.</summary>
public static class SpawnDiscovery
{
    public static DiscoveryResult Build(WorldIndex index, BeastInfo[] beasts, SpawnPoint[] observed,
        float radius, CancellationToken cancellation = default)
    {
        var ordinary = index.LayoutPoints.Concat(observed).Concat(index.CommunityPoints.Where(p => p.CaptureBeastId == 0))
            .Select(p => EncounterPolicy.Normalize(TravelPlanning.NormalizeMap(p, index), index)).ToArray();
        var byName = ordinary.GroupBy(p => (p.NameId, p.Name, p.EnglishName)).ToArray();
        var reports = index.CommunityPoints.Where(p => p.CaptureBeastId != 0)
            .Select(p => EncounterPolicy.Normalize(TravelPlanning.NormalizeMap(p, index), index)).GroupBy(p => p.CaptureBeastId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var output = new Dictionary<uint, SpawnGroup[]>();
        var browse = new Dictionary<uint, SpawnGroup[]>();
        // Group the complete hinted areas once per distinct territory set, off the framework thread.
        var areaGroups = new Dictionary<string, SpawnGroup[]>();
        foreach (var beast in beasts)
        {
            cancellation.ThrowIfCancellationRequested();
            var direct = reports.GetValueOrDefault(beast.RowId) ?? [];
            var englishBeast = index.EnglishBeastNames.GetValueOrDefault(beast.RowId) ?? "";
            var candidates = new List<SpawnCandidate>();
            var joinedReports = new HashSet<SpawnPoint>();
            foreach (var named in byName)
            {
                var localMatch = beast.NameResolved ? SpawnMatching.MatchName(beast.Name, named.Key.Name) : NameMatchKind.None;
                var englishMatch = englishBeast.Length != 0 && named.Key.EnglishName.Length != 0
                    ? SpawnMatching.MatchName(englishBeast, named.Key.EnglishName) : NameMatchKind.None;
                var match = localMatch == NameMatchKind.Exact || englishMatch == NameMatchKind.Exact
                    ? NameMatchKind.Exact : localMatch != NameMatchKind.None || englishMatch != NameMatchKind.None
                        ? NameMatchKind.NameVariant : NameMatchKind.None;
                var relevant = direct.Where(r => ReportIdentity.MatchesName(r, named.Key.NameId)).ToArray();
                if (match == NameMatchKind.None && relevant.Length == 0) continue;
                foreach (var point in named)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var linked = relevant.FirstOrDefault(r => CanJoinReport(r, point, index));
                    if (linked != null)
                    {
                        joinedReports.Add(linked);
                        candidates.Add(new SpawnCandidate(point, NameMatchKind.CommunityReport)
                        { CaptureAttribution = linked.Source + " | " + linked.Conditions });
                    }
                    else if (match != NameMatchKind.None || relevant.Length > 0)
                        // Same-name NPCs in other contexts stay candidates; do not extrapolate capture eligibility.
                        candidates.Add(new SpawnCandidate(point, match != NameMatchKind.None ? match : NameMatchKind.Exact));
                }
            }
            foreach (var report in direct)
            {
                var scoped = candidates.Where(c => c.IsReported && CanJoinReport(report, c.Point, index))
                    .Select(c => c.Point.NameId).Where(id => id != 0).Distinct().ToArray();
                var resolved = report.NameId == 0 && scoped.Length == 1
                    ? report with { NameId = scoped[0], Name = index.NpcNames.GetValueOrDefault(scoped[0]) ?? report.Name }
                    : report;
                if (resolved.HasCoordinates || !joinedReports.Contains(report))
                    candidates.Add(new SpawnCandidate(resolved, NameMatchKind.CommunityReport)
                    { CaptureAttribution = report.Source + " | " + report.Conditions });
            }
            output[beast.RowId] = SpawnGrouping.Group(candidates, index, radius, cancellation);
            var areas = (index.HintTerritories.GetValueOrDefault(beast.RowId) ?? []).Order().ToArray();
            var areaKey = string.Join(",", areas);
            if (!areaGroups.TryGetValue(areaKey, out var grouped))
            {
                var set = areas.ToHashSet();
                grouped = SpawnGrouping.Group(ordinary.Where(p => set.Contains(p.TerritoryId))
                    .Select(p => new SpawnCandidate(p, NameMatchKind.None)), index, radius, cancellation);
                areaGroups[areaKey] = grouped;
            }
            browse[beast.RowId] = grouped;
        }
        return new DiscoveryResult(output, browse);
    }

    public static bool CanJoinReport(SpawnPoint report, SpawnPoint point, WorldIndex index)
    {
        // The explicit capture assertion is scoped to enemy name ID, territory/duty,
        // level AND encounter conditions. It does not bless every mob with the same name.
        if (!ReportIdentity.MatchesName(report, point.NameId) || report.TerritoryId == 0 ||
            report.TerritoryId != point.TerritoryId || report.Level != point.Level || !point.Level.HasValue) return false;
        if (report.MapId != 0 && point.MapId != 0 && report.MapId != point.MapId) return false;
        var pointDuty = point.DutyId != 0 ? point.DutyId : index.Territories.GetValueOrDefault(point.TerritoryId)?.DutyId ?? 0;
        if (report.DutyId != pointDuty || report.Encounter != point.Encounter) return false;
        if (report.Encounter == EncounterKind.Regular) return point.FateId == 0;
        // A FATE/hunt name without a mapped event identity is not a safe association.
        return report.Encounter == EncounterKind.Fate && report.FateId is > 0 && report.FateId == point.FateId;
    }
}
