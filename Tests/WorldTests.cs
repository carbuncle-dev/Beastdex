// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;
using Beastdex.Services;

internal static class WorldTests
{
    public static void Run(Action<bool, string> check)
    {
        check(SpawnMatching.MatchName("Dodo", "dodo") == NameMatchKind.Exact, "Name matching is case-insensitive");
        check(SpawnMatching.MatchName("Coeurl", "Young Coeurl") == NameMatchKind.NameVariant, "Whole-name modifier generates a candidate, not a verified capture");
        check(SpawnMatching.MatchName("Stag", "Stagnant Sprite") == NameMatchKind.None, "No substring false positive");
        check(SpawnMatching.MatchName("", "Dodo") == NameMatchKind.None, "Missing familiar name cannot produce a match");
        check(SpawnMatching.MatchName("Dodo", "Dodos") == NameMatchKind.None, "No unverified stemming or synonyms");
        check(SpawnMatching.NormalizeName("Ｃｏｅｕｒｌ") == "coeurl", "Unicode compatibility normalization");
        check(SpawnMatching.NormalizeName("  Antelope--Stag ") == "antelope stag", "Punctuation and whitespace normalization");

        check(SpawnMatching.IsUnownedCombatNpc(5, 0) && SpawnMatching.IsUnownedCombatNpc(5, 0xE0000000),
            "Documented combat NPC subtype supports both no-owner sentinels");
        check(!SpawnMatching.IsUnownedCombatNpc(2, 0) && !SpawnMatching.IsUnownedCombatNpc(5, 123),
            "Pets and owned actors are not recorded as world combat NPCs");
        var captured = SpawnMatching.ShowCapture(CaptureFilter.All, true, true);
        var missing = SpawnMatching.ShowCapture(CaptureFilter.All, true, false);
        check(captured && missing, "All view contains obtained and unobtained entries");
        check(SpawnMatching.ShowCapture(CaptureFilter.All, false, false), "Unavailable capture state still permits all view");
        check(!SpawnMatching.ShowCapture(CaptureFilter.Missing, false, false), "Unavailable does not become missing");
        check(!SpawnMatching.ShowCapture(CaptureFilter.Captured, false, true), "Unavailable does not become captured");
        check(SpawnMatching.ShowCapture(CaptureFilter.Missing, true, false) &&
            !SpawnMatching.ShowCapture(CaptureFilter.Missing, true, true), "Missing filter uses verified capture status");
        check(SpawnMatching.ShowCapture(CaptureFilter.Captured, true, true) &&
            !SpawnMatching.ShowCapture(CaptureFilter.Captured, true, false), "Captured filter uses verified capture status");

        TerritoryInfo[] territories = [
            new(10, "Area A", "ffxiv/a/level/a", 111, 0, 100, [100]),
            new(20, "Area B", "ffxiv/b/level/b", 222, 0, 200, [200]),
            new(30, "Duty", "ffxiv/d/level/d", 111, 77, 300, [300, 301]),
        ];
        MapInfo[] maps = [
            new(100, 10, 111, 123, "Area A subarea", 100, 0, 0),
            new(200, 20, 222, 0, "Area B", 100, 0, 0),
            new(300, 30, 111, 0, "Duty floor", 100, 0, 0),
        ];
        var beast = new BeastInfo(1, "Dodo", 123, "") { NameResolved = true, Location = new(1, 111, "Area A", true) };
        check(SpawnMatching.ResolveHint(beast, territories, maps).SequenceEqual(new uint[] {10}), "Area hint does not include a duty sharing the PlaceName");
        check(SpawnMatching.ResolveHint(beast with { Location = new(1, 123, "Subarea", true) }, territories, maps)
            .SequenceEqual(new uint[] {10}), "Map subarea name links to its containing territory");
        check(SpawnMatching.ResolveHint(beast with { Location = new(2, 77, "Duty", true) }, territories, maps)
            .SequenceEqual(new uint[] {30}), "Duty hint uses ContentFinderCondition rather than PlaceName");
        check(SpawnMatching.ResolveHint(beast with { Location = new(2, 88, "Duty", true) }, territories, maps, 30)
            .SequenceEqual(new uint[] {30}), "Duty's direct TerritoryType reference is accepted");
        check(SpawnMatching.ResolveHint(beast with { Location = new(3, 111, "Unknown", true) }, territories, maps).Length == 0,
            "Unknown hint kind cannot become an arbitrary destination");
        check(SpawnMatching.ResolveHint(beast with { Location = new(1, 999, "Area A", true) }, territories, maps).Length == 0,
            "Area names are not fuzzy-matched across sheet IDs");

        SpawnPoint Point(uint territory, int? level, string name = "Dodo") => new(territory, territory == 10 ? 100u : 200u,
            1000, 2000, name, level, 0, 0, 0, SpawnEvidence.Observed, "Synthetic fixture", "Not a capture source");
        var points = new[] { Point(10, 12), Point(20, 80), Point(10, 13), Point(10, null), Point(10, 99, "Unrelated") };
        var matches = SpawnMatching.Candidates(beast, new HashSet<uint> {10}, points);
        check(matches.Length == 3 && matches.All(m => m.Point.TerritoryId == 10), "Candidate levels stay in the hinted area");
        check(matches.Select(m => m.Point.Level).SequenceEqual(new int?[] {12, 13, null}), "Distinct NPC levels and unknown level preserved");
        check(SpawnMatching.Candidates(beast with { NameResolved = false }, new HashSet<uint> {10}, points).Length == 0,
            "Unresolved name never generates candidates");
        check(SpawnMatching.WithinLevel(12, 12, false) && !SpawnMatching.WithinLevel(13, 12, true), "Level ceiling inclusive, no above-level mobs");
        check(SpawnMatching.WithinLevel(null, 12, true) && !SpawnMatching.WithinLevel(null, 12, false), "Unknown level handling is explicit");
        check(!SpawnMatching.WithinLevel(0, 100, true), "Level zero is never treated as an easy capture");

        check(Math.Abs(SpawnMatching.MapCoordinate(0, 100, 0) - 21.48f) < 0.001f, "Map conversion at world origin");
        check(Math.Abs(SpawnMatching.MapCoordinate(-100, 200, 100) - 11.24f) < 0.001f, "Map conversion handles offset and size factor");
        check(float.IsNaN(SpawnMatching.MapCoordinate(0, 0, 0)), "Invalid scale does not create map coordinates");
        check(!SpawnMatching.ValidPosition(float.NaN, 0, 0) && !SpawnMatching.ValidPosition(0, float.PositiveInfinity, 0), "Rejects non-finite locations");
        check(SpawnMatching.LayoutPaths("ffxiv/a/level/a.lvb")[0] == "bg/ffxiv/a/level/planevent.lgb", "Layout path from Bg row");
        check(SpawnMatching.LayoutPaths("bg/ffxiv/a/level/a")[0] == "bg/ffxiv/a/level/planevent.lgb", "No duplicate bg prefix");
        check(SpawnMatching.LayoutPaths("invalid").Length == 0 && SpawnMatching.LayoutPaths("../a/level/a").Length == 0, "Malformed layout paths rejected");

        var near = Point(10, 12);
        TravelDestination[] destinations = [
            new(new(1, 20, 200, "Other territory", 0, 0), 0, 0),
            new(new(2, 10, 100, "Far", 100, 0), 0, 10),
            new(new(3, 10, 100, "Near", 10, 0), 0, 100),
            new(new(4, 10, 101, "Other floor", 0, 0), 0, 10),
            new(new(5, 10, 100, "Unknown position", null, null), 0, 10)
        ];
        var ordered = SpawnMatching.OrderDestinations(destinations, near).ToArray();
        check(ordered.Length == 4 && ordered[0].Aetheryte.Id == 3 && ordered[1].Aetheryte.Id == 2,
            "Teleport-near ignores other territories and prioritizes known same-map distance");
        check(SpawnMatching.OrderDestinations(destinations, null).Count() == 5, "Area-only destination list does not invent a nearest spawn");

        var raw = new Npc { ParentData = new Parent { ParentData = new Game { BaseId = 10 } }, NameId = 20, Level = 12 };
        check(LayoutNpcReader.TryRead(raw, out var parsed, out _) && parsed!.BaseId == 10 && parsed.NameId == 20 && parsed.Level == 12,
            "Managed boxed LGB NPC fields decode independently of a process-memory layout");
        raw.Level = 0;
        check(LayoutNpcReader.TryRead(raw, out parsed, out _) && parsed!.Level == null, "Layout level zero stays unknown");
        raw.Level = 999;
        check(LayoutNpcReader.TryRead(raw, out parsed, out _) && parsed!.Level == null, "Invalid layout level stays unknown");
        raw.PopEvent = 1;
        check(LayoutNpcReader.TryRead(raw, out parsed, out _) && parsed!.Conditions.Contains("PopEvent"), "Conditional layout spawn flag retained");
        check(!LayoutNpcReader.TryRead(new object(), out _, out var error) && error.Length > 0, "Unknown managed layout rejected with diagnostic");
        raw.NameId = 0;
        check(!LayoutNpcReader.TryRead(raw, out _, out _), "Missing BNpcName reference rejected");
    }

    // Synthetic managed equivalents only. Not real game structs or game data.
    public struct Game { public uint BaseId; }
    public struct Parent { public Game ParentData; }
    public struct Npc { public Parent ParentData; public uint NameId; public ushort Level; public byte PopEvent; }
}
