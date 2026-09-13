// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using Beastdex.Services;
using Beastdex.Windows;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Beastdex;

public sealed class Plugin : IDalamudPlugin
{
    public const string CommandName = "/bstgrind";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static INamePlateGui NamePlateGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IAetheryteList AetheryteList { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public static Configuration Configuration { get; private set; } = null!;
    internal static BeastTargetMarkerService TargetMarkers { get; private set; } = null!;

    private readonly WindowSystem windows = new("Beastdex");
    private readonly MainWindow mainWindow;
    private readonly BeastDataService beastData;
    private readonly XbmCapturedService captured;
    private readonly WorldSearchService world;
    private readonly TravelService travel;
    private readonly BestiaryPlanService plans;
    private int reloadMetadataRequested = 1;
    private readonly Stopwatch refreshTimer = Stopwatch.StartNew();

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        beastData = new BeastDataService();
        captured = new XbmCapturedService(beastData);
        world = new WorldSearchService(beastData);
        travel = new TravelService(world);
        plans = new BestiaryPlanService(beastData, captured, world);
        TargetMarkers = new BeastTargetMarkerService(world, captured, plans);
        mainWindow = new MainWindow(beastData, captured, world, travel, plans, RequestMetadataReload);
        windows.AddWindow(mainWindow);
        windows.AddWindow(mainWindow.SpawnWindow);
        windows.AddWindow(mainWindow.CompactWindow);
        windows.AddWindow(mainWindow.SettingsWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Beastdex. Options: compact, full, settings, refresh (captures), initialize (load bestiary data), reload (game metadata).",
        });

        Framework.Update += OnFrameworkUpdate;
        ClientState.Login += OnLogin;
        ClientState.Logout += OnLogout;
        ClientState.TerritoryChanged += OnTerritoryChanged;
        ClientState.ClassJobChanged += OnClassJobChanged;
        ClientState.LevelChanged += OnLevelChanged;
        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ShowSettings;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ClientState.Login -= OnLogin;
        ClientState.Logout -= OnLogout;
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        ClientState.ClassJobChanged -= OnClassJobChanged;
        ClientState.LevelChanged -= OnLevelChanged;
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ShowSettings;
        CommandManager.RemoveHandler(CommandName);
        TargetMarkers.Dispose();
        windows.RemoveAllWindows();
        mainWindow.Dispose();
        captured.Dispose();
        world.Dispose();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // Initialization must run even with capture polling paused or all windows closed.
        captured.TickStartup();
        world.Tick();
        travel.Tick();
        TargetMarkers.Tick();
        var forced = captured.ConsumeRefreshRequest();
        var reloadMetadata = Volatile.Read(ref reloadMetadataRequested) != 0;
        if (!RefreshPolicy.IsDue(refreshTimer.ElapsedMilliseconds, Configuration.AutoRefreshBestiary,
            Configuration.BestiaryRefreshSeconds, forced || reloadMetadata))
            return;

        // Both native game calls and metadata refreshes stay off the ImGui draw path.
        if (Interlocked.Exchange(ref reloadMetadataRequested, 0) != 0 || !beastData.Available)
        {
            beastData.Reload();
            world.RequestIndex();
        }
        captured.Refresh();
        refreshTimer.Restart();
    }

    private void OnLogin()
    {
        captured.ResetStartupSession();
        captured.Clear("Waiting for the current character's Beastmaster data.");
        captured.RequestRefresh();
    }

    private void OnLogout(int type, int code)
    {
        captured.ResetStartupSession();
        captured.Clear("Not logged in. Waiting for character data.");
        captured.RequestRefresh();
    }

    internal static void SaveConfiguration() => PluginInterface.SavePluginConfig(Configuration);

    private void RequestMetadataReload()
    {
        Interlocked.Exchange(ref reloadMetadataRequested, 1);
        captured.RequestRefresh();
    }

    private void OnTerritoryChanged(uint id) { if (Configuration.AutoRefreshBestiary) captured.RequestRefresh(); }
    private void OnClassJobChanged(uint id) { if (Configuration.AutoRefreshBestiary) captured.RequestRefresh(); }
    private void OnLevelChanged(uint job, uint level) { if (Configuration.AutoRefreshBestiary) captured.RequestRefresh(); }

    private void DrawUi()
    {
        // All style changes are plugin-local and balanced even if a draw throws.
        using var theme = UiTheme.Push();
        windows.Draw();
    }

    private void ShowSettings() => mainWindow.ShowSettings();

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "compact": mainWindow.ShowCompact(); return;
            case "full": mainWindow.ShowFull(); return;
            case "settings": ShowSettings(); return;
            case "reload": RequestMetadataReload(); break;
            case "refresh": captured.RequestRefresh(); break;
            case "initialize": captured.RequestInitialization(); return;
        }
        mainWindow.OpenPreferred();
    }

    private void ToggleMainUi() => mainWindow.TogglePreferred();
}
