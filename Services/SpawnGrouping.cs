// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using Beastdex.Models;

namespace Beastdex.Services;

/// <summary>Deterministic, bounded spatial clustering. Does not invent a navmesh or a spawn population.</summary>
public static class SpawnGrouping
{
    private static string Identity(SpawnPoint p) => p.NameId != 0 ? $"n{p.NameId}" : "s" + SpawnMatching.NormalizeName(p.Name);
    private static uint Duty(SpawnPoint p, WorldIndex index) => p.DutyId != 0 ? p.DutyId : index.Territories.GetValueOrDefault(p.TerritoryId)?.DutyId ?? 0;
    private static string Bucket(SpawnPoint p, WorldIndex index) =>
        $"{p.TerritoryId}:{Duty(p, index)}:{p.MapId}:{Identity(p)}:{p.Level}:{p.Encounter}:{p.FateId}:{p.AreaLabelForUnknown()}";
    private static string AreaLabelForUnknown(this SpawnPoint p) => p.TerritoryId == 0 ? p.AreaLabel : string.Empty;

    public static SpawnGroup[] Group(IEnumerable<SpawnCandidate> candidates, WorldIndex index,
        float linkRadius = 160, CancellationToken cancellation = default)
    {
        linkRadius = float.IsFinite(linkRadius) ? Math.Clamp(linkRadius, 30, 300) : 160;
        var output = new List<SpawnGroup>();
        foreach (var bucket in candidates.GroupBy(c => Bucket(c.Point, index)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            var rows = bucket.OrderBy(c => c.Point.X).ThenBy(c => c.Point.Z).ThenBy(c => c.Point.Y)
                .ThenBy(c => c.Point.Evidence).ThenBy(c => c.Point.Source, StringComparer.Ordinal).ToArray();
            var positioned = rows.Where(c => c.Point.HasCoordinates).ToArray();
            var clusters = new List<List<SpawnCandidate>>();
            // Greedy complete-span guard prevents a chain of wandering samples from merging an entire zone.
            // Vertical separation is respected when both records have known world elevation.
            foreach (var row in positioned)
            {
                cancellation.ThrowIfCancellationRequested();
                List<SpawnCandidate>? chosen = null;
                var bestDistance = double.MaxValue;
                foreach (var cluster in clusters)
                {
                    if (cluster.Any(other => Math.Abs(row.Point.X - other.Point.X) > 2 * linkRadius ||
                            Math.Abs(row.Point.Z - other.Point.Z) > 2 * linkRadius ||
                            ((Duty(row.Point, index) != 0 || index.Territories.GetValueOrDefault(row.Point.TerritoryId)?.MapIds.Length > 1) &&
                                row.Point.HasWorldY && other.Point.HasWorldY && Math.Abs(row.Point.Y - other.Point.Y) > 18))) continue;
                    var distance = cluster.Min(other => DistanceSquared(row.Point, other.Point));
                    if (distance <= linkRadius * linkRadius && distance < bestDistance)
                    { chosen = cluster; bestDistance = distance; }
                }
                if (chosen == null) { chosen = []; clusters.Add(chosen); }
                chosen.Add(row);
            }
            var areaOnly = rows.Where(c => !c.Point.HasCoordinates).ToArray();
            // Area-only reports don't create a fake (0,0) point. Attach contextual evidence to
            // positioned groups; if none exist, keep one area-only row with the duty/area action.
            if (clusters.Count == 0) clusters.Add([]);
            foreach (var cluster in clusters)
            {
                cluster.AddRange(areaOnly);
                if (cluster.Count == 0) continue;
                var located = cluster.Where(c => c.Point.HasCoordinates).ToArray();
                SpawnCandidate representative;
                var radius = 0f;
                if (located.Length == 0) representative = cluster.OrderByDescending(c => c.IsReported).First();
                else
                {
                    // Choose an actual sampled point, not the arithmetic center (which may be in a wall/river).
                    var meanX = located.Average(c => c.Point.X);
                    var meanZ = located.Average(c => c.Point.Z);
                    var precise = located.Where(c => c.Point.Evidence != SpawnEvidence.CommunityReport).ToArray();
                    var pool = precise.Length == 0 ? located : precise;
                    representative = pool.OrderBy(c => Math.Pow(c.Point.X - meanX, 2) + Math.Pow(c.Point.Z - meanZ, 2))
                        .ThenByDescending(c => c.IsReported).ThenBy(c => c.Point.X).ThenBy(c => c.Point.Z).First();
                    radius = (float)Math.Sqrt(located.Max(c => DistanceSquared(c.Point, representative.Point)));
                }
                if (cluster.Any(c => c.IsReported) && !representative.IsReported)
                    representative = representative with { Match = NameMatchKind.CommunityReport,
                        CaptureAttribution = cluster.First(c => c.IsReported).CaptureAttribution };
                var p = representative.Point;
                var key = bucket.Key + ":" + p.X.ToString("R", CultureInfo.InvariantCulture) + ":" + p.Z.ToString("R", CultureInfo.InvariantCulture);
                output.Add(new SpawnGroup(representative, cluster.ToArray(), located.Length, radius, key));
            }
        }
        return Rank(output, index).ToArray();
    }

    public static double DistanceSquared(SpawnPoint a, SpawnPoint b) => Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2);
    public static int EncounterRank(EncounterKind kind) => kind switch
    { EncounterKind.Regular => 0, EncounterKind.Fate => 1, EncounterKind.Hunt => 2, EncounterKind.Conditional => 3, _ => 4 };
    public static IOrderedEnumerable<SpawnGroup> Rank(IEnumerable<SpawnGroup> groups, WorldIndex index) => groups
        .OrderBy(g => g.Point.Level is > 0 ? g.Point.Level.Value : int.MaxValue)
        .ThenBy(g => Duty(g.Point, index) != 0 ? 1 : g.Point.TerritoryId != 0 ? 0 : 2)
        .ThenBy(g => EncounterRank(g.Point.Encounter))
        .ThenByDescending(g => g.IsReported).ThenByDescending(g => g.Point.HasCoordinates)
        .ThenBy(g => g.Point.TerritoryId).ThenBy(g => g.Point.NameId).ThenBy(g => g.Key, StringComparer.Ordinal);

    public static SpawnGroup? Preferred(IEnumerable<SpawnGroup> groups, WorldIndex index)
    {
        // A lower-level lookalike must never displace a reported capture source.
        // Candidate-only recommendations remain explicitly labelled as such in the UI.
        var valid = groups.Where(g => g.Point.TerritoryId != 0).ToArray();
        var reported = valid.Where(g => g.IsReported).ToArray();
        return Rank(reported.Length > 0 ? reported : valid, index).FirstOrDefault();
    }
}
