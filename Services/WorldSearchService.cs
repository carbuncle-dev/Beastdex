// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Beastdex.Models;
using Dalamud.Game.ClientState.Objects.Types;
using Lumina.Excel.Sheets;

namespace Beastdex.Services;

public sealed class WorldSearchService : IDisposable
{
    private readonly BeastDataService beasts;
    private readonly CancellationTokenSource cancellation = new();
    private Task<WorldIndex>? buildTask;
    private CancellationTokenSource? buildCancellation;
    private Task<DiscoveryResult>? discoveryTask;
    private volatile string progress = "World data has not been indexed yet.";
    private volatile bool requested = true;
    private volatile bool forcePublicRefresh;
    private volatile bool clearRequested;
    private volatile bool regroupRequested;
    private int requestedGeneration;
    private int buildingGeneration;
    private WorldIndex? activeDiscoveryIndex;
    private bool discoveryRequested;
    private readonly Stopwatch observeTimer = Stopwatch.StartNew();
    private readonly Stopwatch saveTimer = Stopwatch.StartNew();
    private readonly Dictionary<string, SpawnPoint> observations = [];
    private DiscoveryResult discovery = new([], []);
    private SpawnPoint[] observationSnapshot = [];
    private readonly string directory;
    private readonly string cachePath;
    private readonly string cacheStamp;
    private string observationStatus = "No nearby enemy observations yet.";
    public WorldIndex Index { get; private set; } = WorldIndex.Empty;
    public bool IsIndexing => buildTask != null || discoveryTask != null;
    public string Status => buildTask != null ? progress :
        $"{Index.LayoutPoints.Count} client-layout points; {Index.CommunityPoints.Count} community records; " +
        $"{observationSnapshot.Length} local observations; {discovery.Groups.Values.Sum(g => g.Length)} grouped options." +
        (discoveryTask != null ? " Updating groups..." : "");
    public string CommunityStatus => Index.CommunityStatus;
    public int? BstLevel { get; private set; }
    public int ObservationCount => observationSnapshot.Length;
    public int Revision { get; private set; }
    public string ObservationStatus => observationStatus;

    public WorldSearchService(BeastDataService beasts)
    {
        this.beasts = beasts;
        directory = Plugin.PluginInterface.GetPluginConfigDirectory();
        cachePath = Path.Combine(directory, "mob-observations-v1.json");
        cacheStamp = $"{typeof(FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState).Assembly.GetName().Version}|{Plugin.DataManager.Language}";
        LoadObservations();
        observationSnapshot = observations.Values.ToArray();
    }

    public void RequestIndex(bool refreshPublic = false)
    {
        Interlocked.Increment(ref requestedGeneration);
        if (refreshPublic) forcePublicRefresh = true;
        requested = true;
    }
    public void RequestRegroup() => regroupRequested = true;
    public void RequestClearObservations() => clearRequested = true;
    public SpawnGroup[] GetGroups(uint beastId) => discovery.Groups.GetValueOrDefault(beastId) ?? [];
    public SpawnGroup[] GetAreaGroups(uint beastId) => discovery.HintAreaGroups.GetValueOrDefault(beastId) ?? [];
    public SpawnCandidate[] GetCandidates(uint beastId) => GetGroups(beastId).Select(g => g.Representative).ToArray();
    public uint[] GetTerritories(uint beastId) => Index.HintTerritories.GetValueOrDefault(beastId) ?? [];
    public string AreaName(SpawnPoint point) => point.DutyId != 0 && Index.Duties.TryGetValue(point.DutyId, out var duty)
        ? duty.Name : Index.Territories.GetValueOrDefault(point.TerritoryId)?.Name ??
            (point.AreaLabel.Length > 0 ? point.AreaLabel : $"Territory {point.TerritoryId}");

