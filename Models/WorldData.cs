// SPDX-License-Identifier: GPL-3.0-only
namespace Beastdex.Models;

public sealed record TerritoryInfo(uint Id, string Name, string Bg, uint PlaceNameId,
    uint DutyId, uint DefaultMapId, uint[] MapIds);
public sealed record MapInfo(uint Id, uint TerritoryId, uint PlaceNameId, uint SubPlaceNameId,
    string Name, int SizeFactor, int OffsetX, int OffsetY);
public sealed record DutyInfo(uint Id, uint TerritoryId, string Name, string EnglishName)
{
    public uint ContentTypeId { get; init; }
}
public sealed record AetheryteInfo(uint Id, uint TerritoryId, uint MapId, string Name,
    float? X, float? Z);
public sealed record TravelDestination(AetheryteInfo Aetheryte, byte SubIndex, uint GilCost);
// Preserve values 0/1 for compatibility with existing observation caches.
public enum SpawnEvidence { ClientLayout, Observed, CommunityObservation, CommunityReport }
public enum NameMatchKind { None, Exact, NameVariant, CommunityReport }
public enum EncounterKind { Unknown, Regular, Fate, Hunt, Conditional }
public enum CaptureFilter { All, Captured, Missing }

/// <summary>Location evidence. CaptureBeastId is used ONLY for a directly attributed capture report.</summary>
public sealed record SpawnPoint(uint TerritoryId, uint MapId, uint BaseId, uint NameId,
    string Name, int? Level, float X, float Y, float Z, SpawnEvidence Evidence,
    string Source, string Conditions, DateTimeOffset? ObservedAt = null)
{
    public bool HasCoordinates { get; init; } = true;
    public bool HasWorldY { get; init; } = true;
    public uint? FateId { get; init; }
    public EncounterKind Encounter { get; init; } = EncounterKind.Unknown;
    public uint DutyId { get; init; }
    public uint CaptureBeastId { get; init; }
    // A display name is not a globally unique BNpcName key. Keep all exact name IDs
    // until territory, level and encounter evidence can bind a particular instance.
    public uint[] PossibleNameIds { get; init; } = [];
    public string AreaLabel { get; init; } = string.Empty;
    public string EnglishName { get; init; } = string.Empty;
    // Community aggregate baseid is not a per-position identity: don't use it for capture inference.
    public bool BaseIdReliable { get; init; } = true;
    public string LevelText => Level?.ToString() ?? "Unknown";
    public string EvidenceLabel => Evidence switch
    {
        SpawnEvidence.Observed => "Observed in your client",
        SpawnEvidence.ClientLayout => "Client layout",
        SpawnEvidence.CommunityObservation => "Teamcraft observation",
        _ => "FFXIV Collect report",
    };
    public string EncounterLabel => Encounter switch
    {
        EncounterKind.Regular => Evidence == SpawnEvidence.CommunityReport ? "Ordinary source (reported)" : "Non-FATE observation",
        EncounterKind.Fate => FateId is > 0 ? $"FATE #{FateId}" : "FATE",
        EncounterKind.Hunt => "Hunt mark",
        EncounterKind.Conditional => "Conditional spawn",
        _ => "Conditions unknown",
    };
}
public sealed record SpawnCandidate(SpawnPoint Point, NameMatchKind Match)
{
    public string CaptureAttribution { get; init; } = string.Empty;
    public bool IsReported => Match == NameMatchKind.CommunityReport;
    public string MatchLabel => Match switch
    {
        NameMatchKind.CommunityReport => "Community-reported capture source",
        NameMatchKind.Exact => "Name match; capture unverified",
        NameMatchKind.NameVariant => "Name variant; capture unverified",
        _ => "Not associated with this familiar",
    };
}

/// <summary>A group of same-NPC, same-level, same-area evidence, with one real representative point.</summary>
public sealed record SpawnGroup(SpawnCandidate Representative, SpawnCandidate[] Members, int SampleCount,
    float Radius, string Key)
{
    public SpawnPoint Point => Representative.Point;
    public bool IsReported => Members.Any(c => c.IsReported);
    public string[] EvidenceLabels => Members.Select(c => c.Point.EvidenceLabel).Distinct().ToArray();
}

public sealed record WorldIndex(
    IReadOnlyDictionary<uint, TerritoryInfo> Territories,
    IReadOnlyDictionary<uint, MapInfo> Maps,
    IReadOnlyDictionary<uint, AetheryteInfo> Aetherytes,
    IReadOnlyDictionary<uint, uint[]> HintTerritories,
    IReadOnlyList<SpawnPoint> LayoutPoints,
    string Diagnostics)
{
    public IReadOnlyDictionary<uint, DutyInfo> Duties { get; init; } = new Dictionary<uint, DutyInfo>();
    public IReadOnlyDictionary<uint, string> NpcNames { get; init; } = new Dictionary<uint, string>();
    public IReadOnlyDictionary<uint, string> EnglishNpcNames { get; init; } = new Dictionary<uint, string>();
    public IReadOnlyDictionary<uint, string> EnglishBeastNames { get; init; } = new Dictionary<uint, string>();
    public IReadOnlyDictionary<uint, string> EnglishPlaceNames { get; init; } = new Dictionary<uint, string>();
    public IReadOnlyList<SpawnPoint> CommunityPoints { get; init; } = Array.Empty<SpawnPoint>();
    // Elite hunt names/bases from this client's NotoriousMonster sheet, not daily hunt-bill targets.
    public IReadOnlySet<uint> HuntNameIds { get; init; } = new HashSet<uint>();
    public IReadOnlySet<uint> HuntBaseIds { get; init; } = new HashSet<uint>();
    public bool HuntCatalogAvailable { get; init; }
    public IReadOnlyDictionary<uint, uint> ContentTypeIcons { get; init; } = new Dictionary<uint, uint>();
    public uint OpenWorldIconId { get; init; }
    public uint BeastmasterJobId { get; init; }
    public uint BeastmasterIconId { get; init; }
    public string[] UnresolvedReports { get; init; } = [];
    public string CommunityStatus { get; init; } = "Community sources off (local game data only).";
    public static WorldIndex Empty { get; } = new(
        new Dictionary<uint, TerritoryInfo>(), new Dictionary<uint, MapInfo>(),
        new Dictionary<uint, AetheryteInfo>(), new Dictionary<uint, uint[]>(), [], "Not indexed.");
}
