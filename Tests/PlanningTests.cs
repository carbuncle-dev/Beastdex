// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;
using Beastdex.Services;

internal static class PlanningTests
{
    public static void Run(Action<bool, string> check)
    {
        var index = new WorldIndex(
            new Dictionary<uint, TerritoryInfo>
            {
                [10] = new(10, "Hint Area", "", 1000, 0, 100, [100]),
                [20] = new(20, "Low Duty", "", 2000, 77, 200, [200]),
                [30] = new(30, "Other Area", "", 3000, 0, 300, [300]),
                [40] = new(40, "Multi-map", "", 4000, 88, 400, [400, 401]),
            },
            new Dictionary<uint, MapInfo>
            {
                [100] = new(100, 10, 1000, 0, "Hint Area", 100, 0, 0),
                [200] = new(200, 20, 2000, 0, "Low Duty", 100, 0, 0),
                [300] = new(300, 30, 3000, 0, "Other Area", 100, 0, 0),
                [400] = new(400, 40, 4000, 0, "Floor 1", 100, 0, 0),
                [401] = new(401, 40, 4000, 0, "Floor 2", 100, 0, 0),
            }, new Dictionary<uint, AetheryteInfo>(), new Dictionary<uint, uint[]> { [1] = [10], [2] = [10], [3] = [20] }, [], "synthetic")
        { Duties = new Dictionary<uint, DutyInfo> { [77] = new(77, 20, "Low Duty", "Low Duty") } };
        BeastInfo Beast(uint id = 1) => new(id, "Familiar " + id, 0, "Not displayed")
        { NameResolved = true, Location = new(1, 1000, "Hint Area", true) };
        SpawnPoint Point(int? level = 10, uint territory = 10, uint name = 500, bool coordinates = true) =>
            new(territory, territory * 10, 900, name, "Capture Enemy", level, 20, 0, 30,
                SpawnEvidence.CommunityReport, "Synthetic capture report", "Synthetic")
            {
                HasCoordinates = coordinates, HasWorldY = coordinates, Encounter = EncounterKind.Regular,
                FateId = 0, DutyId = territory == 20 ? 77u : territory == 40 ? 88u : 0,
                CaptureBeastId = 1,
            };
        SpawnGroup Group(SpawnPoint point, bool reported = true) => SpawnGrouping.Group(
            [new SpawnCandidate(point, reported ? NameMatchKind.CommunityReport : NameMatchKind.Exact)], index).Single();
        BeastPlan Plan(uint id, params SpawnGroup[] groups)
        {
            var b = Beast(id);
            var options = SourceReconciler.Build(b, groups, index);
            return new(b, options, SourceReconciler.Preferred(options, index));
        }
        XbmCaptureSnapshot Captures(params uint[] ids) => new() { Available = true, CapturedRowIds = ids };

        var merged = SourceReconciler.Build(Beast(), [Group(Point())], index);
        check(merged.Length == 1 && merged[0].MatchesBestiary && merged[0].IsReported && !merged[0].IsHintOnly,
            "Matching hint and capture source form one unified option with both origins");
        check(merged[0].Point.Level == 10 && merged[0].Point.X == 20,
            "Merging hint context preserves the source's mob level and real point");
        var alternative = SourceReconciler.Build(Beast(), [Group(Point(20, 20))], index);
        check(alternative.Length == 2 && alternative.Count(o => o.IsHintOnly) == 1,
            "An off-hint duty alternative does not erase the original area hint");
        check(!alternative.Single(o => o.IsHintOnly).Point.HasCoordinates && alternative.Single(o => o.IsHintOnly).Point.Level == null,
            "Fallback hint never invents a coordinate or a mob level");
        var dutyBeast = Beast(3) with { Location = new(2, 77, "Low Duty", true) };
        check(SourceReconciler.MatchesHint(dutyBeast, Point(20, 20), index), "Duty hints match by exact duty ID");
        check(!SourceReconciler.MatchesHint(dutyBeast, Point(20, 20) with { DutyId = 78 }, index),
            "Another duty difficulty does not merge just because it uses the same territory");
        check(!SourceReconciler.MatchesHint(Beast(), Point(10, 30) with { AreaLabel = "Hint Area" }, index),
            "Matching display text cannot merge different territory IDs");
        var lowCandidate = Group(Point(1), reported: false);
        var combo = Plan(1, Group(Point(45)), Group(Point(20, 20)), lowCandidate);
        check(combo.Preferred?.Point.Level == 20, "A lower-level cross-area capture report is preferred over the hint and a level-one lookalike");
        var knownOther = Plan(2, Group(Point(6, 30)));
        var unknown = Plan(4, Group(Point(null)));
        var noReport = Plan(5, lowCandidate);
        var queue = CatchPlanner.Build([combo, knownOther, unknown, noReport], Captures(), 26, index);
        check(queue.Select(q => q.BeastId).SequenceEqual(new uint[] { 2, 1 }),
            "Next-catch queue uses the lowest reported level across all source contexts");
        check(queue.Length == 2 && queue[1].Source.Point.Level == 20 && queue[1].Source.Point.DutyId == 77,
            "Queue has one choice per familiar and retains the chosen alternative duty");
        check(CatchPlanner.Build([combo, knownOther], Captures(2), 26, index).Single().BeastId == 1,
            "Captured familiars leave the queue automatically");
        check(CatchPlanner.Build([combo], Captures(), 19, index).Length == 0,
            "Known future-level sources do not count as catchable yet");
        check(CatchPlanner.Build([combo], new XbmCaptureSnapshot(), 26, index).Length == 0,
            "Unavailable capture state never treats every familiar as missing");
        check(CatchPlanner.Build([combo], Captures(), null, index).Length == 0,
            "Unknown BST level cannot produce a level-ready target");
        check(CatchPlanner.Build([noReport], Captures(), 99, index).Length == 0,
            "Candidates never enter the level-ready queue without an explicit report");
        var sorted = CatchPlanner.Sort([knownOther, combo, unknown], BestiarySort.NextCatch, Captures(2), 26, index);
        check(sorted.Select(p => p.Beast.RowId).SequenceEqual(new uint[] { 1, 4, 2 }),
            "Next-catch sorting places missing level-ready entries before unknowns and obtained entries");
        check(CatchPlanner.Sort([combo, knownOther], BestiarySort.LowestLevel, Captures(2), 26, index)[0].Beast.RowId == 2,
            "All-source level sort can include obtained entries independently of next-catch sorting");

        // Area-only reports and local positions must not be split by a missing sole-map ID.
        var report = Point(10, coordinates: false) with { MapId = 0 };
        var observed = Point() with { Evidence = SpawnEvidence.Observed, CaptureBeastId = 0, Name = "Different target name" };
        var joinedIndex = index with { LayoutPoints = [observed], CommunityPoints = [report] };
        var discovered = SpawnDiscovery.Build(joinedIndex, [Beast()], [], 160).Groups[1];
        check(discovered.Length == 1 && discovered[0].IsReported && discovered[0].Point.HasCoordinates,
            "Area-only capture report joins a known matching NPC position on the territory's sole map");
        check(!SpawnDiscovery.CanJoinReport(report with { MapId = 400, TerritoryId = 40, DutyId = 88 },
            observed with { MapId = 401, TerritoryId = 40, DutyId = 88 }, index),
            "Explicit different floor maps cannot transfer capture attribution");

        TravelDestination Dest(uint id, float? x, float? z, uint map = 100, uint territory = 10, uint gil = 100) =>
            new(new(id, territory, map, "Aetheryte " + id, x, z), 0, gil);
        var near = Dest(1, 22, 32, gil: 999);
        var far = Dest(2, 500, 500, gil: 1);
        var alien = Dest(3, 20, 30, 300, 30);
        var route = TravelPlanning.Choose([far, alien, near], Point(), index);
        check(route.Destination?.Aetheryte.Id == 1 && !route.NeedsChoice && route.Distance is > 0 and < 4,
            "Known mob position selects the closest unlocked same-map aetheryte, not the cheapest or another territory");
        check(TravelPlanning.Choose([far, near], Point(coordinates: false), index).NeedsChoice,
            "Area-only source uses a destination list instead of inventing proximity");
        check(TravelPlanning.Choose([near], Point(coordinates: false), index).Destination == near,
            "Only one unlocked destination needs no redundant selector even without a mob position");
        check(TravelPlanning.Choose([near with { Aetheryte = near.Aetheryte with { X = null } }, far with { Aetheryte = far.Aetheryte with { Z = null } }],
            Point(), index).NeedsChoice, "Missing aetheryte coordinates fail over to choice without a false closest claim");
        check(TravelPlanning.Choose([Dest(7, 0, 0, 200, 20)], Point(20, 20), index).Destination == null,
            "Duty source never becomes an overworld teleport");
        check(TravelPlanning.MapId(Point() with { MapId = 0 }, index) == 100, "A sole valid map is inferred safely");
        check(TravelPlanning.MapId(Point(20, 40) with { MapId = 0 }, index) == 0,
            "Ambiguous multi-floor territory stays unassigned");
        check(TravelPlanning.MapId(Point() with { MapId = 300 }, index) == 0,
            "A wrong-territory map ID is rejected");
        check(!TravelPlanning.CanFlag(Point(coordinates: false), index), "Area-only hints cannot produce false map flags");
        check(TravelPlanning.CanFlag(Point(), index), "Valid positioned sources can place a map flag");
        check(!TravelPlanning.CanFlag(Point() with { X = float.NaN }, index), "NaN position cannot place a flag");
        check(Math.Abs(SpawnMatching.MapCoordinate(0, 100, 0) - 21.48f) < .001f,
            "Coordinate display matches Dalamud MapUtil at the world origin");

        var selection = new CompactSelection();
        check(selection.Sync(queue) == 0 && selection.SelectedId == 2, "Compact initially follows the lowest missing familiar");
        selection.Move(1, queue);
        check(selection.SelectedId == 1 && !selection.FollowFirst, "Compact arrow chooses the next ranked familiar");
        var addedPlan = Plan(9, Group(Point(2)));
        var inserted = new[] { new NextCatch(addedPlan, addedPlan.Preferred!), queue[0], queue[1] };
        check(selection.Sync(inserted) == 2 && selection.SelectedId == 1,
            "Refresh/reordering preserves a manually browsed familiar by identity");
        check(selection.Sync([queue[0]]) == 0 && selection.SelectedId == 2 && selection.FollowFirst,
            "Capturing/removing the selected familiar returns to the new lowest");
        selection.Move(-9, queue);
        check(selection.SelectedId == 2, "Previous navigation clamps at the beginning");
        selection.Move(99, queue);
        check(selection.SelectedId == 1, "Next navigation clamps at the last entry");
        selection.Reset(); check(selection.Sync(queue) == 0, "Lowest button resumes follow-first mode");
        check(selection.Sync([]) == -1 && selection.SelectedId == null, "Empty queue clears stale compact selection");
        check(!RefreshPolicy.IsDue(1999, true, 2, false), "Polling waits for the configured interval");
        check(RefreshPolicy.IsDue(2000, true, 2, false), "Polling refreshes at the configured interval");
        check(!RefreshPolicy.IsDue(999999, false, 2, false), "Auto-refresh can be paused");
        check(RefreshPolicy.IsDue(0, false, 2, true), "Manual/login refresh works even while polling is paused");
        check(!RefreshPolicy.IsDue(0, true, 0, false), "Corrupt zero interval cannot cause a per-frame poll");
    }
}
