# SPDX-License-Identifier: GPL-3.0-only
"""Static assertions against the actual startup integration. NOT C# execution or a game test."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET
from compact_visibility_checks import body

ROOT = Path(__file__).resolve().parents[1]

def read(name):
    return (ROOT/name).read_text(encoding='utf-8')

class StartupSourceChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.plugin = read('Plugin.cs')
        cls.service = read('Services/BestiaryStartupService.cs')
        cls.policy = read('Services/BestiaryStartupController.cs')
        cls.host = read('Interop/BestiaryMenuHost.cs')
        cls.capture = read('Services/XbmCapturedService.cs')

    def test_default_enabled_independent_of_polling(self):
        self.assertIn('bool AutoInitializeBestiary { get; set; } = true;', read('Configuration.cs'))
        update = body(self.plugin, 'private void OnFrameworkUpdate')
        self.assertLess(update.index('captured.TickStartup();'), update.index('RefreshPolicy.IsDue'))

    def test_once_per_session_not_on_zone_or_level_changes(self):
        for name in ('OnLogin()', 'OnLogout('):
            self.assertIn('captured.ResetStartupSession();', body(self.plugin, 'private void ' + name))
        for name in ('OnTerritoryChanged(', 'OnClassJobChanged(', 'OnLevelChanged('):
            self.assertNotIn('ResetStartup', body(self.plugin, 'private void ' + name))
        self.assertIn('if (finished)', self.policy)
        self.assertIn('attemptUsed = true; // consume BEFORE the native call', self.policy)

    def test_clear_and_reread_on_character_replacement(self):
        self.assertIn('Plugin.PlayerState.ContentId != lastCharacter', self.service)
        self.assertIn('captures.Clear(', self.service)
        self.assertIn('captures.RequestRefresh();', self.service)
        self.assertIn('never consume a snapshot from the previous character', self.policy)

    def test_capture_read_is_separate_from_loading(self):
        refresh = body(self.capture, 'public void Refresh()')
        self.assertNotIn('TryOpen(', refresh)
        self.assertNotIn('Show(', refresh)
        self.assertIn('var result = manager.Read(knownRows);', refresh)

    def test_current_mapped_enum_not_hardcoded_id(self):
        self.assertIn('Enum.TryParse(AgentName, out agentId)', self.host)
        self.assertIn('Enum.IsDefined(agentId)', self.host)
        self.assertNotIn('(AgentId)500', self.host)
        self.assertNotIn('GetAgentByInternalId(500', self.host)

    def test_native_call_site_rechecks_game_state(self):
        opening = body(self.host, 'public unsafe bool TryOpen(')
        for guard in ('Plugin.Framework.IsInFrameworkUpdateThread', 'BestiaryStartupService.SafeToOpen()',
                      'capturesAvailable()', 'current.Active', 'agent->IsActivatable()'):
            self.assertIn(guard, opening)
        self.assertIn('agent->Show();', opening)
        self.assertIn('ownerCharacter = Plugin.PlayerState.ContentId;', opening)

    def test_does_not_write_captures_or_forge_requests(self):
        for source in (self.host, self.service, self.policy):
            for unwanted in ('SetPetUnlocked(', 'HandlePetListPacket(', 'SendPacket(', 'ProcessChatBox(', 'FireCallback('):
                self.assertNotIn(unwanted, source)

    def test_existing_and_player_taken_over_windows_left_alone(self):
        self.assertIn('if (current.Active)', self.policy)
        self.assertIn('if (!host.OwnsWindow)', self.policy)
        self.assertIn('BestiaryUserInput.IsInteraction(input.AtkEventType.ToString())', self.host)
        self.assertIn('ownership.Release("Player interaction detected', self.host)

    def test_passive_addon_notifications_do_not_steal_ownership(self):
        self.assertNotIn('AddonEvent.PreMove', self.host) # Native initial placement may also Move.
        for event in ('PreFinalize', 'PreHide', 'PreClose', 'PreReceiveEvent'):
            self.assertIn('AddonEvent.' + event, self.host)
        self.assertNotIn('PreventOriginal(', self.host)

    def test_close_checks_current_character_and_window_identity(self):
        close = body(self.host, 'public unsafe bool TryClose(')
        self.assertIn('ownerCharacter != Plugin.PlayerState.ContentId', close)
        self.assertIn('ownership.CanClose((nint)agent, (nint)addon, addon->Id, active)', close)
        self.assertIn('agent->Hide();', close)
        self.assertIn('agent == Agent && addon == Addon && id == AddonId', self.policy)

    def test_no_scheduled_old_close_after_transition(self):
        self.assertIn('if (!input.UiAvailable)', self.policy)
        self.assertIn('no delayed close or retry is queued', self.policy)
        self.assertNotIn('Task.Delay(', self.host + self.service + self.policy)

    def test_bounded_delay_timeout_and_pending_request_grace(self):
        for expected in ('SafeDelayMs = 3_000', 'ResponseTimeoutMs = 12_000',
                         'ExistingRequestGraceMs = 10_000', 'PollIntervalMs = 250'):
            self.assertIn(expected, self.policy)
        self.assertIn('if (input.ManagerState == "Requested")', self.policy)
        self.assertIn('if (input.ManagerState == "Received")', self.policy)

    def test_own_notebook_occupied_state_does_not_abort_load(self):
        self.assertIn('bool SafeToContinue = true', self.policy)
        self.assertIn('if (!input.SafeToContinue)', self.policy)
        continuing = body(self.service, 'internal static bool SafeToContinue()')
        self.assertNotIn('c[ConditionFlag.Occupied]', continuing)
        self.assertIn('ConditionFlag.InCombat', continuing)
        self.assertIn('ConditionFlag.WatchingCutscene', continuing)

    def test_listener_failures_do_not_crash_plugin_construction(self):
        constructor = body(self.host, 'public BestiaryMenuHost(')
        self.assertIn('catch (Exception ex)', constructor)
        self.assertIn('listening = false;', constructor)
        self.assertNotIn('throw;', constructor)
        self.assertIn('UnregisterListener(OnAddonEvent)', constructor)

    def test_disposal_unsubscribes_and_avoids_unobserved_async_actions(self):
        self.assertIn('captured.Dispose();', self.plugin)
        self.assertIn('if (Plugin.Framework.IsInFrameworkUpdateThread) controller.Stop', self.service)
        self.assertIn('finally { host.Dispose(); }', self.service)
        self.assertIn('Plugin.AddonLifecycle.UnregisterListener(OnAddonEvent)', body(self.host, 'public void Dispose()'))

    def test_command_setting_and_diagnostics_are_wired(self):
        self.assertIn('case "initialize": captured.RequestInitialization(); return;', self.plugin)
        settings = read('Windows/SettingsWindow.cs')
        self.assertIn('Load bestiary after login', settings)
        self.assertIn('captured.RequestInitialization();', settings)
        self.assertIn('text.Append(startup.Diagnostics);', self.capture)

    def test_initialization_does_not_touch_plugin_window_visibility(self):
        for source in (self.host, self.service, self.policy):
            for window in ('CompactWindow', 'MainWindow', 'SpawnWindow', 'SettingsWindow', 'PreferCompact'):
                self.assertNotIn(window, source)

    def test_managed_startup_tests_and_all_includes_exist(self):
        self.assertIn('StartupTests.Run(Check);', read('Tests/Program.cs'))
        includes = [n.get('Include') for n in ET.parse(ROOT/'Tests/Beastdex.Tests.csproj').findall('.//Compile')]
        self.assertIn('StartupTests.cs', includes)
        self.assertIn('../Services/BestiaryStartupController.cs', includes)
        for entry in includes:
            self.assertTrue((ROOT/'Tests'/entry).is_file(), entry)

if __name__ == '__main__':
    unittest.main(verbosity=2)
