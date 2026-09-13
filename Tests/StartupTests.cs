// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Services;

internal static class StartupTests
{
    private sealed class Host : IBestiaryStartupHost
    {
        public bool Available = true, Active, CanOpen = true, SucceedOpen = true;
        public bool OwnsWindow { get; set; }
        public string OwnershipStatus { get; private set; } = "No owner";
        public int Opens, Closes, Refreshes, Reads;
        public BestiaryMenuSnapshot ReadMenu() { Reads++; return new(Available, Active, CanOpen, "Test menu"); }
        public bool TryOpen(out string status)
        {
            Opens++; status = "Test open";
            if (!SucceedOpen || Active || !CanOpen) return false;
            Active = OwnsWindow = true; OwnershipStatus = "Plugin"; return true;
        }
        public bool TryClose(out string status)
        {
            status = "Not owned";
            if (!OwnsWindow) return false;
            Closes++; Active = OwnsWindow = false; status = "Test close"; return true;
        }
        public void ReleaseWindow() { OwnsWindow = false; OwnershipStatus = "Released or player-owned"; }
        public void RequestCaptureRefresh() => Refreshes++;
    }

    private static StartupContext C(long time = 0) => new(time, true, 1, true, true, true, false, "None", true, true);
    private static void Open(BestiaryStartupController controller, Host host)
    {
        controller.Tick(C(), host); // bind character; never consume a previous-character snapshot
        controller.Tick(C(1), host);
        controller.Tick(C(1 + BestiaryStartupController.SafeDelayMs), host);
    }

