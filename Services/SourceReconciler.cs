// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;

namespace Beastdex.Services;

/// <summary>
/// Reconciles game hints with grouped source evidence by territory/duty IDs, never fuzzy area text.
/// Hints do not invent coordinates, NPC levels, or capture eligibility for name matches.
/// </summary>
public static class SourceReconciler
{
    public static CaptureOption[] Build(BeastInfo beast, IEnumerable<SpawnGroup> groups, WorldIndex index)
    {
        var output = groups.Select(g => new CaptureOption(beast.RowId,
            TravelPlanning.NormalizeMap(g.Point, index), g, MatchesHint(beast, g.Point, index), false, g.Key)).ToList();
        var hint = beast.Location;
        if (hint.RowId != 0 && !output.Any(o => o.MatchesBestiary))
        {
            var territories = index.HintTerritories.GetValueOrDefault(beast.RowId) ?? [];
            // Multiple possible hint territories remain explicit alternatives, not a made-up combined pin.
            foreach (var territory in territories.Length > 0 ? territories : new uint[] { 0 })
            {
                var p = new SpawnPoint(territory, 0, 0, 0, beast.Name, null, 0, 0, 0,
                    SpawnEvidence.ClientLayout, "In-game Master's Bestiary hint", "No specific capture NPC/position has been established.")
                {
                    HasCoordinates = false, HasWorldY = false, DutyId = hint.Key == 2 ? hint.RowId : 0,
                    AreaLabel = hint.DisplayName, BaseIdReliable = false,
                };
                output.Add(new(beast.RowId, TravelPlanning.NormalizeMap(p, index), null, true, true,
                    $"hint:{hint.Key}:{hint.RowId}:{territory}"));
            }
        }
        return Rank(output, index).ToArray();
    }

    public static bool MatchesHint(BeastInfo beast, SpawnPoint point, WorldIndex index)
    {
        if (beast.Location.RowId == 0) return false;
        var duty = TravelPlanning.DutyId(point, index);
        if (beast.Location.Key == 2) return duty == beast.Location.RowId;
        return beast.Location.Key == 1 && duty == 0 && point.TerritoryId != 0 &&
            (index.HintTerritories.GetValueOrDefault(beast.RowId) ?? []).Contains(point.TerritoryId);
    }

    public static IOrderedEnumerable<CaptureOption> Rank(IEnumerable<CaptureOption> options, WorldIndex index) => options
        .OrderBy(o => o.Point.Level is > 0 ? o.Point.Level.Value : int.MaxValue)
        .ThenBy(o => TravelPlanning.DutyId(o.Point, index) != 0 ? 1 : o.Point.TerritoryId != 0 ? 0 : 2)
        .ThenBy(o => SpawnGrouping.EncounterRank(o.Point.Encounter))
        .ThenByDescending(o => o.IsReported).ThenByDescending(o => o.Point.HasCoordinates)
        .ThenBy(o => o.Point.TerritoryId).ThenBy(o => o.Key, StringComparer.Ordinal);

    public static CaptureOption? LowestReported(IEnumerable<CaptureOption> options, WorldIndex index) =>
        Rank(options.Where(o => o.IsReported && o.Point.Level is > 0), index).FirstOrDefault();

    /// <summary>
    /// First isolate reported, level-ready sources. Among them use common overworld,
    /// duty, FATE, hunt, then unresolved conditions. Level breaks ties WITHIN a tier.
    /// Future/unknown levels stay visible but never displace an available capture source.
    /// Without a known BST level, retain the absolute-lowest-level fallback.
    /// </summary>
    public static IOrderedEnumerable<CaptureOption> RankForPlayer(IEnumerable<CaptureOption> options,
        WorldIndex index, int? bstLevel) => options
        .OrderBy(o => o.IsHintOnly ? 2 : o.IsReported ? 0 : 1)
        .ThenBy(o => EncounterPolicy.IsLevelReady(o.Point, bstLevel) ? 0 : o.Point.Level is > 0 ? 1 : 2)
        .ThenBy(o => EncounterPolicy.IsLevelReady(o.Point, bstLevel) ? (int)EncounterPolicy.Tier(o, index) : 0)
        .ThenBy(o => o.Point.Level is > 0 ? o.Point.Level.Value : int.MaxValue)
        .ThenBy(o => (int)EncounterPolicy.Tier(o, index))
        .ThenByDescending(o => o.Point.HasCoordinates)
        .ThenBy(o => o.Point.TerritoryId).ThenBy(o => o.Key, StringComparer.Ordinal);

    public static CaptureOption? PreferredReported(IEnumerable<CaptureOption> options, WorldIndex index, int? bstLevel) =>
        RankForPlayer(options.Where(o => o.IsReported && o.Point.Level is > 0), index, bstLevel).FirstOrDefault();

    public static CaptureOption? Preferred(IEnumerable<CaptureOption> options, WorldIndex index, int? bstLevel = null)
    {
        var all = options.ToArray();
        var reports = all.Where(o => o.IsReported).ToArray();
        if (reports.Length > 0) return RankForPlayer(reports, index, bstLevel).First();
        // In the absence of a capture report, don't recommend a worldwide lookalike over the actual hint.
        var hinted = all.Where(o => o.MatchesBestiary).ToArray();
        return Rank(hinted.Length > 0 ? hinted : all, index).FirstOrDefault();
    }
}
