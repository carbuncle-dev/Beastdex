// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Services;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Beastdex.Interop;

/// <summary>
/// Calls the installed, mapped UI agent. No hard-coded agent index, signatures,
/// opcodes, capture-bit writes, addon callbacks, or simulated chat/key commands.
/// Show/Hide calls are only made on the framework thread.
/// </summary>
public sealed class BestiaryMenuHost : IBestiaryStartupHost, IDisposable
{
    public const string AgentName = "XBMMonsterNotebook";
    private static readonly AddonEvent[] Events =
        [AddonEvent.PreFinalize, AddonEvent.PreHide, AddonEvent.PreClose,
         AddonEvent.PreReceiveEvent];
    private readonly System.Action refresh;
    private readonly Func<bool> capturesAvailable;
    private readonly BestiaryWindowOwnership ownership = new();
    private readonly AgentId agentId;
    private readonly bool mapped;
    private bool listening;
    private bool ownCall;
    private ulong ownerCharacter;

    public string BindingStatus { get; }
    public bool OwnsWindow => ownership.IsOwned;
    public string OwnershipStatus => ownership.Status;

    public BestiaryMenuHost(System.Action refresh, Func<bool> capturesAvailable)
    {
        this.refresh = refresh;
        this.capturesAvailable = capturesAvailable;
        // Resolve the enum from the installed assembly, not a numeric index from another patch.
        mapped = Enum.TryParse(AgentName, out agentId) && Enum.IsDefined(agentId);
        if (!mapped)
        {
            BindingStatus = "The installed AgentId enum has no XBMMonsterNotebook; automatic menu initialization is unavailable.";
            return;
        }
        try
        {
            foreach (var type in Events) Plugin.AddonLifecycle.RegisterListener(type, OnAddonEvent);
            listening = true;
            BindingStatus = $"Installed AgentId.{AgentName} ({(uint)agentId}); mapped Show/Hide; ownership lifecycle guards enabled.";
        }
        catch (Exception ex)
        {
            // Failure to install optional ownership guards must not prevent the plugin loading.
            try { Plugin.AddonLifecycle.UnregisterListener(OnAddonEvent); } catch { /* no native UI actions */ }
            listening = false;
            BindingStatus = $"Bestiary ownership listeners unavailable: {ex.GetType().Name}: {ex.Message}. Automatic opening is disabled.";
            Plugin.Log.Warning(ex, "Automatic bestiary initialization is unavailable; ordinary capture reading remains enabled.");
        }
    }

    public unsafe BestiaryMenuSnapshot ReadMenu()
    {
        if (!mapped || !listening) return new(false, false, false, BindingStatus);
        if (!Plugin.Framework.IsInFrameworkUpdateThread || !BestiaryStartupService.UiAvailable())
            return new(false, false, false, "Waiting for the game UI.");
        var agent = CurrentAgent();
        if (agent == null) return new(false, false, false, "Waiting for the game's bestiary agent.");
        var addon = CurrentAddon(agent);
        var active = agent->IsAgentActive() || (addon != null && addon->IsVisible);
        ownership.Observe((nint)agent, (nint)addon, addon == null ? 0u : addon->Id, active);
        return new(true, active, !active && agent->IsActivatable(), "Bestiary agent ready.");
    }

    public unsafe bool TryOpen(out string status)
    {
        status = "The game UI is not ready for automatic bestiary initialization.";
        if (!mapped || !listening || !Plugin.Framework.IsInFrameworkUpdateThread ||
            !BestiaryStartupService.SafeToOpen()) return false;
        if (capturesAvailable()) { status = "Capture data is already available."; return false; }
        var current = ReadMenu();
        if (!current.Available || current.Active || !current.CanOpen)
        { status = current.Active ? "Bestiary was opened by the player; leaving it alone." : current.Status; return false; }

        // Re-resolve immediately before the UI call. The saved values are ownership identities only.
        var agent = CurrentAgent();
        if (agent == null || agent->IsAgentActive() || !agent->IsActivatable()) return false;
        ownerCharacter = Plugin.PlayerState.ContentId;
        ownership.Claim((nint)agent);
        ownCall = true;
        try { agent->Show(); }
        catch { ReleaseWindow(); throw; }
        finally { ownCall = false; }
        ReadMenu(); // adopt the addon identity now, or on the next tick for asynchronous setup
        status = "Requested a temporary bestiary opening.";
        return true;
    }

    public unsafe bool TryClose(out string status)
    {
        status = "No owned bestiary window was closed.";
        if (!Plugin.Framework.IsInFrameworkUpdateThread || !BestiaryStartupService.UiAvailable() ||
            !OwnsWindow || ownerCharacter != Plugin.PlayerState.ContentId) return false;
        var agent = CurrentAgent();
        if (agent == null) return false;
        var addon = CurrentAddon(agent);
        var active = agent->IsAgentActive() || (addon != null && addon->IsVisible);
        if (addon == null || !ownership.CanClose((nint)agent, (nint)addon, addon->Id, active))
        { status = "The bestiary window changed; no close was sent."; return false; }
        ownCall = true;
        try { agent->Hide(); }
        finally { ownCall = false; ReleaseWindow(); }
        status = "Closed the temporary bestiary opened by this plugin.";
        return true;
    }

    private unsafe AgentInterface* CurrentAgent()
    {
        var module = AgentModule.Instance();
        return module == null || module->Initialized == 0 ? null : module->GetAgentByInternalId(agentId);
    }
    private static unsafe AtkUnitBase* CurrentAddon(AgentInterface* agent)
    {
        var id = agent->GetAddonId();
        var units = RaptureAtkUnitManager.Instance();
        return units == null || id == 0 || id > ushort.MaxValue ? null : units->GetAddonById((ushort)id);
    }

    private void OnAddonEvent(AddonEvent type, AddonArgs args)
    {
        // Passive observation only. Never block the game event or call Show/Hide from its hook.
        if (ownCall || !OwnsWindow) return;
        if (ownership.Addon != 0 ? args.Addon.Address != ownership.Addon : args.AddonName != AgentName) return;
        if (type is AddonEvent.PreFinalize or AddonEvent.PreHide or AddonEvent.PreClose)
            ownership.Release("The game or player closed/hidden the temporary bestiary");
        else if (args is AddonReceiveEventArgs input && BestiaryUserInput.IsInteraction(input.AtkEventType.ToString()))
            ownership.Release("Player interaction detected; the bestiary now belongs to the player");
    }

    public void RequestCaptureRefresh() => refresh();
    public void ReleaseWindow() { ownership.Release(); ownerCharacter = 0; }
    public void Dispose()
    {
        ReleaseWindow();
        if (listening) Plugin.AddonLifecycle.UnregisterListener(OnAddonEvent);
        listening = false;
    }
}
