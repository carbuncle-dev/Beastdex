// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using Beastdex.Models;
using Beastdex.Services;

internal static class SourceTests
{
    public static void Run(Action<bool, string> check)
    {
        // Synthetic sheet IDs and shapes. No provider catalog is redistributed by these fixtures.
        var index = Index();
        SpawnPoint Point(float x, float z = 0, int? level = 4, uint territory = 30) => new(territory,
            territory == 30 ? 300u : territory == 20 ? 200u : 100u,
            341, 401, "Puk Hatchling", level, x, 0, z, SpawnEvidence.Observed, "Synthetic test", "Synthetic")
        { FateId = 0, Encounter = EncounterKind.Regular, EnglishName = "Puk Hatchling", DutyId = territory == 20 ? 77u : 0u };
        SpawnCandidate Candidate(SpawnPoint p) => new(p, NameMatchKind.NameVariant);
        var cloud = Enumerable.Range(0, 36).Select(i => Candidate(Point((i % 6) * 20, (i / 6) * 12))).ToArray();
        var grouped = SpawnGrouping.Group(cloud, index);
        check(grouped.Length == 1 && grouped[0].SampleCount == 36, "36 nearby same-level Puk sightings become one group");
        check(cloud.Any(c => c.Point == grouped[0].Point), "Representative is a real point, not an invented centroid");
        check(SpawnGrouping.Group(cloud.Reverse(), index)[0].Point == grouped[0].Point,
            "Group representative is independent of observation order");
        check(SpawnGrouping.Group(cloud.Append(Candidate(Point(900))), index).Length == 2,
            "Separated regions remain separate spawn groups");
        check(SpawnGrouping.Group([Candidate(Point(0)), Candidate(Point(1, level: 5))], index).Length == 2,
            "Different mob levels are never collapsed");
        check(SpawnGrouping.Group([Candidate(Point(0)), Candidate(Point(0, territory: 10))], index).Length == 2,
            "Different territories are never collapsed");
        check(SpawnGrouping.Group([Candidate(Point(0)), Candidate(Point(1) with { MapId = 301 })], index).Length == 2,
            "Different map pages are kept separate");
        check(SpawnGrouping.Group([Candidate(Point(0)), Candidate(Point(1) with { NameId = 999 })], index).Length == 2,
            "Different NPC names are kept separate");
        check(SpawnGrouping.Group([Candidate(Point(0)), Candidate(Point(1) with { BaseId = 888 })], index).Length == 1,
            "Same name/level context can retain multiple base IDs in one group");
        check(SpawnGrouping.Group([Candidate(Point(0)), Candidate(Point(1) with { Encounter = EncounterKind.Fate, FateId = 12 })], index).Length == 2,
            "Ordinary and FATE sources are separate");
        check(SpawnGrouping.Group([Candidate(Point(0) with { Encounter = EncounterKind.Fate, FateId = 12 }),
            Candidate(Point(1) with { Encounter = EncounterKind.Fate, FateId = 13 })], index).Length == 2,
            "Different FATE identities remain separate");
        check(SpawnGrouping.Group([Candidate(Point(0, territory: 20)), Candidate(Point(0, territory: 20) with { Y = 35 })], index).Length == 2,
            "Vertically separated duty floors are not grouped");
        check(SpawnGrouping.Group([Candidate(Point(0)), Candidate(Point(5) with { Y = 35 })], index).Length == 1,
            "Overworld slopes do not masquerade as separate duty floors");
        var areaOnly = Point(0) with { HasCoordinates = false, HasWorldY = false, CaptureBeastId = 14,
            Evidence = SpawnEvidence.CommunityReport };
        var onlyGroup = SpawnGrouping.Group([new(areaOnly, NameMatchKind.CommunityReport)], index);
        check(onlyGroup.Length == 1 && !onlyGroup[0].Point.HasCoordinates && onlyGroup[0].SampleCount == 0,
            "Area-only reports never manufacture a map pin");

        SpawnGroup Report(SpawnPoint p) => SpawnGrouping.Group([new(p, NameMatchKind.CommunityReport)], index).Single();
        var lowDuty = Report(Point(0, level: 20, territory: 20));
        var highWorld = Report(Point(0, level: 45, territory: 10));
        check(SpawnGrouping.Preferred([highWorld, lowDuty], index) == lowDuty,
            "Lower-level duty wins over higher-level overworld (level is the first preference)");
        var lowWorld = Report(Point(0, level: 20, territory: 10));
        check(SpawnGrouping.Preferred([lowDuty, lowWorld], index) == lowWorld,
            "Overworld wins when level is tied");
        var fateWorld = Report(Point(0, level: 20, territory: 10) with { Encounter = EncounterKind.Fate, FateId = 99 });
        check(SpawnGrouping.Preferred([fateWorld, lowWorld], index) == lowWorld,
            "Ordinary source wins over FATE when level and world/duty status tie");
        var lowFate = Report(Point(0, level: 3, territory: 10) with { Encounter = EncounterKind.Fate, FateId = 99 });
        check(SpawnGrouping.Preferred([lowFate, lowWorld], index) == lowFate,
            "A lower-level FATE wins according to the requested level-first priority");
        check(SpawnGrouping.Preferred([Report(Point(0, level: null)), lowDuty], index) == lowDuty,
            "Unknown level is never treated as lowest");
        var lookalike = SpawnGrouping.Group([Candidate(Point(0, level: 1))], index).Single();
        check(SpawnGrouping.Preferred([lookalike, lowDuty], index) == lowDuty,
            "An unverified lower-level lookalike cannot replace a reported capture source");

        foreach (var size in new[] { 100, 200, 400 })
        foreach (var offset in new[] { -100, 0, 160 })
        foreach (var world in new[] { -512f, 0f, 650f })
            check(Math.Abs(world - SpawnMatching.WorldCoordinate(SpawnMatching.MapCoordinate(world, size, offset), size, offset)) < 0.01f,
                $"Map/world coordinate round trip size={size} offset={offset} x={world}");

        // Two minimal capture alternatives supplied in the user's example. IDs below are synthetic bindings.
        const string html = """
            <table><tr><td><a href="/beasts/42">No. 42</a></td><td><img src="ignored"/></td>
            <td><a class="name" href="/beasts/42">Cobra</a></td>
            <td class="hide-xs"><div class="sources"><span class="source source-npc">Lv 45 Lake Cobra - Mor Dhona (25, 12)</span><span class="source source-instance">Lv 20 Coliseum Python - Halatali</span><span class="source source-vendor">Vendor - Some Place - 10 Tokens</span></div></td></tr></table>
            """;
        var rows = CommunityParsers.ReadCollectHtml(html);
        check(rows.Length == 1 && rows[0].BeastId == 42 && rows[0].SourceLines.Length == 3,
            "Actual span.source boundaries preserve each NPC/duty/vendor record independently");
        check(rows[0].SourceLines[1] == "Lv 20 Coliseum Python - Halatali", "Vendor text cannot leak into the duty name");
        var bound = CommunityReportBinder.Bind(rows, index, new HashSet<uint> { 42, 14 });
        check(bound.Points.Length == 2, "Both reported Cobra alternatives are imported without a hard-coded alias");
        var python = bound.Points.Single(p => p.NameId == 502);
        var lake = bound.Points.Single(p => p.NameId == 501);
        check(python.TerritoryId == 20 && python.DutyId == 77 && python.Level == 20 && !python.HasCoordinates,
            "Differently named duty enemy binds its own territory/duty/level, without fake coordinates");
        check(lake.TerritoryId == 10 && lake.DutyId == 0 && lake.HasCoordinates && lake.Level == 45,
            "World report keeps its actual level and area");
        check(Math.Abs(SpawnMatching.MapCoordinate(lake.X, 100, 0) - 25) < 0.001f, "Reported X becomes the correct world coordinate");
        check(bound.Unresolved.Length == 1, "Non-mob/vendor acquisition is explicitly unresolved, not assigned level zero");
        var mismatch = CommunityReportBinder.Bind([rows[0] with { EnglishName = "Unrelated" }], index, new HashSet<uint> { 42 });
        check(mismatch.Points.Length == 0 && mismatch.Unresolved.Length == 1, "Provider ID/name disagreement is rejected");
        var duplicatePlace = index with { Territories = index.Territories.Values.Append(new TerritoryInfo(99, "Mor Dhona (instance)", "", 111, 0, 100, [100]))
            .ToDictionary(t => t.Id) };
        var ambiguous = CommunityReportBinder.Bind(rows, duplicatePlace, new HashSet<uint> { 42 });
        check(ambiguous.Points.Single(p => p.Level == 45).TerritoryId == 0,
            "Ambiguous location is visible but no arbitrary territory is chosen");
        var keys = index.EnglishPlaceNames.Values.Concat(index.Duties.Values.Select(d => d.EnglishName))
            .Select(CommunityParsers.LocationKey).ToHashSet();
        check(CommunityParsers.TryReadCaptureSource(14, "Lv 3 Puk - FATE: \"Synthetic Event\" - Middle La Noscea (21,20)", keys, out var fateReport, out _) &&
            fateReport!.Encounter == EncounterKind.Fate, "FATE source conditions preserved");
        check(!CommunityParsers.TryReadCaptureSource(14, "Lv 3 Puk - Unknown Zone", keys, out _, out _),
            "Unrecognised area is not fuzzy-mapped to an unrelated zone");
        check(CommunityParsers.TryReadCaptureSource(14, "Lv 50 - Puk - B Rank Hunt - Middle La Noscea", keys, out var huntReport, out _) &&
            huntReport!.Encounter == EncounterKind.Hunt, "Optional dash after level and hunt condition are supported");
        var raidKeys = keys.Append(CommunityParsers.LocationKey("The Second Coil of Bahamut - Turn 1")).ToHashSet();
        check(CommunityParsers.TryReadCaptureSource(49, "Lv 50 Enemy - The Second Coil of Bahamut - Turn 1", raidKeys, out var raid, out _) &&
            raid!.Area == "The Second Coil of Bahamut - Turn 1", "Hyphenated duty name is not split into a bogus enemy alias");
        check(Throws(() => CommunityParsers.ReadCollectHtml("<html>No table</html>")), "Changed/missing source table is rejected");
        check(Throws(() => CommunityParsers.ReadCollectHtml(html + html)), "Duplicate IDs reject a malformed page");

        const string tc = """
            {"401":{"baseid":99999,"positions":[
              {"map":300,"zoneid":999999,"level":4,"fate":0,"x":100,"y":8,"z":200},
              {"map":300,"zoneid":999999,"level":4,"fate":55,"x":110,"y":8,"z":200},
              {"map":300,"zoneid":999999,"level":0,"x":115,"y":8,"z":200},
              {"map":999999,"level":1,"fate":0,"x":0,"y":0,"z":0}]}}
            """;
        var observations = CommunityParsers.ReadTeamcraft(tc, index);
        check(observations.Length == 3 && observations.All(p => p.TerritoryId == 30),
            "Teamcraft territory resolves through Map; zoneid is not mistaken for TerritoryType");
        check(observations[0].X == 100 && observations[0].Y == 8 && observations[0].Z == 200,
            "Teamcraft coordinates are world XYZ, not UI map XY");
        check(observations.All(p => p.NameId == 401 && p.BaseId == 0 && !p.BaseIdReliable),
            "BNpcName key used; per-name aggregate baseid is never claimed for each position");
        check(observations[0].Encounter == EncounterKind.Regular && observations[1].FateId == 55 && observations[2].Encounter == EncounterKind.Unknown,
            "Regular, FATE and unknown conditions are distinct");
        check(observations[2].Level == null, "Missing/zero community level remains unknown");
        check(Throws(() => CommunityParsers.ReadTeamcraft("[]", index)) && Throws(() => CommunityParsers.ReadTeamcraft("{}", index)),
            "Invalid Teamcraft document/schema is rejected");

        var cobra = new BeastInfo(42, "Cobra", 1, "") { NameResolved = true, Location = new(1, 111, "Mor Dhona", true) };
        var sourceIndex = index with { CommunityPoints = bound.Points };
        var found = SpawnDiscovery.Build(sourceIndex, [cobra], [], 160);
        var preferred = SpawnGrouping.Preferred(found.Groups[42], sourceIndex);
        check(found.Groups[42].Length == 2 && preferred?.Point.NameId == 502,
            "End-to-end source discovery recommends the lower-level alternative outside the bestiary hint");
        var currentPython = python with { CaptureBeastId = 0, Evidence = SpawnEvidence.Observed, HasCoordinates = true,
            HasWorldY = true, MapId = 200, X = 40, Y = 2, Z = 50, BaseIdReliable = true, BaseId = 1234 };
        check(SpawnDiscovery.CanJoinReport(python, currentPython, index), "Exact NPC/territory/duty/level/context can attach a report to a location");
        check(!SpawnDiscovery.CanJoinReport(python, currentPython with { Level = 21 }, index),
            "A different level is not silently promoted to a verified report");
        check(!SpawnDiscovery.CanJoinReport(python, currentPython with { TerritoryId = 30, DutyId = 0 }, index),
            "A report does not prove capture suitability in another area");
        check(!SpawnDiscovery.CanJoinReport(python, currentPython with { Encounter = EncounterKind.Fate, FateId = 9 }, index),
            "Same-name FATE enemy cannot inherit an ordinary enemy's capture report");
        var joined = SpawnDiscovery.Build(sourceIndex, [cobra], [currentPython], 160);
        check(joined.Groups[42].Length == 2 && SpawnGrouping.Preferred(joined.Groups[42], sourceIndex)!.Point.HasCoordinates,
            "A contextual observation replaces an area-only duplicate while retaining reported capture attribution");
        var cacheCopy = JsonSerializer.Deserialize<SpawnPoint>(JsonSerializer.Serialize(currentPython));
        check(cacheCopy != null && cacheCopy.PossibleNameIds.SequenceEqual(currentPython.PossibleNameIds) &&
            (cacheCopy with { PossibleNameIds = currentPython.PossibleNameIds }) == currentPython,
            "Extended observation data survives local JSON persistence, including name-ID array values");

        var temporary = Path.Combine(Path.GetTempPath(), "beast-source-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var path = Path.Combine(temporary, "cached.txt");
            File.WriteAllText(path, "offline fixture");
            var cached = PublicDataCache.ReadAsync(PublicDataCache.CollectUrl, path, 1000, text => text, false, CancellationToken.None)
                .GetAwaiter().GetResult();
            check(cached.Value == "offline fixture" && cached.Status.StartsWith("Cached"),
                "Fresh local public-data cache is used without a network request");
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }
    private static WorldIndex Index()
    {
        TerritoryInfo[] territories = [new(10, "Mor Dhona", "", 111, 0, 100, [100]),
            new(20, "Halatali", "", 222, 77, 200, [200]), new(30, "Middle La Noscea", "", 333, 0, 300, [300])];
        MapInfo[] maps = [new(100, 10, 111, 0, "Mor Dhona", 100, 0, 0),
            new(200, 20, 222, 0, "Halatali", 100, 0, 0), new(300, 30, 333, 0, "Middle La Noscea", 100, 0, 0)];
        return new WorldIndex(territories.ToDictionary(t => t.Id), maps.ToDictionary(m => m.Id),
            new Dictionary<uint, AetheryteInfo>(), new Dictionary<uint, uint[]> { [42] = [10], [14] = [30] }, [], "Synthetic")
        {
            Duties = new Dictionary<uint, DutyInfo> { [77] = new(77, 20, "Halatali", "Halatali") },
            EnglishPlaceNames = new Dictionary<uint, string> { [111] = "Mor Dhona", [222] = "Halatali", [333] = "Middle La Noscea" },
            NpcNames = new Dictionary<uint, string> { [501] = "Lake Cobra", [502] = "Coliseum Python", [401] = "Puk Hatchling" },
            EnglishNpcNames = new Dictionary<uint, string> { [501] = "Lake Cobra", [502] = "Coliseum Python", [401] = "Puk Hatchling" },
            EnglishBeastNames = new Dictionary<uint, string> { [42] = "Cobra", [14] = "Puk" },
        };
    }
}
