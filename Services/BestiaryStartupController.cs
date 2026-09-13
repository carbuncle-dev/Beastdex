// SPDX-License-Identifier: GPL-3.0-only
namespace Beastdex.Services;

/// <summary>Pure, bounded per-login policy; native UI access lives in the host.</summary>
public sealed class BestiaryStartupController
{
    public const long SafeDelayMs = 3_000;
    public const long ResponseTimeoutMs = 12_000;
    public const long ExistingRequestGraceMs = 10_000;
    public const long PollIntervalMs = 250;

    private ulong characterId;
    private long? safeSince;
    private long? requestedSince;
    private long? openedAt;
    private long? observedAt;
    private long nextPollAt;
    private bool attemptUsed;
    private bool finished;
    private bool explicitRequest;

    public string Status { get; private set; } = "Waiting for login.";
    public string Phase { get; private set; } = "Waiting";
    public int OpenAttempts { get; private set; }
    public bool IsRunning => openedAt.HasValue || observedAt.HasValue;

    public void Reset(IBestiaryStartupHost host)
    {
        // Logout/character replacement must never dereference a previous session's UI.
        host.ReleaseWindow();
        characterId = 0;
        safeSince = requestedSince = openedAt = observedAt = null;
        nextPollAt = 0;
        attemptUsed = finished = explicitRequest = false;
        OpenAttempts = 0;
        Set("Waiting", "Waiting for login.");
    }

    public void Retry(IBestiaryStartupHost host)
    {
        host.RequestCaptureRefresh();
        if (IsRunning) return; // repeated button presses cannot create another open/close job
        attemptUsed = finished = false;
        explicitRequest = true;
        safeSince = requestedSince = null;
        nextPollAt = 0;
        Set("Waiting", "Initialization requested; waiting for the game UI to be ready.");
    }

    public void Tick(StartupContext input, IBestiaryStartupHost host)
    {
        if (!input.LoggedIn)
        {
            if (characterId != 0 || IsRunning) Reset(host);
            return;
        }
        if (input.CharacterId == 0)
        {
            Set("Waiting", "Waiting for character identity before initializing the bestiary.");
            return;
        }
        if (characterId != input.CharacterId)
        {
            var requested = explicitRequest;
            Reset(host);
            characterId = input.CharacterId;
            explicitRequest = requested;
            host.RequestCaptureRefresh();
            return; // never consume a snapshot from the previous character on this tick
        }

        if (!input.Enabled && !explicitRequest)
        {
            if (openedAt.HasValue) FinishOwned(input, host, "Automatic initialization disabled.");
            observedAt = null;
            safeSince = requestedSince = null;
            Set("Disabled", "Automatic menu initialization is disabled; capture polling is separate.");
            return;
        }
        if (finished)
        {
            if (input.CapturesAvailable)
                Set("Complete", "Capture data is ready. No more automatic menu opens this login.");
            return;
        }
        if (openedAt.HasValue)
        {
            if (!input.UiAvailable)
            {
                // Do not retain a close action over logout/loading/UI replacement.
                host.ReleaseWindow();
                Finish("Stopped", "Initialization interrupted by a UI transition; no delayed close or retry is queued.");
                return;
            }
            var menu = host.ReadMenu();
            if (!host.OwnsWindow)
            {
                Finish(input.CapturesAvailable ? "Complete" : "Stopped",
                    "Bestiary ownership released: " + host.OwnershipStatus + ". No automatic close or retry.");
                return;
            }
            Poll(input.NowMs, host);
            if (!input.SafeToContinue)
            {
                FinishOwned(input, host, "Initialization interrupted; the game is no longer idle.");
                return;
            }
            if (input.CapturesAvailable && input.NowMs - openedAt.Value >= PollIntervalMs)
            {
                FinishOwned(input, host, "Capture data received and verified.");
                return;
            }
            if (input.NowMs - openedAt.Value >= ResponseTimeoutMs)
            {
                FinishOwned(input, host, "Timed out waiting for verified captures. Use Initialize bestiary now to retry.");
                return;
            }
            Set("Loading", menu.Active ? "Bestiary opened temporarily; waiting for verified capture data." :
                "Waiting for the temporary bestiary window to finish opening.");
            return;
        }
        if (input.CapturesAvailable)
        {
            host.ReleaseWindow();
            Finish("Complete", "Capture data was already ready; no menu opening was needed.");
            return;
        }
        if (!input.ReaderAvailable)
        {
            Finish("Unavailable", "Capture API is unavailable in this Dalamud build; automatic menu opening was skipped.");
            return;
        }
        if (observedAt.HasValue)
        {
            Poll(input.NowMs, host);
            if (!input.UiAvailable || !host.ReadMenu().Active || input.NowMs - observedAt.Value >= ResponseTimeoutMs)
                Finish("Stopped", "An existing bestiary window was left untouched. Use Initialize bestiary now to retry if needed.");
            return;
        }
        if (!input.MetadataAvailable)
        {
            safeSince = null;
            Poll(input.NowMs, host);
            Set("Waiting", "Waiting for the local XBMPet metadata; no menu action yet.");
            return;
        }
        if (input.ManagerState == "Received")
        {
            // The server data exists but failed count/ID verification. Opening the UI is not a decoder fix.
            Finish("Stopped", "The game has received capture data, but validation failed. See capture diagnostics; no menu opened.");
            return;
        }
        if (!input.UiAvailable)
        {
            safeSince = null;
            Set("Waiting", "Waiting for the game UI and local character to finish loading.");
            return;
        }
        var current = host.ReadMenu();
        if (!current.Available)
        {
            safeSince = null;
            Set("Waiting", current.Status);
            return;
        }
        if (current.Active)
        {
            attemptUsed = true;
            observedAt = input.NowMs;
            Poll(input.NowMs, host);
            Set("Observing", "Bestiary already open; waiting for its data without closing it.");
            return;
        }
        if (input.ManagerState == "Requested")
        {
            requestedSince ??= input.NowMs;
            Poll(input.NowMs, host);
            if (input.NowMs - requestedSince.Value < ExistingRequestGraceMs)
            {
                safeSince = null;
                Set("Waiting", "A capture-data request is already pending; allowing it time to finish first.");
                return;
            }
        }
        else requestedSince = null;

        if (!input.SafeToOpen || !current.CanOpen)
        {
            safeSince = null;
            Set("Waiting", current.CanOpen ? "Waiting for an idle moment outside combat, loading and occupied states." :
                "The game does not currently allow the bestiary to open. No job switch or unlock bypass will be attempted.");
            return;
        }
        safeSince ??= input.NowMs;
        if (input.NowMs - safeSince.Value < SafeDelayMs)
        {
            Poll(input.NowMs, host);
            Set("Waiting", "Allowing the UI to settle before the one-time bestiary initialization.");
            return;
        }
        if (attemptUsed) return;
        attemptUsed = true; // consume BEFORE the native call, including failure paths
        OpenAttempts++;
        if (!host.TryOpen(out var result))
        {
            Finish("Stopped", result + " No automatic retry; use Initialize bestiary now.");
            return;
        }
        openedAt = input.NowMs;
        Poll(input.NowMs, host);
        Set("Loading", "Temporarily opening the Master's Bestiary to load capture data.");
    }

