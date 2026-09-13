// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using Beastdex.Models;

namespace Beastdex.Services;

/// <summary>Pure matching/routing rules. No monster catalog, guessed levels or capture flags.</summary>
public static class SpawnMatching
{
    public static string NormalizeName(string value)
    {
        var result = new StringBuilder();
        foreach (var c in value.Normalize(NormalizationForm.FormKC))
            result.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        return string.Join(' ', result.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static NameMatchKind MatchName(string familiarName, string mobName)
    {
        var familiar = NormalizeName(familiarName);
        var mob = NormalizeName(mobName);
        if (familiar.Length == 0 || mob.Length == 0) return NameMatchKind.None;
        if (familiar == mob) return NameMatchKind.Exact;
        // Whole phrase only: "antelope stag" may match "stag", but "stag" must
        // not match "stagnant". Never infer a global synonym or capture mapping.
        return familiar.Length >= 3 && (" " + mob + " ").Contains(" " + familiar + " ", StringComparison.Ordinal)
            ? NameMatchKind.NameVariant : NameMatchKind.None;
    }

    public static uint[] ResolveHint(BeastInfo beast, IEnumerable<TerritoryInfo> territories,
        IEnumerable<MapInfo> maps, uint dutyTerritoryId = 0)
    {
        var location = beast.Location;
        if (location.RowId == 0) return [];
        if (location.Key == 2)
            return territories.Where(t => t.DutyId == location.RowId ||
                    (dutyTerritoryId != 0 && t.Id == dutyTerritoryId))
                .Select(t => t.Id).Distinct().Order().ToArray();
        if (location.Key != 1) return [];
        var mapTerritories = maps.Where(m => m.PlaceNameId == location.RowId || m.SubPlaceNameId == location.RowId)
            .Select(m => m.TerritoryId).ToHashSet();
        // An overworld area must not silently select a dungeon/quest instance
        // that happens to use the same PlaceName. Broad region names are not used.
        return territories.Where(t => t.DutyId == 0 &&
                (t.PlaceNameId == location.RowId || mapTerritories.Contains(t.Id)))
            .Select(t => t.Id).Distinct().Order().ToArray();
    }

    public static SpawnCandidate[] Candidates(BeastInfo beast, IReadOnlySet<uint> territories,
        IEnumerable<SpawnPoint> points) => !beast.NameResolved ? [] : points
        .Where(p => territories.Contains(p.TerritoryId))
        .Select(p => new SpawnCandidate(p, MatchName(beast.Name, p.Name)))
        .Where(c => c.Match != NameMatchKind.None)
        .OrderBy(c => c.Match).ThenBy(c => c.Point.Level ?? int.MaxValue)
        .ThenByDescending(c => c.Point.Evidence).ThenBy(c => c.Point.Name).ToArray();

    public static bool ShowCapture(CaptureFilter filter, bool captureAvailable, bool captured) =>
        filter == CaptureFilter.All || (captureAvailable &&
            (filter == CaptureFilter.Captured ? captured : !captured));

    public static bool WithinLevel(int? level, int ceiling, bool includeUnknown) =>
        level.HasValue ? level.Value > 0 && level.Value <= ceiling : includeUnknown;

    public static float MapCoordinate(float world, int sizeFactor, int offset)
    {
        if (sizeFactor <= 0 || !float.IsFinite(world)) return float.NaN;
        // Same formula as Dalamud.Utility.MapUtil.ConvertWorldCoordXZToMapCoord.
        return .02f * (world + offset) + 2048f / sizeFactor + 1f;
    }

    public static float WorldCoordinate(float mapCoordinate, int sizeFactor, int offset)
    {
        if (sizeFactor <= 0 || !float.IsFinite(mapCoordinate)) return float.NaN;
        return (mapCoordinate - 1f - 2048f / sizeFactor) / .02f - offset;
    }

    // Dalamud BattleNpcSubKind.Combatant is 5. Compare the documented
    // value instead of relying on a string representation of the enum.
    public static bool IsUnownedCombatNpc(byte subKind, uint ownerId) =>
        subKind == 5 && ownerId is 0 or 0xE0000000;

    public static bool ValidPosition(float x, float y, float z) =>
        float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) &&
        Math.Abs(x) < 100000 && Math.Abs(y) < 100000 && Math.Abs(z) < 100000;

    public static string[] LayoutPaths(string bg)
    {
        var path = bg.Replace('\\', '/').Trim('/');
        if (path.Length == 0 || path.Contains("..", StringComparison.Ordinal)) return [];
        if (!path.StartsWith("bg/", StringComparison.OrdinalIgnoreCase)) path = "bg/" + path;
        var marker = path.LastIndexOf("/level/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return [];
        var root = path[..(marker + "/level/".Length)];
        // Candidate filenames, not a promise that all exist or contain NPCs.
        return new[] { "planevent.lgb", "planlive.lgb", "planmap.lgb", "pop.lgb" }
            .Select(name => root + name).ToArray();
    }

    public static IEnumerable<TravelDestination> OrderDestinations(IEnumerable<TravelDestination> destinations,
        SpawnPoint? near)
    {
        var sameArea = near == null ? destinations : destinations.Where(d => d.Aetheryte.TerritoryId == near.TerritoryId);
        // Do not compare positions on different map pages. The distance is
        // straight-line, not a traversable route or a reachability guarantee.
        return sameArea.OrderBy(d => near != null && near.MapId != 0 &&
                d.Aetheryte.MapId == near.MapId && d.Aetheryte.X.HasValue && d.Aetheryte.Z.HasValue ? 0 : 1)
            .ThenBy(d => near != null && near.MapId != 0 && d.Aetheryte.MapId == near.MapId &&
                    d.Aetheryte.X.HasValue && d.Aetheryte.Z.HasValue
                ? Math.Pow(d.Aetheryte.X.Value - near.X, 2) + Math.Pow(d.Aetheryte.Z.Value - near.Z, 2)
                : double.MaxValue)
            .ThenBy(d => d.Aetheryte.Name);
    }
}
