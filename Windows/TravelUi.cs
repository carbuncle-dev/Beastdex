// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using Beastdex.Models;
using Beastdex.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace Beastdex.Windows;

internal static class TravelUi
{
    private static string Scope(SpawnPoint p) => FormattableString.Invariant($"{p.TerritoryId}:{p.DutyId}:{p.MapId}:{p.NameId}:{p.Level}:{p.X:R}:{p.Z:R}");

    public static void LocationLink(WorldSearchService world, TravelService travel, SpawnPoint point, bool compact = false)
    {
        ImGui.PushID("location:" + Scope(point));
        try { DrawLocationLink(world, travel, point, compact); }
        finally { ImGui.PopID(); }
    }

    private static void DrawLocationLink(WorldSearchService world, TravelService travel, SpawnPoint point, bool compact)
    {
        var mapId = TravelPlanning.MapId(point, world.Index);
        var duty = TravelPlanning.DutyId(point, world.Index);
        var hasPosition = point.HasCoordinates && SpawnMatching.ValidPosition(point.X, point.Y, point.Z);
        var text = world.AreaName(point);
        if (hasPosition && mapId != 0 && world.Index.Maps.TryGetValue(mapId, out var map))
            text += $"  X:{SpawnMatching.MapCoordinate(point.X, map.SizeFactor, map.OffsetX):0.0} Y:{SpawnMatching.MapCoordinate(point.Z, map.SizeFactor, map.OffsetY):0.0}";
        var mapChoices = hasPosition && mapId == 0 && world.Index.Territories.TryGetValue(point.TerritoryId, out var t)
            ? t.MapIds.Where(id => world.Index.Maps.TryGetValue(id, out var m) && m.TerritoryId == point.TerritoryId).ToArray() : [];
        var actionable = hasPosition && (mapId != 0 || mapChoices.Length > 0) || duty != 0;
        var tooltip = hasPosition ? mapId != 0
            ? text + "\nClick to open the game map and place its flag on this recorded position. This is not a live enemy tracker."
            : text + "\nPosition known but floor unresolved; click to select a map explicitly."
            : text + (duty != 0 ? "\nClick to open this duty in Duty Finder. No exact mob position is available." :
                "\nOnly the area is known; no position flag is invented. Use Sources for alternatives or the area NPC browser.");
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Text, compact ? CompactSkin.Gold : UiTheme.Accent);
        var click = UiTheme.AlignedIconButton(hasPosition ? FontAwesomeIcon.MapMarkerAlt : duty != 0 ? FontAwesomeIcon.DoorOpen : FontAwesomeIcon.Globe,
            text, "location", tooltip, actionable);
        ImGui.PopStyleColor(2);
        if (click)
        {
            if (hasPosition && mapId != 0) travel.RequestMap(point, mapId);
            else if (hasPosition && mapChoices.Length > 0) ImGui.OpenPopup("##sourceMapChoices");
            else if (duty != 0) travel.RequestDuty(duty);
        }
        if (!ImGui.BeginPopup("##sourceMapChoices")) return;
        try
        {
            ImGui.TextColored(UiTheme.Gold, "Choose the map / floor");
            ImGui.TextWrapped("The floor is not established. A flag previews the recorded world position on your chosen map, not a verified floor assignment.");
            foreach (var id in mapChoices)
                if (world.Index.Maps.TryGetValue(id, out var selectedMap))
                {
                    if (ImGui.Selectable($"{selectedMap.Name}##map{id}")) travel.RequestMap(point, id);
                    UiTheme.Tooltip($"Flag the recorded position on {selectedMap.Name}. The map/floor is your choice, not a verified assignment.");
                }
        }
        finally { ImGui.EndPopup(); }
    }

    public static void SourceButton(WorldSearchService world, TravelService travel, uint beastId, SpawnPoint point)
    {
        ImGui.PushID("travel:" + Scope(point));
        try { DrawSourceButton(world, travel, point); }
        finally { ImGui.PopID(); }
    }

    private static void DrawSourceButton(WorldSearchService world, TravelService travel, SpawnPoint point)
    {
        var duty = TravelPlanning.DutyId(point, world.Index);
        if (duty != 0)
        {
            if (UiTheme.Button(FontAwesomeIcon.DoorOpen, "Duty Finder", "Open the selected source's duty. Does not queue or change party settings."))
                travel.RequestDuty(duty);
            return;
        }
        var choice = TravelPlanning.Choose(travel.UnlockedDestinations, point, world.Index);
        var enabled = travel.LifestreamAvailable && (choice.Destination != null || choice.NeedsChoice);
        var destination = choice.Destination;
        var name = destination != null ? UiTheme.Fit(destination.Aetheryte.Name,
            Math.Max(55 * UiTheme.Scale, ImGui.GetContentRegionAvail().X - 45 * UiTheme.Scale)) : "Choose aetheryte";
        var tooltip = !travel.LifestreamAvailable ? travel.Status : choice.Reason;
        if (destination != null)
            tooltip += $"\nTeleport to {destination.Aetheryte.Name} via Lifestream / {destination.GilCost:N0} gil." +
                (choice.Distance.HasValue ? $"\nApproximately {choice.Distance.Value:0} world yalms from the recorded mob position." : "") +
                "\nNormal travel restrictions apply. No automatic walking follows.";
        if (UiTheme.Button(FontAwesomeIcon.LocationArrow, name + "##sourceTravel", tooltip, enabled))
        {
            if (destination != null) travel.RequestTeleport(destination);
            else ImGui.OpenPopup("##teleportChoices");
        }
        // An area selector is the fallback only; a known position has a one-click default.
        if (!ImGui.BeginPopup("##teleportChoices")) return;
        try
        {
            ImGui.TextColored(UiTheme.Accent, "Area destinations");
            ImGui.TextWrapped(choice.Reason);
            foreach (var d in choice.Choices)
            {
                if (ImGui.Selectable($"{d.Aetheryte.Name} / {d.GilCost:N0} gil##tp{d.Aetheryte.Id}:{d.SubIndex}"))
                    travel.RequestTeleport(d);
                UiTheme.Tooltip($"Teleport to {d.Aetheryte.Name} via Lifestream / {d.GilCost:N0} gil. No automatic walking follows.");
            }
        }
        finally { ImGui.EndPopup(); }
    }

    public static void Evidence(CaptureOption option, bool alignWithIcons = false)
    {
        var help = option.IsHintOnly ? "The game's area/duty hint. It does not supply an exact position or enemy level."
            : (option.Group != null ? string.Join(" + ", option.Group.EvidenceLabels) + "\n" : "") +
              (option.IsCandidate ? "Name similarity is not proof that this enemy can be captured." :
                  "Explicit community capture report; not independently verified by the game API.") +
              (option.MatchesBestiary ? "\nMatches the in-game bestiary's area/duty hint." : "");
        var indent = UiIconLayout.TextOffset(UiTheme.Scale);
        if (alignWithIcons) ImGui.Indent(indent);
        try
        {
            UiTheme.TextFit(option.EvidenceLabel,
                option.IsCandidate ? UiTheme.Gold : option.IsHintOnly ? UiTheme.Muted : UiTheme.Accent, help);
        }
        finally { if (alignWithIcons) ImGui.Unindent(indent); }
    }
}
