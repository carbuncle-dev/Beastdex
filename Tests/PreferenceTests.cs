// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;
using Beastdex.Services;

internal static class PreferenceTests
{
    public static void Run(Action<bool, string> check)
    {
        var index = new WorldIndex(
            new Dictionary<uint, TerritoryInfo>
            {
                [10] = new(10, "Upper La Noscea", "", 1, 0, 100, [100]),
                [20] = new(20, "Sastasha", "", 2, 77, 200, [200]),
            }, new Dictionary<uint, MapInfo>
            {
                [100] = new(100, 10, 1, 0, "Upper La Noscea", 100, 0, 0),
                [200] = new(200, 20, 2, 0, "Sastasha", 100, 0, 0),
            }, new Dictionary<uint, AetheryteInfo>(), new Dictionary<uint, uint[]>(), [], "test")
        {
            HuntCatalogAvailable = true,
            HuntNameIds = new HashSet<uint> { 999 }, HuntBaseIds = new HashSet<uint> { 888 },
        };
        CaptureOption Option(int? level, uint territory = 10, EncounterKind encounter = EncounterKind.Regular,
            uint? fateId = 0, bool reported = true, uint nameId = 500, uint baseId = 50, bool reliable = true,
            string name = "Synthetic Enemy")
        {
            var point = new SpawnPoint(territory, territory * 10, baseId, nameId, name, level,
                0, 0, 0, SpawnEvidence.CommunityReport, "Test fixture", "Test fixture")
            {
                Encounter = encounter, FateId = fateId, BaseIdReliable = reliable,
                DutyId = territory == 20 ? 77u : 0,
            };
            var candidate = new SpawnCandidate(point, reported ? NameMatchKind.CommunityReport : NameMatchKind.Exact);
            var key = $"{level}:{territory}:{encounter}:{fateId}:{reported}:{nameId}:{name}";
            return new(33, point, new SpawnGroup(candidate, [candidate], 1, 0, key), false, false, key);
        }
        CaptureOption? Pick(int? bst, params CaptureOption[] options) => SourceReconciler.Preferred(options, index, bst);

        // Named examples are TEST fixtures only; no familiar/NPC exception exists in the runtime policy.
        // Reported source levels: https://ffxivcollect.com/beasts/33 (2026-09-11).
        var master = Option(24, name: "Master Coeurl");
        var chopper = Option(15, 20, name: "Chopper");
        check(Pick(16, master, chopper) == chopper, "Coeurl at BST16 prefers Chopper in Sastasha");
        check(Pick(30, master, chopper) == master, "Coeurl at BST30 prefers Master Coeurl in Upper La Noscea");
        check(Pick(23, master, chopper) == chopper, "Coeurl below the common-mob threshold uses Sastasha");
        check(Pick(24, master, chopper) == master, "EQUAL level counts: switch to Master Coeurl at BST24");
        check(Pick(15, master, chopper) == chopper, "Equal-level duty is eligible when no common source is eligible");
        for (var level = 15; level <= 60; level++)
            check(Pick(level, chopper, master) == (level < 24 ? chopper : master), $"Coeurl threshold consistent at BST{level}");
        check(Pick(null, master, chopper) == chopper && Pick(0, master, chopper) == chopper,
            "Unavailable BST level keeps lowest-known fallback without claiming readiness");
        check(Pick(14, master, chopper) == chopper &&
            EncounterPolicy.RecommendationLabel(chopper, 14) == "NEXT LEVEL", "No eligible option means a clearly labelled future target");

        var common = Option(30); var duty = Option(25, 20);
        var fate = Option(10, encounter: EncounterKind.Fate, fateId: 12);
        var hunt = Option(5, encounter: EncounterKind.Hunt);
        check(Pick(30, common, duty, fate, hunt) == common, "Common at or below BST beats every lower-level special source");
        check(Pick(29, common, duty, fate, hunt) == duty, "Eligible dungeon beats lower-level FATE and hunt");
        check(Pick(24, common, duty, fate, hunt) == fate, "Eligible FATE beats hunt when common and duty are too high");
        check(Pick(9, common, duty, fate, hunt) == hunt, "Eligible hunt is used only after other known eligible tiers are exhausted");
        check(Pick(30, Option(31), hunt) == hunt, "Over-level common source never displaces an eligible hunt");
        check(Pick(30, Option(31, 20), fate) == fate, "Over-level duty never displaces an eligible FATE");
        check(Pick(30, common, Option(26))?.Point.Level == 26, "Lowest level breaks ties within common tier");
        check(Pick(30, duty, Option(20, 20))?.Point.Level == 20, "Lowest level breaks ties within duty tier");
        check(Pick(30, fate, Option(8, encounter: EncounterKind.Fate, fateId: 90))?.Point.Level == 8,
            "Lowest level breaks ties within FATE tier");
        check(Pick(30, hunt, Option(3, encounter: EncounterKind.Hunt))?.Point.Level == 3,
            "Lowest level breaks ties within hunt tier");
        foreach (var a in new[] { common, duty, fate, hunt })
        foreach (var b in new[] { common, duty, fate, hunt }.Where(x => x != a))
        foreach (var c in new[] { common, duty, fate, hunt }.Where(x => x != a && x != b))
        {
            var d = new[] { common, duty, fate, hunt }.Single(x => x != a && x != b && x != c);
            check(SourceReconciler.RankForPlayer([a, b, c, d], index, 30).SequenceEqual([common, duty, fate, hunt]),
                "Tier ordering is independent of input order");
        }
        check(SourceReconciler.LowestReported([common, duty, fate, hunt], index) == hunt,
            "Absolute-lowest lookup remains separate from convenience preference");
        var unknown = Option(null);
        check(Pick(30, unknown, duty) == duty && Pick(30, Option(0), duty) == duty,
            "Unknown/zero levels are not silently eligible");
        check(EncounterPolicy.RecommendationLabel(unknown, 30) == "LEVEL ?" &&
            EncounterPolicy.RecommendationLabel(duty, null) == "LOWEST KNOWN", "Fallback badges do not falsely claim level-readiness");
        check(EncounterPolicy.RecommendationReason(duty, index, 30).Contains("ahead of FATEs and hunts"),
            "Visible reason explains convenience-tier selection");

        foreach (var uncertain in new[]
        {
            Option(1, encounter: EncounterKind.Unknown), Option(1, encounter: EncounterKind.Conditional),
            Option(1, fateId: null), Option(1, territory: 0), Option(1, territory: 99), Option(1, nameId: 0),
        })
            check(EncounterPolicy.Tier(uncertain, index) == CaptureSourceTier.Uncertain && Pick(30, uncertain, duty) == duty,
                "Unresolved conditions/identity do not impersonate common wildlife: " + uncertain.Key);
        check(Pick(30, Option(1, reported: false), duty) == duty, "A level-one unverified lookalike cannot displace a capture report");
        check(!EncounterPolicy.IsCommonOverworldWithinLevel(common with { IsHintOnly = true }, index, 30),
            "Hint-only metadata cannot activate common preference");
        check(EncounterPolicy.Tier(Option(1, fateId: 123), index) == CaptureSourceTier.Fate,
            "Positive FATE ID overrides an inconsistent Regular label");
        check(EncounterPolicy.Tier(Option(1, nameId: 999), index) == CaptureSourceTier.Hunt,
            "Elite hunt NPC-name mapping overrides Regular label");
        check(EncounterPolicy.Tier(Option(1, baseId: 888), index) == CaptureSourceTier.Hunt,
            "Reliable elite hunt base ID overrides Regular label");
        check(EncounterPolicy.Tier(Option(1, baseId: 888, reliable: false), index) == CaptureSourceTier.CommonOverworld,
            "Aggregate unreliable base ID cannot misclassify ordinary NPC identity as a hunt");
        check(EncounterPolicy.Tier(common, index with { HuntCatalogAvailable = false }) == CaptureSourceTier.CommonOverworld,
            "A missing supplemental hunt lookup does not erase an explicitly reported ordinary source");
        check(SourceReconciler.Preferred([fate, duty], index with { HuntCatalogAvailable = false }, 30) == duty,
            "Known duty/FATE/hunt tiers still work if common exclusions are unavailable");
        check(EncounterPolicy.Normalize(Option(1, nameId: 999).Point, index).Encounter == EncounterKind.Hunt,
            "Normalization retains elite hunt classification");
        check(EncounterPolicy.Normalize(Option(1, fateId: 12).Point, index).Encounter == EncounterKind.Fate,
            "Normalization retains FATE classification");
        check(EncounterPolicy.Normalize(Option(1, encounter: EncounterKind.Unknown).Point, index).Encounter == EncounterKind.Unknown,
            "Not being a known hunt is insufficient to mark unknown conditions regular");

        BeastPlan Plan(uint id, params CaptureOption[] options) => new(
            new BeastInfo(id, $"Familiar {id}", 0, "") { NameResolved = true }, options,
            SourceReconciler.Preferred(options, index, 30));
        var coeurl = Plan(33, master, chopper);
        var other = Plan(2, Option(20, 20));
        var captures = new XbmCaptureSnapshot { Available = true, CapturedRowIds = [] };
        check(CatchPlanner.Build([coeurl], captures, 16, index).Single().Source == chopper,
            "Compact source agrees with Coeurl recommendation at BST16");
        check(CatchPlanner.Build([coeurl], captures, 24, index).Single().Source == master,
            "Compact switches to common mob at equality without a world-data rebuild");
        var queue = CatchPlanner.Build([coeurl, other], captures, 30, index);
        check(queue.Select(x => x.BeastId).SequenceEqual(new uint[] { 2, 33 }) && queue[1].Source == master,
            "Across familiars keep next-lowest recommended mob order after choosing each convenient source");
        check(CatchPlanner.Build([coeurl], captures, 14, index).Length == 0,
            "Future target never enters compact catchable queue");
        check(CatchPlanner.Build([coeurl], captures with { CapturedRowIds = [33] }, 30, index).Length == 0,
            "Obtained familiar leaves compact queue");
        check(CatchPlanner.Build([coeurl], new XbmCaptureSnapshot(), 30, index).Length == 0,
            "Unavailable bestiary progress does not mark everything missing");
        check(CatchPlanner.Sort([coeurl, other], BestiarySort.LowestLevel, captures, 30, index)[0] == coeurl,
            "All-sources minimum sort uses Lv15, independently of the Lv24 recommendation");
        check(CatchPlanner.Sort([coeurl, other], BestiarySort.NextCatch, captures, 30, index)[0] == other,
            "Field guide next-catch sort agrees with compact recommended-level order");

        // Exercise import -> identity binding -> grouping -> source reconciliation -> recommendation.
        var importedIndex = index with
        {
            Duties = new Dictionary<uint, DutyInfo> { [77] = new(77, 20, "Sastasha", "Sastasha") },
            EnglishPlaceNames = new Dictionary<uint, string> { [1] = "Upper La Noscea" },
            EnglishNpcNames = new Dictionary<uint, string> { [500] = "Master Coeurl", [501] = "Chopper" },
            NpcNames = new Dictionary<uint, string> { [500] = "Master Coeurl", [501] = "Chopper" },
            EnglishBeastNames = new Dictionary<uint, string> { [33] = "Coeurl" },
            HintTerritories = new Dictionary<uint, uint[]> { [33] = [10] },
        };
        var import = CommunityReportBinder.Bind(
            [new CollectRow(33, "Coeurl", ["Lv 24 Master Coeurl - Upper La Noscea (9,21)", "Lv 15 Chopper - Sastasha"])],
            importedIndex, new HashSet<uint> { 33 });
        check(import.Points.Length == 2 && import.Unresolved.Length == 0,
            "Coeurl common and duty report lines resolve independently through real importer");
        importedIndex = importedIndex with { CommunityPoints = import.Points };
        var familiar = new BeastInfo(33, "Coeurl", 0, "")
            { NameResolved = true, Location = new(1, 1, "Upper La Noscea", true) };
        var groups = SpawnDiscovery.Build(importedIndex, [familiar], [], 160).Groups[33];
        var choices = SourceReconciler.Build(familiar, groups, importedIndex);
        check(choices.Length == 2 && choices.Count(o => o.MatchesBestiary) == 1,
            "Hint merges into common source without deleting alternative dungeon");
        check(SourceReconciler.Preferred(choices, importedIndex, 16)?.Point.Name == "Chopper" &&
            SourceReconciler.Preferred(choices, importedIndex, 30)?.Point.Name == "Master Coeurl",
            "End-to-end report/binding/grouping/reconciliation obey both user examples");
        check(SourceReconciler.Preferred(choices, importedIndex, 24)?.Point.Name == "Master Coeurl",
            "Equal-level end-to-end common recommendation includes imported conditions and hunt checks");
        // Parser classification regressions: conditions must not disappear during the ranking change.
        var locations = new HashSet<string> { CommunityParsers.LocationKey("Synthetic Area") };
        foreach (var condition in new[] { "FATE", "FATE: Event", "B Rank Hunt", "Hunt: Mark", "Elite mark" })
            check(CommunityParsers.TryReadCaptureSource(1, $"Lv 24 Enemy - {condition} - Synthetic Area", locations, out var parsed, out _) &&
                parsed!.Encounter is EncounterKind.Fate or EncounterKind.Hunt, "Preserve event/hunt conditions: " + condition);
        check(CommunityParsers.TryReadCaptureSource(1, "Lv 24 Enemy - night only - Synthetic Area", locations, out var conditional, out _) &&
            conditional!.Encounter == EncounterKind.Conditional, "Unknown spawn restrictions are not common wildlife");
    }
}
