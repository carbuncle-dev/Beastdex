// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using Beastdex.Models;
using Beastdex.Services;

internal static class IdentityAndMarkerTests
{
    public static void Run(Action<bool, string> check)
    {
        // IDs here are synthetic fixtures, not a bundled capture catalog.
        // Reproduce the full importer -> duplicate identities -> grouping -> ranking path.
        var index = new WorldIndex(new Dictionary<uint, TerritoryInfo>
        {
            [10] = new(10, "Upper La Noscea", "", 1, 0, 100, [100]),
            [20] = new(20, "Sastasha", "", 2, 77, 200, [200]),
            [30] = new(30, "Other duty", "", 3, 88, 300, [300]),
        }, new Dictionary<uint, MapInfo>
        {
            [100] = new(100, 10, 1, 0, "Upper La Noscea", 100, 0, 0),
            [200] = new(200, 20, 2, 0, "Sastasha", 100, 0, 0),
            [300] = new(300, 30, 3, 0, "Other duty", 100, 0, 0),
        }, new Dictionary<uint, AetheryteInfo>(), new Dictionary<uint, uint[]> { [33] = [10] }, [], "fixture")
        {
            NpcNames = new Dictionary<uint, string> { [500] = "master coeurl", [9911] = "master coeurl", [501] = "Chopper" },
            EnglishNpcNames = new Dictionary<uint, string> { [500] = "master coeurl", [9911] = "master coeurl", [501] = "Chopper" },
            EnglishBeastNames = new Dictionary<uint, string> { [33] = "coeurl" },
            EnglishPlaceNames = new Dictionary<uint, string> { [1] = "Upper La Noscea" },
            Duties = new Dictionary<uint, DutyInfo>
            {
                [77] = new(77, 20, "Sastasha", "Sastasha") { ContentTypeId = 2 },
                [88] = new(88, 30, "Other duty", "Other duty") { ContentTypeId = 4 },
            },
            ContentTypeIcons = new Dictionary<uint, uint> { [2] = 61801, [4] = 61804, [5] = 61802, [8] = 61809, [33] = 61819 },
            OpenWorldIconId = 100001,
            HuntCatalogAvailable = false, // Regression: this must not veto a regular capture report.
        };
        var beast = new BeastInfo(33, "coeurl", 0, "") { NameResolved = true };
        var row = new CollectRow(33, "Coeurl", ["Lv 24 Master Coeurl - Upper La Noscea (9,21)", "Lv 15 Chopper - Sastasha"]);
        var import = CommunityReportBinder.Bind([row], index, new HashSet<uint> { 33 });
        check(import.Points.Length == 2, "Both Coeurl sources survive import");
        var master = import.Points.Single(p => p.TerritoryId == 10);
        var chopper = import.Points.Single(p => p.TerritoryId == 20);
        check(master.NameId == 0 && master.PossibleNameIds.Order().SequenceEqual(new uint[] { 500, 9911 }),
            "Duplicate NPC names preserve all exact IDs, without arbitrarily selecting an ID");
        var restored = JsonSerializer.Deserialize<SpawnPoint>(JsonSerializer.Serialize(master));
        check(restored != null && restored.PossibleNameIds.SequenceEqual(master.PossibleNameIds) &&
            (restored with { PossibleNameIds = master.PossibleNameIds }) == master,
            "Duplicate name identities survive a JSON round trip by value");
        check(chopper.NameId == 501, "Unique NPC identity remains resolved");
        check(master.Encounter == EncounterKind.Regular && master.FateId == 0, "Imported common encounter is preserved");
        check(ReportIdentity.MatchesName(master, 500) && ReportIdentity.MatchesName(master, 9911) &&
            !ReportIdentity.MatchesName(master, 28), "No generic Coeurl Pup alias is invented");
        index = index with { CommunityPoints = import.Points };
        CaptureOption[] Options(WorldIndex wi, SpawnPoint[]? observed = null)
        {
            var discovery = SpawnDiscovery.Build(wi, [beast], observed ?? [], 160);
            return SourceReconciler.Build(beast, discovery.Groups[33], wi);
        }
        var options = Options(index);
        foreach (var level in new[] { 16, 23, 24, 26, 30, 50 })
        {
            var chosen = SourceReconciler.Preferred(options, index, level)!;
            check(chosen.Point.TerritoryId == (level < 24 ? 20u : 10u),
                $"Imported duplicate-name Coeurl recommendation correct at BST{level}");
        }
        var preferred = SourceReconciler.Preferred(options, index, 30)!;
        check(EncounterPolicy.Tier(preferred, index) == CaptureSourceTier.CommonOverworld, "An unrelated hunt lookup failure does not disable ordinary reports");
        var huntConflict = index with { HuntNameIds = new HashSet<uint> { 9911 } };
        check(EncounterPolicy.Tier(preferred, huntConflict) == CaptureSourceTier.Uncertain, "Conflicting possible hunt identity remains unverified");
        check(SourceReconciler.Preferred(options, huntConflict, 30)!.Point.TerritoryId == 20, "Unresolved hunt conflict cannot beat duty");

        var sighting = master with { NameId = 500, PossibleNameIds = [], Evidence = SpawnEvidence.Observed,
            CaptureBeastId = 0, BaseId = 50, BaseIdReliable = true };
        check(SpawnDiscovery.CanJoinReport(master, sighting, index), "A duplicate report ID binds inside its correct scope");
        check(!SpawnDiscovery.CanJoinReport(master, sighting with { TerritoryId = 30, DutyId = 88 }, index), "No cross-duty association");
        check(!SpawnDiscovery.CanJoinReport(master, sighting with { Level = 23 }, index), "No silent expansion of reported levels");
        check(!SpawnDiscovery.CanJoinReport(master, sighting with { Encounter = EncounterKind.Fate, FateId = 1 }, index), "No ordinary-to-FATE association");
        var joined = Options(index, [sighting]);
        check(joined.Count(o => o.Point.TerritoryId == 10) == 1 && joined.Single(o => o.Point.TerritoryId == 10).Point.NameId == 500,
            "Scoped identity resolution lets rough report and precise observation group together");
        var plan = new BeastPlan(beast, options, preferred);
        var capture = new XbmCaptureSnapshot { Available = true, CapturedRowIds = [] };
        check(CatchPlanner.Build([plan], capture, 26, index).Single().Source.Point.TerritoryId == 10,
            "Compact recommendation uses the corrected imported source too");

        check(EncounterPresentation.Describe(preferred, index).Kind == EncounterIconKind.OpenWorld, "Open-world type shown with reported regular source");
        var dungeon = options.Single(o => o.Point.TerritoryId == 20);
        check(EncounterPresentation.Describe(dungeon, index) is { Kind: EncounterIconKind.Dungeon, IconId: 61801 }, "Dungeon uses the dungeon game icon");
        var trial = dungeon with { Point = chopper with { TerritoryId = 30, DutyId = 88 } };
        check(EncounterPresentation.Describe(trial, index) is { Kind: EncounterIconKind.Trial, IconId: 61804 }, "Trial is not labelled Dungeon");
        check(EncounterPresentation.Describe(dungeon with { Point = chopper with { Encounter = EncounterKind.Fate, FateId = 12 } }, index)
            is { Kind: EncounterIconKind.Fate, IconId: 61809 }, "Explicit FATE type overrides generic duty type");
        check(EncounterPresentation.Describe(preferred with { Point = master with { Encounter = EncounterKind.Hunt } }, index)
            is { Kind: EncounterIconKind.Hunt, IconId: 61819 }, "Hunt uses hunt game icon");
        check(EncounterPresentation.Describe(preferred with { Point = master with { Encounter = EncounterKind.Conditional } }, index)
            .Kind == EncounterIconKind.Conditional, "Unknown/conditional mobs not labelled common");
        var raidIndex = index with { Duties = new Dictionary<uint, DutyInfo> { [77] = index.Duties[77] with { ContentTypeId = 5 } } };
        check(EncounterPresentation.Describe(dungeon, raidIndex) is { Kind: EncounterIconKind.Raid, IconId: 61802 }, "Raid uses its own type and icon");

        // Missing textures must preserve encounter labels and the UI fallback, not throw.
        var noIcons = index with { ContentTypeIcons = new Dictionary<uint, uint>() };
        check(EncounterPresentation.Describe(dungeon with { Point = chopper with { Encounter = EncounterKind.Fate, FateId = 12 } }, noIcons)
            is { Kind: EncounterIconKind.Fate, Label: "FATE", IconId: 0 }, "Missing FATE icon keeps the FATE label and zero-icon fallback");
        check(EncounterPresentation.Describe(preferred with { Point = master with { Encounter = EncounterKind.Hunt } }, noIcons)
            is { Kind: EncounterIconKind.Hunt, Label: "Hunt", IconId: 0 }, "Missing hunt icon keeps the Hunt label and zero-icon fallback");

        var rules = BeastTargetMatching.BuildRules([plan], index);
        var rule = rules.Single(r => r.Source.TerritoryId == 10);
        var live = new LiveCaptureNpc(10, 0, 500, 50, 24, 0);
        bool Mark(LiveCaptureNpc mob, XbmCaptureSnapshot? snap = null, int? level = 30, bool missing = true, bool within = true) =>
            BeastTargetMatching.Matches(rule, mob, index, snap ?? capture, level, missing, within);
        check(Mark(live), "Scoped reported common NPC gets a BST marker");
        check(!Mark(live with { NameId = 28 }), "Same-family Coeurl Pup is not falsely claimed as the reported Master Coeurl");
        check(!Mark(live with { TerritoryId = 30, DutyId = 88 }), "Same name in another duty is not marked");
        check(!Mark(live with { Level = 23 }), "Marker requires observed level in the reported source");
        check(!Mark(live with { FateId = 14 }), "FATE copy of common NPC is not marked");
        check(!Mark(live with { FateId = null }), "Unresolved live encounter does not get an ordinary marker");
        check(!Mark(live, level: 20) && Mark(live, level: 24), "Marker respects synced player level and equality");
        check(Mark(live, level: 20, within: false), "User may show above-level targets");
        check(!Mark(live, new XbmCaptureSnapshot()), "Unknown captures do not count as missing");
        check(!Mark(live, capture with { CapturedRowIds = [33] }), "Obtained target disappears with missing-only filter");
        check(Mark(live, capture with { CapturedRowIds = [33] }, missing: false), "Obtained target allowed when user disables missing filter");
        var candidate = preferred with { Group = new SpawnGroup(new SpawnCandidate(master, NameMatchKind.NameVariant),
            [new SpawnCandidate(master, NameMatchKind.NameVariant)], 1, 0, "candidate") };
        check(BeastTargetMatching.BuildRules([plan with { Options = [candidate] }], index).Length == 0,
            "Unverified name/model lookalikes never create target markers");
        var exactBase = rule with { Source = rule.Source with { BaseId = 50, BaseIdReliable = true } };
        check(!BeastTargetMatching.Matches(exactBase, live with { BaseId = 51 }, index, capture, 30, true, true),
            "Reliable per-instance base IDs are respected");
        var fateRule = rule with { Source = rule.Source with { Encounter = EncounterKind.Fate, FateId = 99 } };
        check(!BeastTargetMatching.Matches(fateRule with { Source = fateRule.Source with { FateId = null } },
            live with { FateId = 99 }, index, capture, 30, true, true),
            "Unknown FATE event identity must not authorize every event copy");
        check(BeastTargetMatching.Matches(fateRule, live with { FateId = 99 }, index, capture, 30, true, true) &&
            !BeastTargetMatching.Matches(fateRule, live with { FateId = 100 }, index, capture, 30, true, true), "Mapped FATE identity remains scoped");
    }
}
