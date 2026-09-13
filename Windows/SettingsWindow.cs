// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using Beastdex.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace Beastdex.Windows;

public sealed class SettingsWindow : Window
{
    private readonly BeastDataService beasts;
    private readonly XbmCapturedService captured;
    private readonly WorldSearchService world;
    private readonly TravelService travel;
    private readonly System.Action reloadMetadata;
    private readonly System.Action showCompact;
    public SettingsWindow(BeastDataService beasts, XbmCapturedService captured, WorldSearchService world,
        TravelService travel, System.Action reloadMetadata, System.Action showCompact) : base("Beastdex - Settings###BeastdexSettingsV7")
    {
        this.beasts = beasts; this.captured = captured; this.world = world; this.travel = travel; this.reloadMetadata = reloadMetadata;
        this.showCompact = showCompact;
        DisableFadeInFadeOut = true;
        Size = new Vector2(600, 560); SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        { MinimumSize = new Vector2(460, 380), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
    }
    public override void Draw()
    {
        UiTheme.Frame();
        if (UiTheme.Button(FontAwesomeIcon.Compress, "Back to compact", "Hide auxiliary panels; keep the tracker."))
        { showCompact(); return; }
        if (!ImGui.BeginTabBar("##settingsTabs")) return;
        try
        {
            var general = ImGui.BeginTabItem("General");
            UiTheme.Tooltip("Bestiary sync, appearance, compact layout and enemy markers.");
            if (general)
            { try { DrawGeneral(); } finally { ImGui.EndTabItem(); } }
            var data = ImGui.BeginTabItem("Data");
            UiTheme.Tooltip("Choose, refresh or clear the local and community source data.");
            if (data)
            { try { DrawData(); } finally { ImGui.EndTabItem(); } }
            var diagnostics = ImGui.BeginTabItem("Diagnostics");
            UiTheme.Tooltip("Detailed loading status and a copyable troubleshooting report.");
            if (diagnostics)
            { try { DrawDiagnostics(); } finally { ImGui.EndTabItem(); } }
        }
        finally { ImGui.EndTabBar(); }
    }

    private void DrawGeneral()
    {
        var cfg = Plugin.Configuration;
        var changed = false;
        UiTheme.Heading(FontAwesomeIcon.Sync, "BESTIARY");
        var initialize = cfg.AutoInitializeBestiary;
        if (ImGui.Checkbox("Load bestiary after login", ref initialize))
        { cfg.AutoInitializeBestiary = initialize; changed = true; }
        UiTheme.Tooltip("If captures are not loaded, briefly open the bestiary once while idle. Never closes a menu you were using.");
        var auto = cfg.AutoRefreshBestiary;
        if (ImGui.Checkbox("Auto-refresh captures", ref auto))
        { cfg.AutoRefreshBestiary = auto; changed = true; if (auto) captured.RequestRefresh(); }
        UiTheme.Tooltip("Works with plugin windows closed. Does not download or reindex location data.");
        ImGui.SameLine(); ImGui.SetNextItemWidth(130 * UiTheme.Scale);
        var seconds = cfg.BestiaryRefreshSeconds;
        if (ImGui.SliderInt("##refreshInterval", ref seconds, 1, 30, "%d sec"))
        { cfg.BestiaryRefreshSeconds = seconds; changed = true; }
        UiTheme.Tooltip("Seconds between capture-state checks while auto-refresh is enabled. Does not reopen the bestiary each time.");
        if (UiTheme.Button(FontAwesomeIcon.BookOpen, "Initialize", "Request a one-time bestiary initialization while idle, only if capture data is missing.\n" + captured.InitializationStatus)) captured.RequestInitialization();
        ImGui.SameLine();
        if (UiTheme.Button(FontAwesomeIcon.Sync, "Refresh", "Re-read this character's capture state now. No community download or location rebuild.\n" + captured.Snapshot.Status)) captured.RequestRefresh();

        ImGui.Separator();
        UiTheme.Heading(FontAwesomeIcon.Adjust, "APPEARANCE");
        var opacity = cfg.WindowOpacity; ImGui.SetNextItemWidth(180 * UiTheme.Scale);
        if (ImGui.SliderFloat("Opacity", ref opacity, .65f, 1f, "%.2f")) { cfg.WindowOpacity = opacity; changed = true; }
        UiTheme.Tooltip("Background opacity for all plugin windows. Does not change the game's UI or the enemy marker size.");
        var compact = cfg.PreferCompact;
        if (ImGui.Checkbox("Start in compact view", ref compact)) { cfg.PreferCompact = compact; changed = true; }
        UiTheme.Tooltip("Choose the default view when opening the plugin. Sources and Settings can stay open alongside the compact tracker.");
        var locked = cfg.LockCompactPosition;
        if (ImGui.Checkbox("Lock compact position and width", ref locked)) { cfg.LockCompactPosition = locked; changed = true; }
        UiTheme.Tooltip("When unlocked, drag the left grip or resize a window edge.");
        var follow = cfg.FollowBstLevel;
        if (ImGui.Checkbox("Use BST level for guide filter", ref follow)) { cfg.FollowBstLevel = follow; changed = true; }
        UiTheme.Tooltip("Recommendations and the compact tracker always use your real BST level.");
        if (!follow)
        {
            var max = cfg.MaximumMobLevel; ImGui.SetNextItemWidth(130 * UiTheme.Scale);
            if (ImGui.InputInt("Maximum level", ref max)) { cfg.MaximumMobLevel = Math.Clamp(max, 1, 255); changed = true; }
            UiTheme.Tooltip("Manual ceiling for the guide's Only within level filter. Does not change the compact queue or recommended source.");
        }

        ImGui.Separator();
        UiTheme.Heading(FontAwesomeIcon.Paw, "TARGET MARKERS");
        var markers = cfg.ShowBeastTargetMarkers;
        if (ImGui.Checkbox("BST icon above capture targets", ref markers)) { cfg.ShowBeastTargetMarkers = markers; changed = true; }
        UiTheme.Tooltip("Show a small BST job icon above matching enemies. Its size follows the native icon beside the enemy level, including game UI/distance scaling. Existing quest markers take priority. Only works while playing BST; reports are not a universal capture detector.");
        if (markers)
        {
            ImGui.Indent();
            var missing = cfg.TargetMarkersMissingOnly;
            if (ImGui.Checkbox("Missing familiars only", ref missing)) { cfg.TargetMarkersMissingOnly = missing; changed = true; }
            UiTheme.Tooltip("Waits for verified bestiary data. Turn off to also mark obtained familiars.");
            var within = cfg.TargetMarkersWithinLevel;
            if (ImGui.Checkbox("At or below my level", ref within)) { cfg.TargetMarkersWithinLevel = within; changed = true; }
            UiTheme.Tooltip("Uses your current synced level in duties. Markers do not confirm every capture condition.");
            ImGui.Unindent();
        }
        if (changed) Plugin.SaveConfiguration();
    }

    private void DrawData()
    {
        var cfg = Plugin.Configuration;
        var changed = false;
        UiTheme.Heading(FontAwesomeIcon.Globe, "COMMUNITY SOURCES");
        var reports = cfg.UseCommunityCaptureReports;
        var positions = cfg.UseCommunitySpawnObservations;
        var sourceChanged = ImGui.Checkbox("Capture reports (FFXIV Collect)", ref reports);
        UiTheme.Tooltip("Capture targets, alternatives and reported levels. Optional download, cached locally.");
        sourceChanged |= ImGui.Checkbox("Mob positions (Teamcraft)", ref positions);
        UiTheme.Tooltip("Historical positions and levels. Positions alone do not prove capture eligibility.");
        if (sourceChanged)
        {
            cfg.UseCommunityCaptureReports = reports; cfg.UseCommunitySpawnObservations = positions;
            changed = true; world.RequestIndex();
        }
        ImGui.TextDisabled("Cached locally. No character data uploaded.");
        UiTheme.Tooltip("Only enabled public sources are downloaded. This plugin does not submit your character, captures or local sightings.");
        if (UiTheme.Button(FontAwesomeIcon.CloudDownloadAlt, "Update community data", (reports || positions ? "Download enabled public sources again and rebuild the local source index." : "Enable Capture reports or Mob positions first.") + "\n" + world.CommunityStatus, reports || positions))
            world.RequestIndex(refreshPublic: true);
        ImGui.Separator();
        UiTheme.Heading(FontAwesomeIcon.MapMarkedAlt, "LOCAL DATA");
        var observe = cfg.ObserveNearbyMobs;
        if (ImGui.Checkbox("Record nearby mobs", ref observe)) { cfg.ObserveNearbyMobs = observe; changed = true; }
        UiTheme.Tooltip("Record unowned, idle NPCs locally. Never uploads sightings.");
        var radius = (int)cfg.SpawnGroupRadius; ImGui.SetNextItemWidth(180 * UiTheme.Scale);
        if (ImGui.SliderInt("Group radius", ref radius, 30, 300, "%d yalms"))
        { cfg.SpawnGroupRadius = radius; changed = true; world.RequestRegroup(); }
        UiTheme.Tooltip("Combine nearby records with matching enemy identity, level, map and encounter.");
        if (UiTheme.Button(FontAwesomeIcon.BookOpen, "Reload game data", "Re-read the bestiary and rebuild locations from the local cache.")) reloadMetadata();
        ImGui.SameLine();
        if (UiTheme.Button(FontAwesomeIcon.Trash, "Clear sightings", "Delete only locally recorded NPC sightings after confirmation. Keeps your captures and downloaded public caches.")) ImGui.OpenPopup("##confirmClear");
        if (ImGui.BeginPopup("##confirmClear"))
        {
            ImGui.TextUnformatted("Clear local sightings? Captures and public caches stay.");
            if (ImGui.Button("Clear")) { world.RequestClearObservations(); ImGui.CloseCurrentPopup(); }
            UiTheme.Tooltip("Confirm deletion of local sightings. Public caches and capture progress are kept.");
            ImGui.SameLine(); if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            UiTheme.Tooltip("Keep local sightings and close this confirmation.");
            ImGui.EndPopup();
        }
        if (changed) Plugin.SaveConfiguration();
    }

    private void DrawDiagnostics()
    {
        if (UiTheme.Button(FontAwesomeIcon.Copy, "Copy diagnostics", "Copy capture, indexing, marker and travel status to the clipboard. Nothing is uploaded automatically."))
            ImGui.SetClipboardText(captured.GetDiagnostics(captured.Snapshot) + "\n" + world.GetDiagnostics() +
                $"\nMarkers: {Plugin.TargetMarkers.Status}\nTravel: {travel.Status}\nLast action: {travel.LastAction}\n");
        if (UiTheme.Button(FontAwesomeIcon.Sync, "Reindex layouts", "Rebuild the world/source index using game files and enabled cached sources; this does not force a public redownload.")) world.RequestIndex();
        ImGui.Separator();
        Diagnostic(captured.InitializationStatus, "One-time bestiary loading and temporary-menu ownership status.");
        Diagnostic(captured.Snapshot.Status, "Result of the latest capture-state read.");
        Diagnostic(Plugin.TargetMarkers.Status, "Matching rules and native-sized BST markers. Unmeasurable native nodes are skipped rather than using a large icon.");
        Diagnostic(beasts.Status, "Names, familiar images and hints read from the game sheets.");
        Diagnostic(world.Status, "Local layout indexing, grouping and source reconciliation.");
        Diagnostic(world.CommunityStatus, "Download/cache status for enabled public sources.");
        Diagnostic(world.ObservationStatus, "Locally recorded NPC observations; these are not uploaded.");
        Diagnostic(travel.Status, "Lifestream availability and current travel-provider status.");
        Diagnostic(captured.Snapshot.ManagerBinding, "Installed client-structure mapping used to read authoritative capture state.");
        if (!world.Index.HuntCatalogAvailable) Diagnostic("Local hunt cross-check incomplete. Reported encounter types are still used.",
            "The game-sheet hunt catalog is incomplete; known exclusions and explicit report conditions remain in effect.");
    }
    private static void Diagnostic(string value, string tooltip)
    {
        ImGui.TextWrapped(value);
        UiTheme.Tooltip(tooltip);
    }

}
