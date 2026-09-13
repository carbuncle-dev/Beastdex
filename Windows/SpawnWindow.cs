// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using Beastdex.Models;
using Beastdex.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace Beastdex.Windows;

public sealed class SpawnWindow : Window
{
    private readonly WorldSearchService world;
    private readonly TravelService travel;
    private readonly BestiaryPlanService plans;
    private readonly System.Action showCompact;
    private BeastInfo? beast;
    private bool showCandidates;
    private bool browseArea;
    private string search = "";

    public SpawnWindow(WorldSearchService world, TravelService travel, BestiaryPlanService plans, System.Action showCompact)
        : base("Beastdex - Capture sources###BeastdexSourcesV7")
    {
        this.world = world; this.travel = travel; this.plans = plans; this.showCompact = showCompact;
        DisableFadeInFadeOut = true;
        Size = new Vector2(1080, 580); SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        { MinimumSize = new Vector2(900, 380), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
    }
    public void Select(BeastInfo selected)
    {
        beast = selected; showCandidates = false; browseArea = false; search = ""; IsOpen = true;
    }

    public override void Draw()
    {
        UiTheme.Frame();
        if (beast == null) return;
        plans.Update();
        var plan = plans.Get(beast.RowId);
        if (plan == null)
        {
            ImGui.TextWrapped("Waiting for bestiary metadata.");
            UiTheme.Tooltip("The selected familiar's game data is not ready yet. Automatic refresh continues in the background.");
            return;
        }
        beast = plan.Beast;
        UiTheme.Heading(FontAwesomeIcon.MapMarkedAlt, $"{beast.Name.ToUpperInvariant()} / CAPTURE SOURCES");
        ImGui.TextDisabled($"At or below BST {world.BstLevel?.ToString() ?? "?"}: common > duty > FATE > hunt");
        UiTheme.Tooltip("Only reported sources at or below your actual BST level can be level-ready. Prefer common overworld mobs, then dungeon/trial mobs, then FATE mobs, then elite hunts. Within each tier, prefer the lowest enemy level. Future levels, unknown conditions and unverified candidates stay available below the known eligible choices. Alternatives are not hidden by the main table's level filter.");
        var knownHintPositions = plan.Options.Any(o => o.MatchesBestiary && o.Point.HasCoordinates);
        if (knownHintPositions) browseArea = false;
        ImGui.SetNextItemWidth(300 * UiTheme.Scale);
        ImGui.InputTextWithHint("##sourceSearch", "Search enemy, area or encounter...", ref search, 128);
        UiTheme.Tooltip("Filter sources by enemy, area, encounter type or NPC name ID. Clear the search to show all options.");
        ImGui.SameLine(); ImGui.Checkbox("Unverified alternatives", ref showCandidates);
        UiTheme.Tooltip("Name-only matches outside the hinted area are not assumed capturable.");
        ImGui.SameLine();
        if (UiTheme.IconButton("copySources", FontAwesomeIcon.Copy, "Copy source and indexing diagnostics."))
            ImGui.SetClipboardText(world.GetDiagnostics());
        ImGui.SameLine();
        if (UiTheme.IconButton("compact", FontAwesomeIcon.Compress, "Show the compact tracker and hide the other plugin panels."))
        { showCompact(); return; }
        // Show the broad NPC browser only as a fallback when the hinted region has no matched positions.
        if (!knownHintPositions && world.GetTerritories(beast.RowId).Length > 0)
        {
            ImGui.Checkbox("No hinted spawn position yet - browse the area's NPC groups", ref browseArea);
            UiTheme.Tooltip("Fallback only: these NPCs are not assigned to this familiar. A known source normally goes directly to its location, without this broad list.");
        }
        if (browseArea) { DrawAreaBrowser(); return; }
        var query = search.Trim();
        var options = SourceReconciler.RankForPlayer(plan.Options.Where(o =>
            (!o.IsCandidate || showCandidates || o.MatchesBestiary) && (Matches(o.Point, query) ||
                EncounterPresentation.Describe(o, world.Index).Label.Contains(query, StringComparison.OrdinalIgnoreCase))), world.Index, world.BstLevel).ToArray();
        // No variable-height progress line. Detailed merge/index progress lives in Settings / Diagnostics.
        ImGui.TextDisabled($"{options.Length} source option(s). Same-level local sightings share one representative location.");
        UiTheme.Tooltip("Options after the source filters. Nearby observations with matching identity, level, map and encounter share a group; distant populations remain separate.");
        if (options.Length == 0)
        {
            ImGui.TextWrapped("No resolved options at this filter. Enable community capture reports in settings, clear the search, or inspect the hinted area's NPCs when no positions are available.");
            return;
        }
        var height = Math.Max(140 * UiTheme.Scale, ImGui.GetContentRegionAvail().Y - 34 * UiTheme.Scale);
        if (ImGui.BeginTable("##unifiedSourcesV084", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp, new Vector2(0, height)))
        {
            try
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("Enemy / source", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 110 * UiTheme.Scale);
                ImGui.TableSetupColumn("Mob Lv.", ImGuiTableColumnFlags.WidthFixed, 60 * UiTheme.Scale);
                ImGui.TableSetupColumn("Location / map flag", ImGuiTableColumnFlags.WidthStretch, 1.3f);
                ImGui.TableSetupColumn("Evidence", ImGuiTableColumnFlags.WidthStretch, .9f);
                ImGui.TableSetupColumn("Travel", ImGuiTableColumnFlags.WidthFixed, 175 * UiTheme.Scale);
                UiTheme.TableHeaders(
                    ("Enemy / source", "Enemy name and recommendation status. A hint is not an exact spawn or a confirmed enemy."),
                    ("Type", "Game icon and encounter category: common overworld, dungeon, trial, FATE, hunt or unresolved."),
                    ("Mob Lv.", "This enemy's reported or observed level, not the duty entry requirement or level-sync cap."),
                    ("Location / map flag", "Click a positioned source to place a map flag. Area-only duty sources open Duty Finder."),
                    ("Evidence", "Where the source data came from. Expand Details for conditions, grouping and NPC identities."),
                    ("Travel", "Teleport via Lifestream or open this source's duty. No automatic walking or queuing."));
                for (var i = 0; i < options.Length; i++) DrawOption(options[i], i, options[i].Key == plan.Preferred?.Key);
            }
            finally { ImGui.EndTable(); }
        }
        if (travel.LastAction.Length > 0) UiTheme.TextFit(travel.LastAction, UiTheme.Muted);
    }