    public static void Run(Action<bool, string> check)
    {
        var host = new Host(); var controller = new BestiaryStartupController();
        controller.Tick(C() with { LoggedIn = false }, host);
        check(host.Opens == 0 && host.Reads == 0, "No native menu work while logged out");
        controller.Tick(C() with { CharacterId = 0 }, host);
        check(host.Opens == 0 && host.Reads == 0, "Character identity must exist before initialization");
        controller.Tick(C() with { CapturesAvailable = true, ManagerState = "Received" }, host);
        controller.Tick(C(1) with { CapturesAvailable = true, ManagerState = "Received" }, host);
        check(host.Opens == 0 && controller.Phase == "Complete", "Already-loaded captures skip menu entirely");
        controller.Tick(C(50_000), host);
        check(host.Opens == 0, "No repeated automatic attempt after a completed session");

        host = new(); controller = new();
        controller.Tick(C(), host);
        controller.Tick(C(1), host);
        controller.Tick(C(3_000), host);
        check(host.Opens == 0, "UI must be continuously safe for three seconds");
        controller.Tick(C(3_001), host);
        check(host.Opens == 1 && controller.OpenAttempts == 1 && host.OwnsWindow, "One temporary opening after settling");
        var refreshes = host.Refreshes;
        controller.Tick(C(3_050) with { ManagerState = "Requested" }, host);
        check(host.Closes == 0 && host.Refreshes == refreshes, "Do not close before response or spam per-frame polls");
        controller.Tick(C(3_251) with { CapturesAvailable = true, ManagerState = "Received" }, host);
        check(host.Closes == 1 && !host.OwnsWindow && controller.Phase == "Complete", "Verified response closes only the owned window");
        controller.Tick(C(10_000), host); controller.Tick(C(40_000), host);
        check(host.Opens == 1 && host.Closes == 1, "Refreshes and elapsed time do not repeat completed menu cycle");

        host = new(); controller = new();
        controller.Tick(C(), host);
        controller.Tick(C(1) with { SafeToOpen = false }, host);
        controller.Tick(C(10_000) with { SafeToOpen = false }, host);
        check(host.Opens == 0, "Busy/combat/cutscene guard prevents an initial opening");
        controller.Tick(C(10_001), host); controller.Tick(C(12_000) with { SafeToOpen = false }, host);
        controller.Tick(C(12_001), host); controller.Tick(C(15_000), host);
        check(host.Opens == 0, "Unsafe interruption resets the continuous-safe delay");
        controller.Tick(C(15_001), host);
        check(host.Opens == 1, "Open after a fresh safe interval");

        host = new() { Active = true }; controller = new();
        controller.Tick(C(), host); controller.Tick(C(1), host);
        controller.Tick(C(300) with { CapturesAvailable = true, ManagerState = "Received" }, host);
        check(host.Opens == 0 && host.Closes == 0 && host.Active, "A player-opened bestiary is observed, never closed");
        host = new() { Active = true }; controller = new();
        controller.Tick(C(), host); controller.Tick(C(1), host); controller.Tick(C(15_000), host);
        check(host.Opens == 0 && host.Closes == 0 && host.Active, "Existing-menu timeout does not take ownership");

        host = new(); controller = new(); Open(controller, host);
        host.ReleaseWindow(); // passive input event transfers ownership to the player
        controller.Tick(C(3_300) with { CapturesAvailable = true, ManagerState = "Received" }, host);
        check(host.Closes == 0 && host.Active, "Player interaction cancels automatic close");
        controller.Tick(C(50_000), host);
        check(host.Opens == 1, "No automatic reopening after player takeover");
        host = new(); controller = new(); Open(controller, host);
        host.Active = false; host.ReleaseWindow();
        controller.Tick(C(3_300), host); controller.Tick(C(50_000), host);
        check(host.Opens == 1 && host.Closes == 0, "Manual close cancels the cycle without reopening");

        host = new(); controller = new(); Open(controller, host);
        controller.Tick(C(15_001), host);
        check(host.Closes == 1 && controller.Phase == "Stopped", "Twelve-second response timeout cleans up owned menu");
        controller.Tick(C(50_000), host);
        check(host.Opens == 1, "Timeout does not produce an automatic retry loop");
        controller.Retry(host); controller.Tick(C(50_001), host); controller.Tick(C(53_001), host);
        check(host.Opens == 2, "Explicit retry can request a new bounded cycle");
        controller.Retry(host); controller.Tick(C(53_101), host);
        check(host.Opens == 2, "Retry while loading cannot create overlapping jobs");

        host = new(); controller = new();
        controller.Tick(C(), host); controller.Tick(C(1) with { ManagerState = "Requested" }, host);
        controller.Tick(C(9_000) with { ManagerState = "Requested" }, host);
        check(host.Opens == 0, "Pending native request gets a grace period without opening another menu");
        controller.Tick(C(9_250) with { CapturesAvailable = true, ManagerState = "Received" }, host);
        check(host.Opens == 0 && controller.Phase == "Complete", "An existing response avoids the fallback");
        host = new(); controller = new();
        controller.Tick(C(), host); controller.Tick(C(1) with { ManagerState = "Requested" }, host);
        controller.Tick(C(10_001) with { ManagerState = "Requested" }, host);
        controller.Tick(C(13_001) with { ManagerState = "Requested" }, host);
        check(host.Opens == 1, "Stalled native request eventually permits one safe menu fallback");

        host = new(); controller = new(); controller.Tick(C(), host);
        controller.Tick(C(1) with { ReaderAvailable = false }, host); controller.Tick(C(50_000), host);
        check(host.Opens == 0 && controller.Phase == "Unavailable", "Missing capture API fails closed without opening menus");
        host = new(); controller = new(); controller.Tick(C(), host);
        controller.Tick(C(1) with { MetadataAvailable = false }, host);
        controller.Tick(C(50_000) with { MetadataAvailable = false }, host);
        check(host.Opens == 0, "Missing local metadata does not prompt a speculative menu request");
        host = new(); controller = new(); controller.Tick(C(), host);
        controller.Tick(C(1) with { ManagerState = "Received" }, host);
        check(host.Opens == 0 && controller.Phase == "Stopped", "Received but invalid capture count is not fixed by opening a menu");
        host = new() { CanOpen = false }; controller = new(); Open(controller, host);
        check(host.Opens == 0, "Respect native activation/job/unlock restrictions");
        host = new() { SucceedOpen = false }; controller = new(); Open(controller, host);
        controller.Tick(C(50_000), host);
        check(host.Opens == 1 && host.Closes == 0, "Native open failure consumes the one automatic attempt");

        host = new(); controller = new(); Open(controller, host);
        controller.Tick(C(3_300) with { UiAvailable = false }, host);
        controller.Tick(C(10_000), host);
        check(host.Closes == 0 && host.Opens == 1 && !host.OwnsWindow, "Loading/UI replacement cancels saved close actions");
        host = new(); controller = new(); Open(controller, host);
        controller.Tick(C(3_300) with { SafeToOpen = false, SafeToContinue = true }, host);
        check(host.Closes == 0, "A notebook's own occupied state does not abort capture loading");
        controller.Tick(C(3_600) with { SafeToOpen = false, SafeToContinue = false }, host);
        check(host.Closes == 1, "Combat/cutscene interruption closes only an owned current window");
        host = new(); controller = new(); Open(controller, host);
        controller.Tick(C(3_300) with { LoggedIn = false }, host);
        check(host.Closes == 0 && !host.OwnsWindow && controller.OpenAttempts == 0, "Logout abandons old addresses and resets per-login state");
        controller.Tick(C(4_000) with { CharacterId = 2 }, host);
        host.Active = false;
        controller.Tick(C(4_001) with { CharacterId = 2 }, host);
        controller.Tick(C(7_001) with { CharacterId = 2 }, host);
        check(host.Opens == 2, "Another character gets its own single startup attempt");

        host = new(); controller = new(); controller.Tick(C(), host);
        controller.Tick(C(1) with { Enabled = false }, host); controller.Tick(C(50_000) with { Enabled = false }, host);
        check(host.Opens == 0 && controller.Phase == "Disabled", "Setting can disable automatic menu initialization");
        controller.Retry(host); controller.Tick(C(50_001) with { Enabled = false }, host);
        controller.Tick(C(53_001) with { Enabled = false }, host);
        check(host.Opens == 1, "Explicit initialization works independently of the automatic setting");
        host = new(); controller = new(); Open(controller, host);
        controller.Tick(C(3_300) with { Enabled = false }, host);
        check(host.Closes == 1 && !host.OwnsWindow, "Disabling during loading cleans up owned menu");
        host = new(); controller = new(); Open(controller, host);
        controller.Stop(C(3_300), host);
        check(host.Closes == 1 && !host.OwnsWindow, "Framework-thread disposal cleans up only owned menu");
        host = new() { Active = true }; controller = new(); controller.Tick(C(), host); controller.Tick(C(1), host);
        controller.Stop(C(500), host);
        check(host.Closes == 0 && host.Active, "Disposal cannot close a player's pre-existing menu");

        var ownership = new BestiaryWindowOwnership();
        check(!ownership.CanClose(1, 2, 3, true), "No close without an ownership token");
        ownership.Claim(1); ownership.Observe(1, 0, 0, true);
        check(ownership.IsOwned && !ownership.CanClose(1, 0, 0, true), "Async opening awaits the actual addon identity");
        ownership.Observe(1, 2, 3, true);
        check(ownership.CanClose(1, 2, 3, true), "Matching active agent/addon/ID may be closed");
        check(!ownership.CanClose(1, 2, 4, true) && !ownership.CanClose(1, 2, 3, false), "ID change or inactive state prevents close");
        ownership.Observe(1, 4, 5, true);
        check(!ownership.IsOwned, "Replacement addon revokes ownership even on the same agent");
        ownership.Claim(1); ownership.Observe(1, 2, 3, true); ownership.Observe(9, 2, 3, true);
        check(!ownership.IsOwned, "Replacement agent revokes ownership");
        ownership.Claim(1); ownership.Observe(1, 2, 3, true); ownership.Observe(1, 2, 3, false);
        check(!ownership.IsOwned, "Hidden/closed window revokes ownership");
        foreach (var input in new[] { "MouseDown", "MouseClick", "MouseWheel", "InputReceived", "InputNavigation", "ButtonPress", "ListItemClick" })
            check(BestiaryUserInput.IsInteraction(input), "User takeover input: " + input);
        foreach (var notification in new[] { "MouseOver", "MouseOut", "FocusStart", "FocusStop", "ValueUpdate", "" })
            check(!BestiaryUserInput.IsInteraction(notification), "Passive notification is not player takeover: " + notification);
    }
}
