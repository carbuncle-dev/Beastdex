// SPDX-License-Identifier: GPL-3.0-only
using Dalamud.Configuration;
using Beastdex.Models;

namespace Beastdex;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 6;
    public bool ShowBeastTargetMarkers { get; set; }
    public bool TargetMarkersMissingOnly { get; set; } = true;
    public bool TargetMarkersWithinLevel { get; set; } = true;
    public BestiarySort Sort { get; set; } = BestiarySort.NextCatch;
    public bool PreferCompact { get; set; }
    public bool LockCompactPosition { get; set; }
    public bool AutoInitializeBestiary { get; set; } = true;
    public bool AutoRefreshBestiary { get; set; } = true;
    public int BestiaryRefreshSeconds { get; set; } = 2;
    public float WindowOpacity { get; set; } = .97f;
    public CaptureFilter CaptureFilter { get; set; } = CaptureFilter.All;
    public bool ObserveNearbyMobs { get; set; } = true;
    public bool UseCommunityCaptureReports { get; set; }
    public bool UseCommunitySpawnObservations { get; set; }
    public float SpawnGroupRadius { get; set; } = 160;
    public bool FilterByLevel { get; set; }
    public bool IncludeUnknownLevels { get; set; } = true;
    public bool FollowBstLevel { get; set; } = true;
    public int MaximumMobLevel { get; set; } = 100;
}
