// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using Beastdex.Models;
using Beastdex.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace Beastdex.Windows;

/// <summary>Two fixed rows. Status changes and long localized names cannot grow the HUD.</summary>
public sealed class CompactWindow : Window
{
    private readonly BestiaryPlanService plans;
    private readonly XbmCapturedService captured;
    private readonly WorldSearchService world;
    private readonly TravelService travel;
    private readonly System.Action<BeastInfo> showSources;
    private readonly System.Action showFull;
    private readonly System.Action settings;
    private readonly System.Action hideOtherWindows;
    private readonly CompactSelection selection = new();
    private IDisposable? skin;
    private float rowHeight;

    public CompactWindow(BestiaryPlanService plans, XbmCapturedService captured, WorldSearchService world,
        TravelService travel, System.Action<BeastInfo> showSources, System.Action showFull,
        System.Action settings, System.Action hideOtherWindows)
        : base("Beastdex - Next catch###BeastdexCompactV8",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoCollapse, true)
    {
        this.plans = plans; this.captured = captured; this.world = world; this.travel = travel;
        this.showSources = showSources; this.showFull = showFull; this.settings = settings;
        this.hideOtherWindows = hideOtherWindows;
        Size = new Vector2(660, 72); SizeCondition = ImGuiCond.FirstUseEver;
        AllowClickthrough = false; AllowPinning = false;
        DisableFadeInFadeOut = true;
        // Escape may close Sources/Settings, but must not dismiss this persistent HUD.
        RespectCloseHotkey = false;
    }

    public override void OnOpen()
    {
        // Covers direct IsOpen changes as well as the command, toolbar and preferred-mode routes.
        hideOtherWindows();
        captured.RequestRefresh();
    }

    public override void PreDraw()
    {
        skin = CompactSkin.Push();
        rowHeight = Math.Max(24 * UiTheme.Scale, ImGui.GetFrameHeight());
        var height = (2 * rowHeight + 4 * UiTheme.Scale + 16 * UiTheme.Scale) / UiTheme.Scale;
        // Dalamud scales constraints itself. Height follows font/UI scale, never the target's text.
        SizeConstraints = new WindowSizeConstraints
        { MinimumSize = new Vector2(560, height), MaximumSize = new Vector2(1100, height) };
        IsPinned = false;
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse |
            (Plugin.Configuration.LockCompactPosition ? ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize : ImGuiWindowFlags.None);
    }
    public override void PostDraw() { skin?.Dispose(); skin = null; }

