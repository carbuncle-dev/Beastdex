// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;

namespace Beastdex.Services;

/// <summary>Pure map and distance policy shared by the list, source details and compact view.</summary>
public static class TravelPlanning
{
    public static uint DutyId(SpawnPoint p, WorldIndex index) => p.DutyId != 0 ? p.DutyId :
        index.Territories.GetValueOrDefault(p.TerritoryId)?.DutyId ?? 0;

    public static uint MapId(SpawnPoint p, WorldIndex index)
    {
        if (p.MapId != 0)
            return index.Maps.TryGetValue(p.MapId, out var map) && map.TerritoryId == p.TerritoryId ? p.MapId : 0;
        if (!index.Territories.TryGetValue(p.TerritoryId, out var t)) return 0;
        var maps = t.MapIds.Where(id => index.Maps.TryGetValue(id, out var m) && m.TerritoryId == t.Id).Distinct().ToArray();
        return maps.Length == 1 ? maps[0] : 0; // Never guess a duty floor.
    }

    public static SpawnPoint NormalizeMap(SpawnPoint p, WorldIndex index) => p with
    { MapId = p.MapId == 0 ? MapId(p, index) : p.MapId, DutyId = DutyId(p, index) };

    public static bool CanFlag(SpawnPoint p, WorldIndex index) => p.HasCoordinates &&
        SpawnMatching.ValidPosition(p.X, p.Y, p.Z) && MapId(p, index) != 0;

    public static TeleportChoice Choose(IEnumerable<TravelDestination> unlocked, SpawnPoint point, WorldIndex index)
    {
        if (DutyId(point, index) != 0) return new(null, null, [], "Open the duty in Duty Finder instead.");
        var choices = unlocked.Where(d => point.TerritoryId != 0 && d.Aetheryte.TerritoryId == point.TerritoryId)
            .DistinctBy(d => (d.Aetheryte.Id, d.SubIndex)).OrderBy(d => d.Aetheryte.Name, StringComparer.Ordinal).ToArray();
        if (choices.Length == 0) return new(null, null, choices, "No unlocked aetheryte in this source's territory.");
        var mapId = MapId(point, index);
        var positioned = point.HasCoordinates && SpawnMatching.ValidPosition(point.X, point.Y, point.Z) && mapId != 0;
        var measured = positioned ? choices.Where(d => d.Aetheryte.MapId == mapId &&
                d.Aetheryte.X is float x && d.Aetheryte.Z is float z && float.IsFinite(x) && float.IsFinite(z))
            .Select(d => (Destination: d, Distance: Math.Sqrt(Math.Pow(d.Aetheryte.X!.Value - point.X, 2) +
                Math.Pow(d.Aetheryte.Z!.Value - point.Z, 2))))
            .OrderBy(d => d.Distance).ThenBy(d => d.Destination.GilCost).ThenBy(d => d.Destination.Aetheryte.Id).ToArray() : [];
        if (measured.Length > 0)
        {
            var best = measured[0];
            var partial = measured.Length < choices.Length;
            return new(best.Destination, best.Distance, choices, partial
                ? "Closest unlocked aetheryte with comparable map coordinates. Some destinations lack comparable positions; straight-line estimate only."
                : "Closest unlocked aetheryte on this map by straight-line distance, not walking distance.");
        }
        if (choices.Length == 1)
            return new(choices[0], null, choices, "Only unlocked aetheryte in this territory; distance is unknown.");
        return new(null, null, choices, positioned
            ? "Aetheryte coordinates are unavailable on this map; choose a destination."
            : "An exact mob position/map is not known. Choose an unlocked destination in the area.");
    }
}