    private bool Matches(SpawnPoint p, string query) => query.Length == 0 ||
        p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || world.AreaName(p).Contains(query, StringComparison.OrdinalIgnoreCase) ||
        p.EncounterLabel.Contains(query, StringComparison.OrdinalIgnoreCase) || p.NameId.ToString().Contains(query);

    private void DrawOption(CaptureOption option, int i, bool preferred)
    {
        var p = option.Point;
        ImGui.PushID(option.Key);
        try
        {
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0);
            UiTheme.TextFit(option.IsHintOnly ? "Bestiary area hint" : p.Name, tooltip:
                option.IsHintOnly ? "The bestiary supplies this area/duty hint, but not a specific capture enemy or position." :
                    $"Capture enemy: {p.Name}. Familiar: {beast!.Name}. Check the type, level and evidence before travelling.");
            if (preferred)
            {
                UiTheme.Badge(EncounterPolicy.RecommendationLabel(option, world.BstLevel),
                    option.IsReported && EncounterPolicy.IsLevelReady(p, world.BstLevel) ? UiTheme.Green : UiTheme.Gold);
                UiTheme.Tooltip(EncounterPolicy.RecommendationReason(option, world.Index, world.BstLevel));
            }
            else UiTheme.Badge(option.IsHintOnly ? "HINT" : option.IsReported ? "ALTERNATIVE" : "CANDIDATE", UiTheme.Muted,
                option.IsHintOnly ? "Original bestiary hint; enemy identity or level remains unresolved." : option.IsReported ?
                "Another reported capture source. The recommendation depends on your BST level and encounter convenience." :
                "Possible name match, not a verified capture source. It cannot displace an explicit capture report.");
            ImGui.TableSetColumnIndex(1);
            EncounterIcons.Draw(option, world.Index);
            ImGui.TableSetColumnIndex(2);
            UiTheme.Badge(p.LevelText, !p.Level.HasValue ? UiTheme.Muted : p.Level <= world.BstLevel ? UiTheme.Green : UiTheme.Gold,
                p.Level.HasValue ? $"Enemy level {p.Level}; your BST level is {world.BstLevel?.ToString() ?? "unknown"}. This is not a duty entry or sync level." :
                "No reliable enemy level is available. Unknown is never treated as level zero or automatically catchable.");
            ImGui.TableSetColumnIndex(3);
            TravelUi.LocationLink(world, travel, p);
            ImGui.TextDisabled(p.HasCoordinates ? "Recorded group position" : "Area only - no invented pin");
            UiTheme.Tooltip(p.HasCoordinates ? "An actual recorded point representing this group. It may be a roaming position, not a guaranteed spawn point." :
                "The area is known but there is no reliable coordinate. No map flag will be invented.");
            ImGui.TableSetColumnIndex(4);
            TravelUi.Evidence(option);
            var details = ImGui.TreeNode("Details");
            UiTheme.Tooltip("Expand encounter conditions, evidence, position-group size and source identity diagnostics.");
            if (details)
            {
                ImGui.TextWrapped(p.Conditions);
                UiTheme.Tooltip("Reported encounter restrictions or uncertainty for this source.");
                if (option.Group is { } g)
                {
                    ImGui.TextWrapped(string.Join(" + ", g.EvidenceLabels));
                    UiTheme.Tooltip("Evidence sources contributing to this merged option.");
                    ImGui.TextWrapped(g.Representative.CaptureAttribution);
                    UiTheme.Tooltip("Attribution for the capture report, separate from position evidence.");
                    ImGui.TextWrapped($"{g.SampleCount} position records (not an enemy count), radius ~{g.Radius:0} yalms.");
                    UiTheme.Tooltip("Recorded samples and approximate spatial extent. Repeated sightings do not count as extra enemies.");
                }
                ImGui.TextWrapped($"NPC name ID: {p.NameId}; possible IDs: {string.Join(", ", p.PossibleNameIds)}");
                UiTheme.Tooltip("Game-sheet IDs used to reconcile reports with local NPCs; duplicate display names can have multiple IDs.");
                ImGui.TextWrapped($"Tier: {EncounterPolicy.Tier(option, world.Index)}; level-ready: {EncounterPolicy.IsLevelReady(p, world.BstLevel)}");
                UiTheme.Tooltip(EncounterPolicy.RecommendationReason(option, world.Index, world.BstLevel));
                ImGui.TextWrapped($"Territory {p.TerritoryId}; map {p.MapId}; duty {TravelPlanning.DutyId(p, world.Index)}");
                UiTheme.Tooltip("Game IDs used for travel and map matching. Zero means not applicable or unresolved.");
                ImGui.TextWrapped("Position evidence and explicit source reports are not independent game verification of capture eligibility.");
                ImGui.TreePop();
            }
            ImGui.TableSetColumnIndex(5); TravelUi.SourceButton(world, travel, beast!.RowId, p);
        }
        finally { ImGui.PopID(); }
    }

    private void DrawAreaBrowser()
    {
        ImGui.TextColored(UiTheme.Gold, "Fallback NPC browser - these are NOT verified capture sources.");
        UiTheme.Tooltip("Broad search of NPC groups in the hinted region, not a list of confirmed captures for this familiar.");
        var rows = world.GetAreaGroups(beast!.RowId).Where(g => Matches(g.Point, search.Trim())).Take(200).ToArray();
        ImGui.TextDisabled($"{rows.Length} grouped result(s), capped at 200. Search to narrow the hinted region.");
        UiTheme.Tooltip("A maximum of 200 matching groups is displayed. Use the source search field to narrow the results.");
        if (ImGui.BeginTable("##areaFallback", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.Resizable, new Vector2(0, Math.Max(100, ImGui.GetContentRegionAvail().Y - 25 * UiTheme.Scale))))
        {
            ImGui.TableSetupColumn("NPC"); ImGui.TableSetupColumn("Mob Lv.");
            ImGui.TableSetupColumn("Position"); ImGui.TableSetupColumn("Travel");
            UiTheme.TableHeaders(("NPC", "An indexed NPC, not necessarily a capture source for this familiar."),
                ("Mob Lv.", "Observed or recorded enemy level."),
                ("Position", "Click to flag a recorded representative position."),
                ("Travel", "Teleport or open the duty for this NPC's location."));
            for (var i = 0; i < rows.Length; i++)
            {
                var p = rows[i].Point; ImGui.PushID(i);
                ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); UiTheme.TextFit(p.Name);
                ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(p.LevelText);
                UiTheme.Tooltip("Enemy level from the indexed observation or layout. This does not prove capture eligibility.");
                ImGui.TableSetColumnIndex(2); TravelUi.LocationLink(world, travel, p);
                ImGui.TableSetColumnIndex(3); TravelUi.SourceButton(world, travel, beast.RowId, p);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
    }
}