    public override void Draw()
    {
        plans.Update();
        var queue = plans.Next;
        var i = selection.Sync(queue);
        var current = i >= 0 ? queue[i] : null;
        var s = UiTheme.Scale;
        var gap = 4 * s;
        var height = 2 * rowHeight + gap;
        CompactSkin.Frame();
        if (!ImGui.BeginTable("##hud", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX)) return;
        try
        {
            ImGui.TableSetupColumn("drag", ImGuiTableColumnFlags.WidthFixed, 8 * s);
            ImGui.TableSetupColumn("icon", ImGuiTableColumnFlags.WidthFixed, 44 * s);
            ImGui.TableSetupColumn("target", ImGuiTableColumnFlags.WidthStretch, .45f);
            ImGui.TableSetupColumn("location", ImGuiTableColumnFlags.WidthStretch, .55f);
            ImGui.TableSetupColumn("tools", ImGuiTableColumnFlags.WidthFixed, 5 * rowHeight + 4 * gap);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); DrawGrip(height);
            ImGui.TableSetColumnIndex(1);
            var imageTop = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(imageTop + new Vector2(0, Math.Max(0, (height - 44 * s) / 2)));
            if (current != null) BeastImages.Draw(current.Plan.Beast, 44, "Missing familiar. The tracker shows its preferred level-ready capture source; use Sources for alternatives.");
            else { UiTheme.Icon(FontAwesomeIcon.Paw, CompactSkin.Gold); UiTheme.Tooltip("Beastdex next-catch tracker"); }
            ImGui.TableSetColumnIndex(2); DrawTarget(current);
            ImGui.TableSetColumnIndex(3); DrawLocation(current);
            ImGui.TableSetColumnIndex(4); DrawTools(current, i, queue);
        }
        finally { ImGui.EndTable(); }
    }

    private void DrawGrip(float height)
    {
        var pos = ImGui.GetCursorScreenPos();
        var s = UiTheme.Scale;
        ImGui.InvisibleButton("##dragBar", new Vector2(8 * s, height));
        if (!Plugin.Configuration.LockCompactPosition && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
        UiTheme.Tooltip(Plugin.Configuration.LockCompactPosition
            ? "Tracker position locked. Unlock in Settings."
            : "Drag this grip to move the tracker. Drag a window edge to change its width; the height stays compact.");
        var color = ImGui.ColorConvertFloat4ToU32(new Vector4(CompactSkin.Gold.X, CompactSkin.Gold.Y, CompactSkin.Gold.Z, .48f));
        for (var n = -1; n <= 1; n++)
        {
            var dot = pos + new Vector2(3 * s, height / 2 + n * 5 * s);
            ImGui.GetWindowDrawList().AddRectFilled(dot, dot + new Vector2(2 * s), color);
        }
    }

    private void DrawTarget(NextCatch? current)
    {
        var top = ImGui.GetCursorScreenPos();
        if (current != null)
        {
            var level = current.Source.Point.Level;
            ImGui.AlignTextToFramePadding();
            UiTheme.TextFit($"Lv. {level}  {current.Plan.Beast.Name}", CompactSkin.Text,
                $"{current.Plan.Beast.Name} / bestiary #{current.BeastId}\nYour BST: {world.BstLevel}; enemy: {level}.\n" +
                EncounterPolicy.RecommendationReason(current.Source, world.Index, world.BstLevel) +
                "\nLevel-ready does not verify duty unlocks or special capture requirements.");
            ImGui.SetCursorScreenPos(new Vector2(top.X, top.Y + rowHeight + 4 * UiTheme.Scale));
            EncounterIcons.Draw(current.Source, world.Index, withLabel: false, size: 18);
            ImGui.SameLine(0, 4 * UiTheme.Scale);
            ImGui.AlignTextToFramePadding();
            UiTheme.TextFit(current.Source.Point.Name, CompactSkin.Muted,
                current.Source.Point.Name + "\n" + EncounterPresentation.Describe(current.Source, world.Index).Detail +
                "\n" + current.Source.EvidenceLabel + "\nUse Sources to compare other enemies, areas and levels.");
        }
        else
        {
            var (heading, detail) = EmptyMessage();
            UiTheme.TextFit(heading, CompactSkin.Gold, heading + "\n" + detail);
            ImGui.SetCursorScreenPos(new Vector2(top.X, top.Y + rowHeight + 4 * UiTheme.Scale));
            UiTheme.TextFit(detail, CompactSkin.Muted);
        }
    }

    private void DrawLocation(NextCatch? current)
    {
        var top = ImGui.GetCursorScreenPos();
        if (current != null) TravelUi.LocationLink(world, travel, current.Source.Point, compact: true);
        else UiTheme.TextFit($"BST {world.BstLevel?.ToString() ?? "?"}", CompactSkin.Muted,
            "Your stored Beastmaster job level. Recommendations use BST even when another job is active.");
        ImGui.SetCursorScreenPos(new Vector2(top.X, top.Y + rowHeight + 4 * UiTheme.Scale));
        if (current != null) TravelUi.SourceButton(world, travel, current.BeastId, current.Source.Point);
        else UiTheme.TextFit("Map / travel pending", CompactSkin.Muted,
            "Map and travel actions become available when there is a missing, level-ready target with a known source.");
    }

    private void DrawTools(NextCatch? current, int i, IReadOnlyList<NextCatch> queue)
    {
        var top = ImGui.GetCursorScreenPos();
        var gap = 4 * UiTheme.Scale;
        if (CompactSkin.Button("previous", FontAwesomeIcon.ChevronLeft, i > 0 ? "Previous familiar in recommended-level order." : "Already at the first target, or no level-ready target is available.", rowHeight, i > 0))
            selection.Move(-1, queue);
        ImGui.SameLine(0, gap);
        if (ImGui.Button($"{(i < 0 ? 0 : i + 1)}/{queue.Count}##position", new Vector2(2 * rowHeight + gap, rowHeight))) selection.Reset();
        UiTheme.Tooltip("Position in the missing, level-ready queue. Click to return to the lowest recommended target.\n" +
            $"Your BST level: {world.BstLevel?.ToString() ?? "unknown"}. One entry per familiar; arrows preserve selection through refreshes.");
        ImGui.SameLine(0, gap);
        if (CompactSkin.Button("next", FontAwesomeIcon.ChevronRight, i >= 0 && i + 1 < queue.Count ? "Next familiar in recommended-level order." : "Already at the last target, or no level-ready target is available.", rowHeight, i >= 0 && i + 1 < queue.Count))
            selection.Move(1, queue);
        ImGui.SameLine(0, gap);
        if (CompactSkin.Button("close", FontAwesomeIcon.Times, "Hide the tracker. Automatic bestiary sync continues.", rowHeight)) IsOpen = false;

        ImGui.SetCursorScreenPos(new Vector2(top.X, top.Y + rowHeight + gap));
        if (CompactSkin.Button("lowest", FontAwesomeIcon.SortAmountDown, "Follow the lowest recommended missing catch again.", rowHeight)) selection.Reset();
        ImGui.SameLine(0, gap);
        if (CompactSkin.Button("sources", FontAwesomeIcon.List, current != null ? "Open all capture-source alternatives alongside this tracker. The selected target stays visible." : "No target to inspect. Open the field guide for future or unknown-level sources.", rowHeight, current != null))
            showSources(current!.Plan.Beast);
        ImGui.SameLine(0, gap);
        if (CompactSkin.Button("refresh", FontAwesomeIcon.Sync, SyncTooltip(), rowHeight, tint:
            !captured.Snapshot.Available ? CompactSkin.Gold : Plugin.Configuration.AutoRefreshBestiary ? CompactSkin.Ready : CompactSkin.Muted))
            captured.RequestRefresh();
        ImGui.SameLine(0, gap);
        if (CompactSkin.Button("full", FontAwesomeIcon.Expand, "Switch to the expanded field guide. This hides the compact tracker.", rowHeight)) showFull();
        ImGui.SameLine(0, gap);
        if (CompactSkin.Button("settings", FontAwesomeIcon.Cog, "Open settings alongside this tracker. Adjust appearance, markers, capture refresh and source data.", rowHeight)) settings();
    }

    private string SyncTooltip() =>
        (Plugin.Configuration.AutoRefreshBestiary ? $"Auto-sync every {Plugin.Configuration.BestiaryRefreshSeconds}s." : "Auto-sync paused.") +
        "\nClick to re-read captures now. No community download or world rebuild.\n" + captured.Snapshot.Status +
        (world.IsIndexing ? "\nLocation data is being updated in the background." : "") +
        (travel.LastAction.Length > 0 ? "\nLast travel action: " + travel.LastAction : "");

    private (string Heading, string Detail) EmptyMessage()
    {
        if (!captured.Snapshot.Available) return ("Waiting for bestiary", "Capture status is unknown; no entries assumed missing.");
        if (world.BstLevel is not > 0) return ("BST level unavailable", "The tracker updates when your Beastmaster level is available.");
        var got = captured.Snapshot.CapturedRowIds.ToHashSet();
        var missing = plans.Plans.Where(p => !got.Contains(p.Beast.RowId)).ToArray();
        if (plans.Plans.Length > 0 && missing.Length == 0) return ("Collection complete", "All loaded familiars obtained.");
        var next = missing.Select(p => SourceReconciler.LowestReported(p.Options, world.Index)?.Point.Level)
            .Where(l => l.HasValue && l.Value > world.BstLevel.Value).Order().FirstOrDefault();
        return ("No level-ready target", next.HasValue ? $"Next known source: Lv. {next}. More options in the field guide." :
            "Enable community capture sources in Settings, or wait for source data.");
    }
}
