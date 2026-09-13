// SPDX-License-Identifier: GPL-3.0-only
using System.Text.RegularExpressions;
using Beastdex.Models;

namespace Beastdex.Services;

public sealed record ReportImport(SpawnPoint[] Points, string[] Unresolved);

public static class CommunityReportBinder
{
    public static ReportImport Bind(CollectRow[] rows, WorldIndex index, IReadOnlySet<uint> beastIds)
    {
        var points = new List<SpawnPoint>();
        var unresolved = new List<string>();
        var placeIds = index.EnglishPlaceNames.GroupBy(p => CommunityParsers.LocationKey(p.Value))
            .ToDictionary(g => g.Key, g => g.Select(v => v.Key).ToArray());
        var dutyIds = index.Duties.Values.GroupBy(d => CommunityParsers.LocationKey(d.EnglishName))
            .ToDictionary(g => g.Key, g => g.ToArray());
        var locationKeys = placeIds.Keys.Concat(dutyIds.Keys).Where(k => k.Length > 0).ToHashSet();
        var nameIds = index.EnglishNpcNames.GroupBy(p => SpawnMatching.NormalizeName(p.Value))
            .ToDictionary(g => g.Key, g => g.Select(v => v.Key).ToArray());
        foreach (var row in rows)
        {
            if (!beastIds.Contains(row.BeastId)) continue;
            // Guard ID alignment using the English Pet name from this same game installation.
            if (!index.EnglishBeastNames.TryGetValue(row.BeastId, out var familiarName) ||
                SpawnMatching.NormalizeName(familiarName) != SpawnMatching.NormalizeName(row.EnglishName))
            { unresolved.Add($"XBMPet {row.BeastId}: provider/game name mismatch ({row.EnglishName}); row ignored."); continue; }
            foreach (var line in row.SourceLines)
            {
                if (!CommunityParsers.TryReadCaptureSource(row.BeastId, line, locationKeys, out var report, out var error))
                { unresolved.Add($"XBMPet {row.BeastId}: {line} | {error}"); continue; }
                var r = report!;
                var key = CommunityParsers.LocationKey(r.Area);
                var targets = new List<(uint Territory, uint Duty)>();
                // Exact duty-name resolution precedes area names. Never select another difficulty.
                if (dutyIds.TryGetValue(key, out var duties))
                    targets.AddRange(duties.Where(d => d.TerritoryId != 0 && index.Territories.ContainsKey(d.TerritoryId))
                        .Select(d => (d.TerritoryId, d.Id)).Distinct());
                else if (placeIds.TryGetValue(key, out var places))
                {
                    var ids = places.ToHashSet();
                    targets.AddRange(index.Territories.Values.Where(t => t.DutyId == 0 &&
                        (ids.Contains(t.PlaceNameId) || t.MapIds.Any(m => index.Maps.TryGetValue(m, out var map) &&
                            (ids.Contains(map.PlaceNameId) || ids.Contains(map.SubPlaceNameId)))))
                        .Select(t => (t.Id, 0u)));
                }
                if (targets.Count != 1)
                {
                    // Keep a report visible with no actionable route rather than choosing an instance arbitrarily.
                    unresolved.Add($"XBMPet {row.BeastId}: {line} | territory/duty resolution is {(targets.Count == 0 ? "missing" : "ambiguous")}");
                    points.Add(NewPoint(r, 0, 0, 0, 0, r.Enemy, false, 0, 0));
                    continue;
                }
                var target = targets[0];
                var territory = index.Territories[target.Territory];
                var mapId = territory.MapIds.Length == 1 ? territory.MapIds[0] : 0;
                var hasPosition = mapId != 0 && r.MapX.HasValue && r.MapY.HasValue;
                float x = 0, z = 0;
                if (hasPosition)
                {
                    var map = index.Maps[mapId];
                    hasPosition = map.SizeFactor > 0;
                    if (hasPosition)
                    {
                        x = SpawnMatching.WorldCoordinate(r.MapX!.Value, map.SizeFactor, map.OffsetX);
                        z = SpawnMatching.WorldCoordinate(r.MapY!.Value, map.SizeFactor, map.OffsetY);
                    }
                }
                var enemies = Regex.Split(r.Enemy, @"\s+or\s+", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                // Only exact NPC-name alternatives can remove an otherwise unknown condition.
                // "Enemy - nighttime/quest/etc." must not become an ordinary overworld recommendation.
                if (r.Encounter == EncounterKind.Conditional && !r.Conditions.Contains(':') && enemies.Length > 1)
                {
                    if (enemies.All(e => (nameIds.GetValueOrDefault(SpawnMatching.NormalizeName(e)) ?? []).Length > 0))
                        r = r with { Encounter = EncounterKind.Regular, Conditions = string.Empty };
                    else
                        enemies = [enemies[0]]; // Keep the unknown restriction, not a fictitious second NPC.
                }
                foreach (var enemy in enemies)
                {
                    var ids = nameIds.GetValueOrDefault(SpawnMatching.NormalizeName(enemy)) ?? [];
                    // A report can still supply a displayed level and duty even when a typo prevents identity binding.
                    var nameId = ids.Length == 1 ? ids[0] : 0;
                    if (ids.Length == 0) unresolved.Add($"XBMPet {row.BeastId}: enemy '{enemy}' has no exact BNpcName match; identity unresolved.");
                    // Duplicate IDs are normal (e.g. the same display name reused in another
                    // duty). Preserve the alternatives instead of discarding source classification.
                    // No single ID is guessed here. SpawnDiscovery joins only within the report's scope.
                    points.Add(NewPoint(r, target.Territory, target.Duty, hasPosition ? mapId : 0, nameId,
                        nameId != 0 ? index.NpcNames.GetValueOrDefault(nameId) ?? enemy : enemy, hasPosition, x, z)
                        with { EnglishName = enemy, PossibleNameIds = ids });
                }
            }
        }
        return new ReportImport(points.ToArray(), unresolved.ToArray());
    }

    private static SpawnPoint NewPoint(CaptureReport r, uint territory, uint duty, uint map, uint name,
        string displayName, bool coordinates, float x, float z) => new(territory, map, 0, name, displayName,
        r.Level, x, 0, z, SpawnEvidence.CommunityReport, $"https://ffxivcollect.com/beasts/{r.BeastId}", r.Original)
    {
        CaptureBeastId = r.BeastId, DutyId = duty, HasCoordinates = coordinates, HasWorldY = false,
        Encounter = r.Encounter, FateId = r.Encounter == EncounterKind.Regular ? 0u : null,
        AreaLabel = r.Area, EnglishName = r.Enemy, BaseIdReliable = false,
    };
}
