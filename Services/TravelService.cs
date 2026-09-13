// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Beastdex.Models;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace Beastdex.Services;

/// <summary>Click-triggered travel only. No auto-queue, movement, capture or combat.</summary>
public sealed class TravelService
{
    private readonly WorldSearchService world;
    private readonly ConcurrentQueue<System.Action> pending = new();
    private ICallGateSubscriber<uint, byte, bool>? teleport;
    private ICallGateSubscriber<bool>? busy;
    private readonly Stopwatch refresh = Stopwatch.StartNew();
    private long lastTeleport;
    public TravelDestination[] UnlockedDestinations { get; private set; } = [];
    public bool LifestreamAvailable { get; private set; }
    public string Status { get; private set; } = "Teleport provider: checking Lifestream IPC.";
    public string LastAction { get; private set; } = string.Empty;

    public TravelService(WorldSearchService world) => this.world = world;

    public TravelDestination[] Destinations(uint beastId, SpawnPoint? near = null)
    {
        var territories = near != null ? new HashSet<uint> { near.TerritoryId } : world.GetTerritories(beastId).ToHashSet();
        return SpawnMatching.OrderDestinations(UnlockedDestinations.Where(d => territories.Contains(d.Aetheryte.TerritoryId)), near is { HasCoordinates: true } ? near : null).ToArray();
    }

    public void Tick()
    {
        // Called on Framework.Update; no native operations in ImGui.Draw.
        if (refresh.ElapsedMilliseconds > 1000)
        {
            refresh.Restart();
            try
            {
                teleport ??= Plugin.PluginInterface.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");
                busy ??= Plugin.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
                LifestreamAvailable = teleport.HasFunction;
                Status = LifestreamAvailable ? "Teleport provider: Lifestream" : "Teleport requires Lifestream to be installed and enabled.";
                var list = new List<TravelDestination>();
                if (Plugin.ClientState.IsLoggedIn)
                    foreach (var unlocked in Plugin.AetheryteList)
                        if (!unlocked.IsSharedHouse && !unlocked.IsApartment &&
                            world.Index.Aetherytes.TryGetValue(unlocked.AetheryteId, out var a))
                            list.Add(new TravelDestination(a, unlocked.SubIndex, unlocked.GilCost));
                UnlockedDestinations = list.ToArray();
            }
            catch (Exception ex)
            {
                LifestreamAvailable = false;
                UnlockedDestinations = [];
                teleport = null;
                busy = null;
                Status = $"Travel provider unavailable: {ex.GetType().Name}: {ex.Message}";
            }
        }
        // One request per frame; double-clicks are rate-limited again below.
        if (pending.TryDequeue(out var action))
            try { action(); }
            catch (Exception ex)
            {
                LastAction = $"Travel action failed: {ex.GetType().Name}: {ex.Message}";
                Plugin.Log.Warning(ex, "Beastmaster travel action failed.");
            }
    }

    private bool CanAct(bool isTeleport)
    {
        if (!Plugin.ClientState.IsLoggedIn || Plugin.ObjectTable.LocalPlayer is not { IsDead: false })
        { LastAction = "A living, logged-in character is required."; return false; }
        if (Plugin.ClientState.IsPvP || Plugin.ClientState.IsGPosing ||
            Plugin.Condition[ConditionFlag.InCombat] || Plugin.Condition[ConditionFlag.Casting] ||
            Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51] ||
            Plugin.Condition[ConditionFlag.LoggingOut] || Plugin.Condition[ConditionFlag.WatchingCutscene] ||
            Plugin.Condition[ConditionFlag.WatchingCutscene78] || Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent])
        { LastAction = "Travel is blocked during combat, casting, zoning, cutscenes, logout, PvP or group pose."; return false; }
        if (isTeleport && (Plugin.Condition[ConditionFlag.BoundByDuty] ||
                Plugin.Condition[ConditionFlag.BoundByDuty56] || Plugin.Condition[ConditionFlag.BoundByDuty95]))
        { LastAction = "Leave the current duty before teleporting."; return false; }
        return true;
    }

    public void RequestTeleport(TravelDestination destination)
    {
        if (pending.Count > 4) return;
        pending.Enqueue(() =>
        {
            if (!CanAct(true)) return;
            if (Environment.TickCount64 - lastTeleport < 3000)
            { LastAction = "A teleport was just requested; duplicate click ignored."; return; }
            if (teleport == null || !teleport.HasFunction)
            { LastAction = "Enable Lifestream first. No direct native teleport fallback is used."; return; }
            if (busy?.HasFunction == true && busy.InvokeFunc())
            { LastAction = "Lifestream is busy; no new travel request was sent."; return; }
            // Recheck the actual character's unlock list after the user clicks.
            if (!Plugin.AetheryteList.Any(a => a.AetheryteId == destination.Aetheryte.Id &&
                    a.SubIndex == destination.SubIndex && !a.IsSharedHouse && !a.IsApartment))
            { LastAction = "This aetheryte is no longer in the current character's unlock list."; return; }
            lastTeleport = Environment.TickCount64;
            LastAction = teleport.InvokeFunc(destination.Aetheryte.Id, destination.SubIndex)
                ? $"Lifestream accepted teleport to {destination.Aetheryte.Name}. Normal travel cost/restrictions apply."
                : "Lifestream declined the teleport. Check your current game state and travel requirements.";
        });
    }

    public void RequestDuty(uint contentFinderConditionId)
    {
        if (pending.Count > 4) return;
        pending.Enqueue(() => OpenDuty(contentFinderConditionId));
    }

    private unsafe void OpenDuty(uint contentFinderConditionId)
    {
        if (!CanAct(false)) return;
        if (contentFinderConditionId == 0 || !Plugin.DataManager.GetExcelSheet<ContentFinderCondition>()
                .TryGetRow(contentFinderConditionId, out var duty))
        { LastAction = "The bestiary hint does not resolve to a Duty Finder entry."; return; }
        if (!duty.IsInDutyFinder)
        { LastAction = "This content is not listed in the regular Duty Finder."; return; }
        var agent = AgentContentsFinder.Instance();
        if (agent == null) { LastAction = "Duty Finder is unavailable in the current UI state."; return; }
        agent->OpenRegularDuty(contentFinderConditionId);
        LastAction = $"Opened Duty Finder for {duty.Name.ExtractText()}. No queue or party settings were changed.";
    }

    public void RequestMap(SpawnPoint point, uint mapId)
    {
        if (pending.Count > 4) return;
        pending.Enqueue(() =>
        {
            if (!Plugin.ClientState.IsLoggedIn) { LastAction = "Log in to open a map."; return; }
            if (!point.HasCoordinates || !SpawnMatching.ValidPosition(point.X, point.Y, point.Z)) { LastAction = "This report identifies an area/duty, not a map position."; return; }
            if (mapId == 0 || !world.Index.Maps.TryGetValue(mapId, out var map) || map.TerritoryId != point.TerritoryId)
            { LastAction = "A map in the NPC's territory must be selected."; return; }
            var ok = Plugin.GameGui.OpenMapWithMapLink(point.TerritoryId, mapId, new Vector3(point.X, point.Y, point.Z));
            LastAction = ok ? "Map flag placed at the recorded mob-group position."
                : "The game could not open that map.";
        });
    }
}
