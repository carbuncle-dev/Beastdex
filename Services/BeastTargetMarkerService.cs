// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;
using Beastdex.Interop;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.NamePlate;

namespace Beastdex.Services;

/// <summary>Uses a small above-name marker sized against the native level/name icon.</summary>
public sealed class BeastTargetMarkerService : IDisposable
{
    private readonly WorldSearchService world;
    private readonly XbmCapturedService captured;
    private readonly BestiaryPlanService plans;
    private volatile Dictionary<(uint Territory, uint Name), BeastMarkerRule[]> rules = [];
    private int revision = -1;
    private bool lastEnabled;
    private bool failed;
    private volatile bool disposed;
    private readonly NameplateMarkerGeometry geometry = new();
    private readonly Dictionary<int, ulong> pendingMarkers = [];
    private int unmeasured;
    public int RuleCount { get; private set; }
    public int VisibleMarkers { get; private set; }
    public string Error { get; private set; } = "";
    public string Status => !Plugin.Configuration.ShowBeastTargetMarkers ? "Target markers off." : failed ? Error :
        world.Index.BeastmasterIconId == 0 ? "Waiting for the BST game icon." :
        $"{RuleCount} reported targets indexed; {VisibleMarkers} small nameplate markers; {unmeasured} waiting for native size.";

