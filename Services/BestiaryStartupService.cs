// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using Beastdex.Interop;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Beastdex.Services;

/// <summary>Login/session integration. Normal capture polling remains independent.</summary>
public sealed class BestiaryStartupService : IDisposable
{
    private readonly XbmCapturedService captures;
    private readonly BeastDataService metadata;
    private readonly BestiaryStartupController controller = new();
    private readonly BestiaryMenuHost host;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private ulong lastCharacter;
    private int resetRequested;
    private int retryRequested;
    private bool failed;

    public string Status => controller.Status;
    public string Diagnostics => $"Bestiary initialization: phase={controller.Phase}; opens this session={controller.OpenAttempts}; " +
        $"auto={Plugin.Configuration.AutoInitializeBestiary}; {controller.Status}\n" +
        $"Bestiary UI binding: {host.BindingStatus}\n" +
        "Direct request: no verified mapped pet-list request used; initialization uses the normal bestiary agent.\n";

    public BestiaryStartupService(XbmCapturedService captures, BeastDataService metadata)
    {
        this.captures = captures;
        this.metadata = metadata;
        host = new BestiaryMenuHost(captures.RequestRefresh, () => captures.Snapshot.Available);
    }
    public void RequestReset() => Interlocked.Exchange(ref resetRequested, 1);
    public void RequestRetry() => Interlocked.Exchange(ref retryRequested, 1);

    public void Tick()
    {
        if (!Plugin.Framework.IsInFrameworkUpdateThread) return;
        try
        {
            if (Interlocked.Exchange(ref resetRequested, 0) != 0)
            {
                controller.Reset(host);
                lastCharacter = 0;
                failed = false;
            }
            var loggedIn = Plugin.ClientState.IsLoggedIn;
            if (!loggedIn)
            {
                lastCharacter = 0;
                // Never carry an explicit retry into a different login.
                Interlocked.Exchange(ref retryRequested, 0);
            }
            else if (Plugin.PlayerState.IsLoaded && Plugin.PlayerState.ContentId != 0 &&
                     Plugin.PlayerState.ContentId != lastCharacter)
            {
                lastCharacter = Plugin.PlayerState.ContentId;
                captures.Clear("Waiting for this character's bestiary capture data.");
                captures.RequestRefresh();
            }
            if (Interlocked.Exchange(ref retryRequested, 0) != 0)
            { failed = false; controller.Retry(host); }
            if (!failed) controller.Tick(Context(), host);
        }
        catch (Exception ex)
        {
            failed = true;
            host.ReleaseWindow();
            controller.Fault($"Automatic bestiary initialization stopped: {ex.GetType().Name}: {ex.Message}");
            Plugin.Log.Warning(ex, "Bestiary initialization stopped safely; no further automatic menu actions this session.");
        }
    }

    private StartupContext Context() => new(clock.ElapsedMilliseconds, Plugin.ClientState.IsLoggedIn,
        lastCharacter, Plugin.Configuration.AutoInitializeBestiary, captures.ReadApiAvailable,
        metadata.Available, captures.Snapshot.Available, captures.Snapshot.ManagerState, UiAvailable(), SafeToOpen(), SafeToContinue());

    internal static bool UiAvailable()
    {
        if (!Plugin.ClientState.IsLoggedIn || !Plugin.PlayerState.IsLoaded ||
            Plugin.PlayerState.ContentId == 0 || Plugin.ObjectTable.LocalPlayer == null ||
            Plugin.GameGui.GameUiHidden) return false;
        var c = Plugin.Condition;
        return !c[ConditionFlag.BetweenAreas] && !c[ConditionFlag.BetweenAreas51] && !c[ConditionFlag.LoggingOut];
    }

    internal static bool SafeToContinue()
    {
        // A notebook can itself set an occupied state. Do not abort the in-flight
        // load for that; only unsafe gameplay transitions cancel an owned opening.
        if (!UiAvailable()) return false;
        var c = Plugin.Condition;
        return !c[ConditionFlag.InCombat] && !c[ConditionFlag.Unconscious] &&
            !c[ConditionFlag.WatchingCutscene] && !c[ConditionFlag.WatchingCutscene78] &&
            !c[ConditionFlag.OccupiedInCutSceneEvent];
    }

    internal static unsafe bool SafeToOpen()
    {
        if (!UiAvailable()) return false;
        var c = Plugin.Condition;
        if (c[ConditionFlag.InCombat] || c[ConditionFlag.Unconscious] ||
            c[ConditionFlag.Casting] || c[ConditionFlag.Casting87] ||
            c[ConditionFlag.WatchingCutscene] || c[ConditionFlag.WatchingCutscene78] ||
            c[ConditionFlag.Occupied] || c[ConditionFlag.Occupied30] ||
            c[ConditionFlag.OccupiedInEvent] || c[ConditionFlag.OccupiedInQuestEvent] ||
            c[ConditionFlag.Occupied33] || c[ConditionFlag.OccupiedInCutSceneEvent] ||
            c[ConditionFlag.Occupied38] || c[ConditionFlag.Occupied39] ||
            c[ConditionFlag.OccupiedSummoningBell] || c[ConditionFlag.TradeOpen] ||
            c[ConditionFlag.Crafting] || c[ConditionFlag.Gathering] ||
            c[ConditionFlag.ExecutingCraftingAction] || c[ConditionFlag.PreparingToCraft] ||
            c[ConditionFlag.ExecutingGatheringAction] || c[ConditionFlag.Fishing] ||
            c[ConditionFlag.Performing] || c[ConditionFlag.Mounting] || c[ConditionFlag.Jumping]) return false;
        var units = RaptureAtkUnitManager.Instance();
        if (units == null || units->IsEditingHudLayout || units->IsUiFading) return false;
        return true;
    }

    public void Dispose()
    {
        try
        {
            // Do not schedule an unobserved UI callback after plugin disposal.
            if (Plugin.Framework.IsInFrameworkUpdateThread) controller.Stop(Context(), host);
        }
        catch (Exception ex) { Plugin.Log.Debug(ex, "Bestiary initialization cleanup could not close its temporary UI."); }
        finally { host.Dispose(); }
    }
}