    public void Fault(string status) => Finish("Unavailable", status);

    public void Stop(StartupContext input, IBestiaryStartupHost host)
    {
        if (openedAt.HasValue) FinishOwned(input, host, "Plugin unloading.");
        host.ReleaseWindow();
        Finish("Stopped", "Initialization stopped.");
    }

    private void FinishOwned(StartupContext input, IBestiaryStartupHost host, string reason)
    {
        var cleanup = "No owned window was closed.";
        if (input.LoggedIn && input.CharacterId == characterId && input.UiAvailable && host.OwnsWindow)
            host.TryClose(out cleanup);
        host.ReleaseWindow();
        host.RequestCaptureRefresh();
        Finish(input.CapturesAvailable ? "Complete" : "Stopped", reason + " " + cleanup);
    }

    private void Poll(long now, IBestiaryStartupHost host)
    {
        if (now < nextPollAt) return;
        nextPollAt = now + PollIntervalMs;
        host.RequestCaptureRefresh();
    }
    private void Finish(string phase, string status)
    {
        openedAt = observedAt = safeSince = requestedSince = null;
        finished = true;
        explicitRequest = false;
        Set(phase, status);
    }
    private void Set(string phase, string status) { Phase = phase; Status = status; }
}

public readonly record struct StartupContext(long NowMs, bool LoggedIn, ulong CharacterId,
    bool Enabled, bool ReaderAvailable, bool MetadataAvailable, bool CapturesAvailable,
    string ManagerState, bool UiAvailable, bool SafeToOpen, bool SafeToContinue = true);

public readonly record struct BestiaryMenuSnapshot(bool Available, bool Active, bool CanOpen, string Status);

public interface IBestiaryStartupHost
{
    bool OwnsWindow { get; }
    string OwnershipStatus { get; }
    BestiaryMenuSnapshot ReadMenu();
    bool TryOpen(out string status);
    bool TryClose(out string status);
    void ReleaseWindow();
    void RequestCaptureRefresh();
}

/// <summary>Only identity values are retained; no saved address is ever dereferenced.</summary>
public sealed class BestiaryWindowOwnership
{
    public nint Agent { get; private set; }
    public nint Addon { get; private set; }
    public uint AddonId { get; private set; }
    public bool IsOwned => Agent != 0;
    public string Status { get; private set; } = "No window owned";

    public void Claim(nint agent)
    {
        Agent = agent; Addon = 0; AddonId = 0;
        Status = "Temporary bestiary opening requested by this plugin";
    }
    public void Observe(nint agent, nint addon, uint addonId, bool active)
    {
        if (!IsOwned) return;
        if (agent != Agent || (!active && Addon != 0))
        { Release("Agent changed or the temporary window was closed"); return; }
        if (addon == 0) return; // async allocation, or a short close transition
        if (Addon == 0) { Addon = addon; AddonId = addonId; }
        else if (addon != Addon || addonId != AddonId)
            Release("Bestiary addon was replaced; it is no longer owned");
    }
    public bool CanClose(nint agent, nint addon, uint id, bool active)
        => IsOwned && Addon != 0 && active && agent == Agent && addon == Addon && id == AddonId;
    public void Release(string status = "No window owned")
    { Agent = Addon = 0; AddonId = 0; Status = status; }
}
