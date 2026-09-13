// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using Beastdex.Models;
using Beastdex.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace Beastdex.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly BeastDataService beasts;
    private readonly XbmCapturedService captured;
    private readonly WorldSearchService world;
    private readonly TravelService travel;
    private readonly BestiaryPlanService plans;
    private string search = "";
    public SpawnWindow SpawnWindow { get; }
    public CompactWindow CompactWindow { get; }
    public SettingsWindow SettingsWindow { get; }

    public MainWindow(BeastDataService beasts, XbmCapturedService captured, WorldSearchService world,
        TravelService travel, BestiaryPlanService plans, System.Action reloadMetadata)
        : base("Beastdex - Field guide###BeastdexMainV7")
    {
        this.beasts = beasts; this.captured = captured; this.world = world; this.travel = travel; this.plans = plans;
        SpawnWindow = new SpawnWindow(world, travel, plans, ShowCompact);
        SettingsWindow = new SettingsWindow(beasts, captured, world, travel, reloadMetadata, ShowCompact);
        CompactWindow = new CompactWindow(plans, captured, world, travel, ShowSources, ShowFull,
            ShowSettings, HideOtherWindowsForCompact);
        DisableFadeInFadeOut = true;
        Size = new Vector2(1060, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        { MinimumSize = new Vector2(900, 410), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
    }
    public void Dispose() { }
    public override void OnOpen() => ForceRefresh();
    public void ForceRefresh() => captured.RequestRefresh();
    public void ShowFull()
    {
        if (Plugin.Configuration.PreferCompact)
        { Plugin.Configuration.PreferCompact = false; Plugin.SaveConfiguration(); }
        CompactWindow.IsOpen = false; IsOpen = true;
    }
    public void ShowCompact()
    {
        if (!Plugin.Configuration.PreferCompact)
        { Plugin.Configuration.PreferCompact = true; Plugin.SaveConfiguration(); }
        HideOtherWindowsForCompact();
        CompactWindow.IsOpen = true;
    }
    private void HideOtherWindowsForCompact()
    {
        IsOpen = false;
        SpawnWindow.IsOpen = false;
        SettingsWindow.IsOpen = false;
    }
    public void ShowSettings()
    {
        // Auxiliary panels may coexist with the HUD. Only ShowFull or the HUD's X closes it.
        SettingsWindow.IsOpen = true;
    }
    private void ShowSources(BeastInfo beast)
    {
        // Keep the tracker visible (and its current arrow selection) while inspecting alternatives.
        SettingsWindow.IsOpen = false;
        SpawnWindow.Select(beast);
    }
    public void OpenPreferred()
    {
        if (Plugin.Configuration.PreferCompact) ShowCompact();
        else ShowFull();
    }
    public void TogglePreferred()
    {
        // A repeated main-UI action must not dismiss an open tracker or its auxiliary panels.
        if (Plugin.Configuration.PreferCompact && CompactWindow.IsOpen) return;
        else if (!Plugin.Configuration.PreferCompact && IsOpen) IsOpen = false;
        else OpenPreferred();
    }

    public override void Draw()
    {
        UiTheme.Frame();
        plans.Update();
        var snapshot = captured.Snapshot;
        DrawHeader(snapshot);
        if (!IsOpen) return; // A compact switch in the header must not draw the old guide below it.
        DrawFilters();
        var obtained = snapshot.CapturedRowIds.ToHashSet();
        var cfg = Plugin.Configuration;
        var ceiling = cfg.FollowBstLevel ? world.BstLevel : cfg.MaximumMobLevel;
        var query = search.Trim();
        var visible = plans.Plans.Where(p => SpawnMatching.ShowCapture(cfg.CaptureFilter, snapshot.Available,
                obtained.Contains(p.Beast.RowId)))
            .Where(p => query.Length == 0 || p.Beast.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Options.Any(o => o.Point.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    world.AreaName(o.Point).Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Where(p => !cfg.FilterByLevel || !ceiling.HasValue ||
                SpawnMatching.WithinLevel(SourceReconciler.PreferredReported(p.Options, world.Index, world.BstLevel)?.Point.Level,
                    ceiling.Value, cfg.IncludeUnknownLevels));
        var rows = CatchPlanner.Sort(visible, cfg.Sort, snapshot, world.BstLevel, world.Index);
        if (!snapshot.Available)
        {
            ImGui.TextColored(UiTheme.Gold, "Capture status unavailable - '?' does not mean missing.");
            UiTheme.Tooltip(snapshot.Status);
        }
        // Background regrouping is deliberately silent here: do not insert/remove rows above the table.
        var rowHeight = Math.Max(100 * UiTheme.Scale, ImGui.GetContentRegionAvail().Y - 66 * UiTheme.Scale);
        if (ImGui.BeginTable("##fieldGuide", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp, new Vector2(0, rowHeight)))
        {
            try
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("Got", ImGuiTableColumnFlags.WidthFixed, 28 * UiTheme.Scale);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 52 * UiTheme.Scale);
                ImGui.TableSetupColumn("Familiar", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("Mob Lv.", ImGuiTableColumnFlags.WidthFixed, 60 * UiTheme.Scale);
                ImGui.TableSetupColumn("Best source / location", ImGuiTableColumnFlags.WidthStretch, 1.5f);
                ImGui.TableSetupColumn("Travel / sources", ImGuiTableColumnFlags.WidthFixed, 183 * UiTheme.Scale);
                UiTheme.TableHeaders(
                    ("Got", "Read-only capture status: checkmark = obtained, square = missing, ? = unavailable."),
                    ("", "Familiar artwork. Hover an image for a larger preview."),
                    ("Familiar", "The familiar in your bestiary, which may have a different name from its capture enemy."),
                    ("Mob Lv.", "Recommended enemy level. A separate min value shows the lowest reported alternative, not a duty entry level."),
                    ("Best source / location", "Preferred capture enemy and location. Click a known position to place a map flag; hover the type icon for encounter details."),
                    ("Travel / sources", "Teleport via Lifestream or open Duty Finder. Sources shows all alternative capture methods."));
                foreach (var row in rows) DrawRow(row, snapshot.Available, obtained.Contains(row.Beast.RowId));
            }
            finally { ImGui.EndTable(); }
        }
        UiTheme.SyncStatus(captured);
        ImGui.SameLine(); ImGui.TextDisabled($"| {rows.Length} shown");
        UiTheme.Tooltip("Number of familiars left after the search, collection and level filters.");
        if (travel.LastAction.Length > 0) UiTheme.TextFit(travel.LastAction, UiTheme.Muted);
        else UiTheme.TextFit("Click a location to flag it. Source eligibility is community-reported; level-ready does not verify duty access.", UiTheme.Muted);
    }

    private void DrawHeader(XbmCaptureSnapshot snapshot)
    {
        if (ImGui.BeginTable("##header", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("title", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("actions", ImGuiTableColumnFlags.WidthFixed, 115 * UiTheme.Scale);
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0);
            UiTheme.Heading(FontAwesomeIcon.Paw, "BEASTMASTER / FIELD GUIDE");
            ImGui.TextColored(UiTheme.Green, snapshot.Available ? $"{snapshot.CapturedRowIds.Count} / {beasts.Beasts.Count} obtained" : "Collection pending");
            UiTheme.Tooltip(snapshot.Available ? $"{snapshot.CapturedRowIds.Count} obtained out of {beasts.Beasts.Count} familiar entries. Read automatically from this character's bestiary." : snapshot.Status);
            ImGui.SameLine(); ImGui.TextColored(UiTheme.Gold, $"{plans.Next.Length} level-ready");
            UiTheme.Tooltip("Missing familiars with an explicit reported source at or below your current BST level. Unknown levels and name-only matches are excluded.");
            ImGui.SameLine(); ImGui.TextDisabled($"BST {world.BstLevel?.ToString() ?? "?"}");
            UiTheme.Tooltip("Your stored Beastmaster job level. Source preferences and the tracker always use this, not a manual guide filter.");
            ImGui.TableSetColumnIndex(1);
            if (UiTheme.IconButton("compact", FontAwesomeIcon.Compress, "Switch to the compact next-catch tracker.")) ShowCompact();
            ImGui.SameLine();
            if (UiTheme.IconButton("refresh", FontAwesomeIcon.Sync, "Refresh character capture state now (no public download or world reindex).")) ForceRefresh();
            ImGui.SameLine();
            if (UiTheme.IconButton("settings", FontAwesomeIcon.Cog, "Appearance, auto-refresh, source data and diagnostics.")) ShowSettings();
            ImGui.EndTable();
        }
        ImGui.ProgressBar(snapshot.Available && beasts.Beasts.Count > 0 ?
            (float)snapshot.CapturedRowIds.Count / beasts.Beasts.Count : 0, new Vector2(-1, 5 * UiTheme.Scale), "");
        UiTheme.Tooltip(snapshot.Available ? $"Collection progress: {snapshot.CapturedRowIds.Count} / {beasts.Beasts.Count} obtained." : snapshot.Status);
        if (!Plugin.Configuration.UseCommunityCaptureReports)
        {
            if (UiTheme.Button(FontAwesomeIcon.CloudDownloadAlt, "Enable source levels and alternatives",
                "Opt in to FFXIV Collect capture reports and Teamcraft positions. Cached locally; no character data is uploaded."))
            {
                Plugin.Configuration.UseCommunityCaptureReports = true;
                Plugin.Configuration.UseCommunitySpawnObservations = true;
                Plugin.SaveConfiguration(); world.RequestIndex(refreshPublic: true);
            }
        }
    }

    private void DrawFilters()
    {
        var cfg = Plugin.Configuration;
        ImGui.SetNextItemWidth(235 * UiTheme.Scale);
        ImGui.InputTextWithHint("##search", "Search familiar, enemy or area...", ref search, 128);
        UiTheme.Tooltip("Filter by familiar, capture-enemy or area name. Clear the text to restore the full list.");
        foreach (var filter in Enum.GetValues<CaptureFilter>())
        {
            ImGui.SameLine();
            var selected = cfg.CaptureFilter == filter;
            if (selected) ImGui.PushStyleColor(ImGuiCol.Button, UiTheme.Selected);
            if (ImGui.Button(filter == CaptureFilter.Captured ? "Obtained" : filter.ToString()))
            { cfg.CaptureFilter = filter; Plugin.SaveConfiguration(); }
            if (selected) ImGui.PopStyleColor();
            UiTheme.Tooltip(filter switch
            {
                CaptureFilter.Captured => "Show only familiars the game confirms you have obtained.",
                CaptureFilter.Missing => "Show only familiars the game confirms you have not obtained. Unknown capture state is not treated as missing.",
                _ => "Show both obtained and missing familiars, subject to your other filters.",
            });
        }
        ImGui.SameLine(); ImGui.SetNextItemWidth(194 * UiTheme.Scale);
        var sortOpen = ImGui.BeginCombo("##sort", SortName(cfg.Sort));
        UiTheme.Tooltip("Choose the field guide's order. Recommendations stay convenience-first; Lowest level compares the minimum across all sources.");
        if (sortOpen)
        {
            foreach (var sort in Enum.GetValues<BestiarySort>())
            {
                if (ImGui.Selectable(SortName(sort), sort == cfg.Sort)) { cfg.Sort = sort; Plugin.SaveConfiguration(); }
                UiTheme.Tooltip(SortHelp(sort));
            }
            ImGui.EndCombo();
        }

        var filterLevel = cfg.FilterByLevel;
        if (ImGui.Checkbox("Only within level", ref filterLevel)) { cfg.FilterByLevel = filterLevel; Plugin.SaveConfiguration(); }
        UiTheme.Tooltip("Filter by the preferred source's mob level using the ceiling beside this control. Does not change source preferences or the compact queue.");
        ImGui.SameLine();
        if (cfg.FollowBstLevel) ImGui.TextDisabled($"BST ceiling: {world.BstLevel?.ToString() ?? "unknown"}");
        else ImGui.TextDisabled($"Manual ceiling: {cfg.MaximumMobLevel}");
        UiTheme.Tooltip("The maximum mob level allowed by this guide filter. Choose BST or a manual limit in Settings; recommendations still use your actual BST level.");
        ImGui.SameLine();
        var unknown = cfg.IncludeUnknownLevels;
        if (ImGui.Checkbox("Keep unknown", ref unknown)) { cfg.IncludeUnknownLevels = unknown; Plugin.SaveConfiguration(); }
        UiTheme.Tooltip("Unknown capture levels are never treated as zero or included in the compact level-ready queue.");
    }

    private static string SortName(BestiarySort sort) => sort switch
    {
        BestiarySort.NextCatch => "Next catch (preferred)", BestiarySort.LowestLevel => "Lowest level (all sources)",
        BestiarySort.Name => "Familiar name", _ => "Bestiary number",
    };

    private static string SortHelp(BestiarySort sort) => sort switch
    {
        BestiarySort.NextCatch => "Missing and level-ready first. Order familiars by their preferred enemy level; eligible common overworld sources beat duties, FATEs, then hunts.",
        BestiarySort.LowestLevel => "Compare the lowest reported enemy level across every source, even when the preferred source is a more convenient higher-level overworld enemy.",
        BestiarySort.Name => "Order by localized familiar name.",
        _ => "Order by the familiar's in-game bestiary number.",
    };

    private void DrawRow(BeastPlan plan, bool available, bool obtained)
    {
        var beast = plan.Beast;
        var option = plan.Preferred;
        ImGui.PushID((int)beast.RowId);
        try
        {
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0);
            UiTheme.Icon(!available ? FontAwesomeIcon.QuestionCircle : obtained ? FontAwesomeIcon.CheckSquare : FontAwesomeIcon.Square,
                !available ? UiTheme.Gold : obtained ? UiTheme.Green : UiTheme.Muted);
            UiTheme.Tooltip(!available ? "Capture state unavailable." : obtained ? "Obtained - read from the game." : "Not obtained.");
            ImGui.TableSetColumnIndex(1); BeastImages.Draw(beast);
            ImGui.TableSetColumnIndex(2);
            UiTheme.TextFit(beast.Name, obtained ? UiTheme.Green : null, $"{beast.Name} / bestiary #{beast.RowId}. The enemy's capture name appears in the source column.");
            if (plans.Next.FirstOrDefault()?.BeastId == beast.RowId)
                UiTheme.Badge("NEXT CATCH", UiTheme.Gold, "First missing, level-ready familiar in recommended-level order. The compact tracker starts here.");
            else
            {
                ImGui.TextDisabled($"Bestiary #{beast.RowId}");
                UiTheme.Tooltip("This familiar's row number in the game's bestiary, not the enemy's level.");
            }
            ImGui.TableSetColumnIndex(3);
            var level = option?.Point.Level;
            UiTheme.Badge(level?.ToString() ?? "?", option?.IsCandidate == true ? UiTheme.Gold :
                level.HasValue && world.BstLevel.HasValue ? level <= world.BstLevel ? UiTheme.Green : UiTheme.Red : UiTheme.Muted);
            UiTheme.Tooltip(level.HasValue ? "This source's mob level, not a duty entry/sync requirement." : "No reliable source level is available.");
            var absoluteMinimum = SourceReconciler.LowestReported(plan.Options, world.Index)?.Point.Level;
            if (absoluteMinimum.HasValue && absoluteMinimum != level)
            {
                ImGui.TextDisabled($"min {absoluteMinimum}");
                UiTheme.Tooltip("Lowest reported level across all alternatives. The convenience preference can select a higher-level enemy that is still at or below your BST. Lowest level (all sources) sorts by this minimum.");
            }
            ImGui.TableSetColumnIndex(4);
            if (option != null)
            {
                TravelUi.LocationLink(world, travel, option.Point);
                EncounterIcons.Draw(option, world.Index);
                if (!option.IsHintOnly)
                {
                    ImGui.SameLine(0, 7 * UiTheme.Scale);
                    UiTheme.TextFit(option.Point.Name, UiTheme.Muted, "Capture enemy: " + option.Point.Name + "\n" + EncounterPolicy.RecommendationReason(option, world.Index, world.BstLevel));
                }
                TravelUi.Evidence(option, alignWithIcons: true);
            }
            else
            {
                ImGui.TextDisabled("No source resolved");
                UiTheme.Tooltip("No usable location is indexed yet. Enable capture reports in Settings or inspect Sources for more evidence.");
            }
            ImGui.TableSetColumnIndex(5);
            if (option != null) TravelUi.SourceButton(world, travel, beast.RowId, option.Point);
            var count = plan.Options.Count(o => !o.IsCandidate || o.MatchesBestiary);
            if (UiTheme.Button(FontAwesomeIcon.List, $"Sources ({count})", "See merged bestiary/community sources and other level/location options."))
                ShowSources(beast);
        }
        finally { ImGui.PopID(); }
    }
}