    public void Tick()
    {
        if (clearRequested)
        {
            clearRequested = false;
            observations.Clear();
            ScheduleDiscovery();
            observationStatus = "Observation cache cleared.";
            SaveObservations();
        }
        if (regroupRequested) { regroupRequested = false; ScheduleDiscovery(); }
        if (requested && buildTask != null && buildingGeneration != Volatile.Read(ref requestedGeneration))
            buildCancellation?.Cancel();
        if (buildTask is { IsCompleted: true })
        {
            try
            {
                var result = buildTask.GetAwaiter().GetResult();
                // Settings can change during a download. Never publish a superseded provider selection.
                if (buildingGeneration == Volatile.Read(ref requestedGeneration))
                {
                    Index = result;
                    discovery = new([], []);
                    Revision++;
                    ScheduleDiscovery();
                    progress = "World index ready.";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                progress = $"World index failed: {ex.GetType().Name}: {ex.Message}";
                observationStatus = progress;
                Plugin.Log.Warning(ex, "World-data indexing failed; captured bestiary remains independent.");
            }
            finally { buildTask = null; buildCancellation?.Dispose(); buildCancellation = null; }
        }
        if (requested && buildTask == null && beasts.Available)
        {
            requested = false;
            buildingGeneration = Volatile.Read(ref requestedGeneration);
            var snapshot = beasts.Beasts.Values.ToArray();
            var data = Plugin.DataManager;
            var captureReports = Plugin.Configuration.UseCommunityCaptureReports;
            var spawnObservations = Plugin.Configuration.UseCommunitySpawnObservations;
            var force = forcePublicRefresh;
            forcePublicRefresh = false;
            buildCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            var token = buildCancellation.Token;
            buildTask = Task.Run(async () =>
            {
                var local = WorldIndexBuilder.Build(data, snapshot, token, s => progress = s);
                return await CommunityWorldLoader.LoadAsync(local, snapshot.Select(b => b.RowId).ToHashSet(),
                    directory, captureReports, spawnObservations, force, s => progress = s, token).ConfigureAwait(false);
            }, token);
        }
        if (discoveryTask is { IsCompleted: true })
        {
            try
            {
                var result = discoveryTask.GetAwaiter().GetResult();
                if (ReferenceEquals(activeDiscoveryIndex, Index))
                { discovery = result; Revision++; }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                observationStatus = $"Grouping unavailable: {ex.Message}";
                Plugin.Log.Warning(ex, "Spawn grouping failed.");
            }
            finally { discoveryTask = null; }
        }
        if (discoveryRequested && discoveryTask == null && Index.Territories.Count != 0)
        {
            discoveryRequested = false;
            activeDiscoveryIndex = Index;
            var index = Index;
            var familiarSnapshot = beasts.Beasts.Values.ToArray();
            var points = observationSnapshot;
            var radius = Plugin.Configuration.SpawnGroupRadius;
            var token = cancellation.Token;
            discoveryTask = Task.Run(() => SpawnDiscovery.Build(index, familiarSnapshot, points, radius, token), token);
        }
        if (observeTimer.ElapsedMilliseconds < 2000) return;
        observeTimer.Restart();
        UpdateBstLevel();
        if (Plugin.Configuration.ObserveNearbyMobs) Observe();
        if (saveTimer.ElapsedMilliseconds > 60000)
        { saveTimer.Restart(); SaveObservations(); }
    }

    private void ScheduleDiscovery()
    {
        observationSnapshot = observations.Values.ToArray();
        discoveryRequested = true;
    }

    private void UpdateBstLevel()
    {
        BstLevel = null;
        if (!Plugin.ClientState.IsLoggedIn || !Plugin.PlayerState.IsLoaded) return;
        try
        {
            foreach (var row in Plugin.DataManager.GetExcelSheet<ClassJob>())
                if (row.Abbreviation.ExtractText().Equals("BST", StringComparison.OrdinalIgnoreCase))
                {
                    var level = Plugin.PlayerState.GetClassJobLevel(row);
                    if (level > 0) BstLevel = level;
                    break;
                }
        }
        catch (Exception ex) { observationStatus = $"BST level unavailable: {ex.Message}"; }
    }


    private unsafe void Observe()
    {
        if (!Plugin.ClientState.IsLoggedIn || Plugin.ClientState.IsPvP || Plugin.ClientState.IsGPosing ||
            Plugin.ObjectTable.LocalPlayer == null) return;
        var territoryId = Plugin.ClientState.TerritoryType;
        // All mapped areas/duties, not just bestiary hints: cross-area alternatives can be discovered locally too.
        if (!Index.Territories.TryGetValue(territoryId, out var territory)) return;
        var dirty = false;
        var read = 0;
        try
        {
            foreach (var npc in Plugin.ObjectTable.OfType<IBattleNpc>())
            {
                if (!SpawnMatching.IsUnownedCombatNpc((byte)npc.BattleNpcKind, npc.OwnerId) || !npc.IsTargetable || npc.IsDead ||
                    npc.NameId == 0 || npc.BaseId == 0 || npc.Address == 0) continue;
                if (npc.StatusFlags.HasFlag(Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat)) continue;
                var pos = npc.Position;
                if (!SpawnMatching.ValidPosition(pos.X, pos.Y, pos.Z)) continue;
                var mapId = territory.MapIds.Length == 1 ? territory.MapIds[0] : 0;
                var name = npc.Name.TextValue.Trim();
                if (name.Length == 0) continue;
                // Read the installed, mapped field on Framework.Update; never retain an actor/pointer.
                var rawFate = ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)npc.Address)->FateId;
                uint? fate = rawFate == ushort.MaxValue ? null : rawFate;
                read++;
                var point = new SpawnPoint(territoryId, mapId, npc.BaseId, npc.NameId, name,
                    npc.Level > 0 ? npc.Level : null, pos.X, pos.Y, pos.Z,
                    SpawnEvidence.Observed, "Nearby unowned combat NPC, outside combat",
                    "Observed position, not guaranteed spawn point; a non-FATE flag alone does not exclude a hunt/quest enemy.",
                    DateTimeOffset.UtcNow)
                {
                    DutyId = territory.DutyId, FateId = fate,
                    Encounter = fate.HasValue ? fate.Value > 0 ? EncounterKind.Fate : EncounterKind.Regular : EncounterKind.Unknown,
                    EnglishName = Index.EnglishNpcNames.GetValueOrDefault(npc.NameId) ?? "",
                };
                var key = ObservationKey(point);
                if (observations.ContainsKey(key)) continue;
                if (observations.Count >= 4000)
                    observations.Remove(observations.OrderBy(x => x.Value.ObservedAt).First().Key);
                observations[key] = point;
                dirty = true;
            }
            observationStatus = $"Read {read} nearby unowned, idle combat NPCs in territory {territoryId}; cached {observations.Count} positions.";
            if (dirty) ScheduleDiscovery();
        }
        catch (Exception ex) { observationStatus = $"Nearby read unavailable: {ex.GetType().Name}: {ex.Message}"; }
    }

    private static string ObservationKey(SpawnPoint p) =>
        $"{p.TerritoryId}:{p.MapId}:{p.BaseId}:{p.NameId}:{p.Level}:{p.Encounter}:{p.FateId}:" +
        $"{Math.Floor(p.X / 8)}:{Math.Floor(p.Y / 8)}:{Math.Floor(p.Z / 8)}";

    private sealed record ObservationCache(string Stamp, SpawnPoint[] Points);
    private void LoadObservations()
    {
        try
        {
            if (!File.Exists(cachePath) || new FileInfo(cachePath).Length > 8_000_000) return;
            var cache = JsonSerializer.Deserialize<ObservationCache>(File.ReadAllText(cachePath));
            if (cache == null || cache.Stamp != cacheStamp) return;
            foreach (var point in (cache.Points ?? []).Take(4000))
                if (point != null && point.HasCoordinates && point.Evidence == SpawnEvidence.Observed && point.TerritoryId != 0 &&
                    point.BaseId != 0 && point.NameId != 0 && !string.IsNullOrWhiteSpace(point.Name) &&
                    point.Name.Length < 300 && (point.Level == null || point.Level is > 0 and <= 255) &&
                    SpawnMatching.ValidPosition(point.X, point.Y, point.Z))
                    observations[ObservationKey(point)] = point;
            observationStatus = $"Restored {observations.Count} historical NPC observations (capture still unverified).";
        }
        catch (Exception ex) { observationStatus = $"Observation cache not loaded: {ex.Message}"; }
    }

    public void SaveObservations()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var temp = cachePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new ObservationCache(cacheStamp, observations.Values.ToArray())));
            File.Move(temp, cachePath, overwrite: true);
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Could not save local NPC observations."); }
    }


    public string GetDiagnostics()
    {
        var text = new StringBuilder();
        text.AppendLine($"World index: {Status}");
        text.AppendLine($"Progress: {progress}");
        text.AppendLine($"Public providers (opt-in): Collect={Plugin.Configuration.UseCommunityCaptureReports}; Teamcraft={Plugin.Configuration.UseCommunitySpawnObservations}");
        text.AppendLine(Index.CommunityStatus);
        text.AppendLine($"BST level: {BstLevel?.ToString() ?? "unknown"}; nearby: {observationStatus}");
        text.AppendLine($"Observation stamp: {cacheStamp}; entries: {observationSnapshot.Length}; grouping radius: {Plugin.Configuration.SpawnGroupRadius} world yalms");
        text.AppendLine("Preference: reported sources at or below BST level, including equality: common overworld > dungeon/trial > FATE > elite hunt. Lowest enemy level breaks ties within a tier; future/unknown levels are not level-ready, and unresolved conditions are not common wildlife.");
        text.AppendLine($"Hunt catalog available={Index.HuntCatalogAvailable}; elite hunt names={Index.HuntNameIds.Count}.");
        text.AppendLine(Index.Diagnostics);
        text.AppendLine($"Unresolved public source lines: {Index.UnresolvedReports.Length}");
        foreach (var line in Index.UnresolvedReports.Take(150)) text.AppendLine("  " + line);
        foreach (var beast in beasts.Beasts.Values.OrderBy(b => b.RowId))
        {
            var entries = GetGroups(beast.RowId);
            var preferred = SourceReconciler.Preferred(SourceReconciler.Build(beast, entries, Index), Index, BstLevel);
            text.AppendLine($"XBMPet {beast.RowId}: {entries.Length} groups; preferred={preferred?.Point.Name ?? "none"} Lv={preferred?.Point.LevelText ?? "unknown"}.");
            foreach (var group in entries.Take(20))
            {
                var point = group.Point;
                text.AppendLine($"  {point.Name} base={point.BaseId} nameId={point.NameId} Lv={point.LevelText} territory={point.TerritoryId} duty={point.DutyId} map={point.MapId} " +
                    $"coordinates={point.HasCoordinates} pos=({point.X},{point.Y},{point.Z}) samples={group.SampleCount} radius={group.Radius:0.0} " +
                    $"encounter={point.Encounter} fate={point.FateId} possibleNameIds=[{string.Join(",", point.PossibleNameIds)}] reported={group.IsReported}; {string.Join(", ", group.EvidenceLabels)}");
                var option = new CaptureOption(beast.RowId, point, group, false, false, group.Key);
                text.AppendLine($"    Tier={EncounterPolicy.Tier(option, Index)}; ready={EncounterPolicy.IsLevelReady(point, BstLevel)}; type={EncounterPresentation.Describe(option, Index).Label}.");
                text.AppendLine($"    {group.Representative.CaptureAttribution}; {point.Source}; {point.Conditions}");
            }
        }
        return text.ToString();
    }

    public void Dispose()
    {
        cancellation.Cancel();
        SaveObservations();
        // No native access in either worker. Dispose the token source only after both have stopped.
        var tasks = new Task?[] { buildTask, discoveryTask }.Where(t => t != null).Cast<Task>().ToArray();
        var child = buildCancellation;
        if (tasks.Length == 0) { child?.Dispose(); cancellation.Dispose(); }
        else _ = Task.WhenAll(tasks).ContinueWith(t => { _ = t.Exception; child?.Dispose(); cancellation.Dispose(); }, TaskScheduler.Default);
    }
}