    public BeastTargetMarkerService(WorldSearchService world, XbmCapturedService captured, BestiaryPlanService plans)
    {
        this.world = world; this.captured = captured; this.plans = plans;
        // MarkerIconId is reset by the game each frame, so this must use OnDataUpdate,
        // not only the event for nameplates with major changes.
        Plugin.NamePlateGui.OnDataUpdate += OnNamePlateData;
        Plugin.NamePlateGui.OnPostDataUpdate += OnPostNamePlateData;
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "NamePlate", OnNamePlateFinalize);
    }

    public void Tick()
    {
        if (lastEnabled != Plugin.Configuration.ShowBeastTargetMarkers)
        {
            lastEnabled = Plugin.Configuration.ShowBeastTargetMarkers;
            failed = false; Error = ""; VisibleMarkers = 0;
            Plugin.NamePlateGui.RequestRedraw();
        }
        if (!lastEnabled || failed) return;
        plans.Update(); // Managed cached plans; runs even when every plugin window is hidden.
        if (revision == plans.Revision) return;
        revision = plans.Revision;
        var nextRules = new Dictionary<(uint Territory, uint Name), BeastMarkerRule[]>();
        var list = BeastTargetMatching.BuildRules(plans.Plans, world.Index);
        RuleCount = list.Length;
        foreach (var group in list.SelectMany(rule => ReportIdentity.Names(rule.Source)
            .Select(name => (rule.Source.TerritoryId, Name: name, Rule: rule)))
            .GroupBy(x => (x.TerritoryId, x.Name)))
            nextRules[group.Key] = group.Select(x => x.Rule).ToArray();
        rules = nextRules;
    }

    private unsafe void OnNamePlateData(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        // Restore our previous per-frame transform before native layout runs again.
        // This also restores slots that have been recycled for a different NPC.
        try { geometry.Restore(context.AddonAddress); }
        catch (Exception ex)
        {
            geometry.Forget(); failed = true; pendingMarkers.Clear();
            Error = "Target marker geometry unavailable: " + ex.Message;
            Plugin.Log.Warning(ex, "BST target markers disabled after a native geometry error.");
            return;
        }
        pendingMarkers.Clear();
        VisibleMarkers = 0; unmeasured = 0;
        var cfg = Plugin.Configuration;
        if (disposed || failed || !cfg.ShowBeastTargetMarkers || !Plugin.ClientState.IsLoggedIn ||
            Plugin.ClientState.IsPvP || Plugin.ClientState.IsGPosing || world.Index.BeastmasterIconId == 0) return;
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null || player.IsDead || player.ClassJob.RowId != world.Index.BeastmasterJobId) return;
        var territory = Plugin.ClientState.TerritoryType;
        var duty = world.Index.Territories.GetValueOrDefault(territory)?.DutyId ?? 0;
        // Respect level sync while in a duty, rather than the character's stored unsynced level.
        int? level = world.BstLevel is > 0 ? Math.Min(world.BstLevel.Value, player.Level) : null;
        var currentRules = rules;
        var index = world.Index;
        var captures = captured.Snapshot;
        try
        {
            foreach (var handler in handlers)
            {
                // Preserve native quest/event markers and earlier plugins' markers.
                // Names, the small name icon, root positioning and native quest markers stay untouched.
                if (handler.MarkerIconId > 0 || handler.GameObject is not IBattleNpc npc ||
                    !SpawnMatching.IsUnownedCombatNpc((byte)npc.BattleNpcKind, npc.OwnerId) ||
                    !npc.IsTargetable || npc.IsDead || npc.Address == 0 ||
                    !currentRules.TryGetValue((territory, npc.NameId), out var candidates)) continue;
                var rawFate = ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)npc.Address)->FateId;
                uint? fate = rawFate == ushort.MaxValue ? null : rawFate;
                var live = new LiveCaptureNpc(territory, duty, npc.NameId, npc.BaseId, npc.Level, fate);
                foreach (var rule in candidates)
                {
                    if (!BeastTargetMatching.Matches(rule, live, index, captures, level,
                        cfg.TargetMarkersMissingOnly, cfg.TargetMarkersWithinLevel)) continue;
                    if (!geometry.CanMeasure(context.AddonAddress, handler.NamePlateIndex))
                    { unmeasured++; break; }
                    handler.MarkerIconId = checked((int)index.BeastmasterIconId);
                    pendingMarkers[handler.NamePlateIndex] = handler.GameObjectId;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            failed = true;
            Error = "Target markers paused: " + ex.Message;
            Plugin.Log.Warning(ex, "BST nameplate markers paused for this session. Toggle the setting to retry.");
        }
    }

    private void OnPostNamePlateData(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (disposed || pendingMarkers.Count == 0) return;
        try
        {
            foreach (var handler in handlers)
            {
                if (!pendingMarkers.TryGetValue(handler.NamePlateIndex, out var objectId) ||
                    handler.GameObjectId != objectId) continue;
                // Native code normally consumes/reset the ID. Leave a later replacement alone.
                if (handler.MarkerIconId > 0 && handler.MarkerIconId != world.Index.BeastmasterIconId) continue;
                if (geometry.Fit(context.AddonAddress, handler.NamePlateIndex)) VisibleMarkers++;
                else unmeasured++;
            }
        }
        catch (Exception ex)
        {
            failed = true;
            Error = "Target marker sizing paused: " + ex.Message;
            Plugin.Log.Warning(ex, "BST marker sizing paused. Toggle the setting to retry.");
        }
        finally { pendingMarkers.Clear(); }
    }

    private void OnNamePlateFinalize(AddonEvent type, AddonArgs args)
    {
        // PreFinalize guarantees these nodes still belong to the live addon.
        geometry.Restore(args.Addon.Address);
        pendingMarkers.Clear();
    }

    public void Dispose()
    {
        disposed = true;
        Plugin.NamePlateGui.OnDataUpdate -= OnNamePlateData;
        Plugin.NamePlateGui.OnPostDataUpdate -= OnPostNamePlateData;
        Plugin.AddonLifecycle.UnregisterListener(OnNamePlateFinalize);
        // All native writes run on the framework thread. No cached pointer is used
        // for cleanup: resolve the addon again and compare node ownership tokens.
        _ = Plugin.Framework.RunOnFrameworkThread(() =>
        {
            try
            {
                pendingMarkers.Clear();
                geometry.Restore((nint)Plugin.GameGui.GetAddonByName("NamePlate"));
                Plugin.NamePlateGui.RequestRedraw();
            }
            catch (Exception ex) { Plugin.Log.Warning(ex, "Unable to restore BST marker geometry during unload."); }
        });
        rules = [];
    }
}
