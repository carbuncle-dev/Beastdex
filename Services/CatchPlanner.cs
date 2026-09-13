// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;

namespace Beastdex.Services;

/// <summary>"Ready" refers only to mob level and missing capture, not unlocked duty/quest access.</summary>
public static class CatchPlanner
{
    public static NextCatch[] Build(IEnumerable<BeastPlan> plans, XbmCaptureSnapshot captures,
        int? bstLevel, WorldIndex index)
    {
        if (!captures.Available || bstLevel is not > 0) return [];
        var obtained = captures.CapturedRowIds.ToHashSet();
        var items = plans.Where(p => !obtained.Contains(p.Beast.RowId)).Select(p =>
            (Plan: p, Source: SourceReconciler.PreferredReported(p.Options, index, bstLevel)))
            .Where(x => x.Source != null && x.Source.Point.Level.HasValue &&
                x.Source.Point.Level.Value > 0 && x.Source.Point.Level.Value <= bstLevel.Value)
            .Select(x => new NextCatch(x.Plan, x.Source!));
        return items.OrderBy(x => x.Source.Point.Level)
            .ThenBy(x => TravelPlanning.DutyId(x.Source.Point, index) != 0 ? 1 : 0)
            .ThenBy(x => SpawnGrouping.EncounterRank(x.Source.Point.Encounter))
            .ThenBy(x => x.Plan.Beast.RowId).ToArray();
    }

    public static BeastPlan[] Sort(IEnumerable<BeastPlan> plans, BestiarySort sort,
        XbmCaptureSnapshot captures, int? bstLevel, WorldIndex index)
    {
        var obtained = captures.CapturedRowIds.ToHashSet();
        CaptureOption? Source(BeastPlan plan) => SourceReconciler.PreferredReported(plan.Options, index, bstLevel);
        int Level(BeastPlan plan) => Source(plan)?.Point.Level ?? int.MaxValue;
        int AbsoluteLowest(BeastPlan plan) => SourceReconciler.LowestReported(plan.Options, index)?.Point.Level ?? int.MaxValue;
        int ReadyBucket(BeastPlan plan)
        {
            if (!captures.Available) return 3;
            if (obtained.Contains(plan.Beast.RowId)) return 4;
            if (Level(plan) == int.MaxValue) return 2;
            return bstLevel is > 0 && Level(plan) <= bstLevel ? 0 : 1;
        }
        return sort switch
        {
            BestiarySort.BestiaryNumber => plans.OrderBy(p => p.Beast.RowId).ToArray(),
            BestiarySort.Name => plans.OrderBy(p => p.Beast.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Beast.RowId).ToArray(),
            BestiarySort.LowestLevel => plans.OrderBy(AbsoluteLowest).ThenBy(p => p.Beast.RowId).ToArray(),
            _ => plans.OrderBy(ReadyBucket).ThenBy(Level)
                .ThenBy(p => Source(p) is { } s && TravelPlanning.DutyId(s.Point, index) != 0 ? 1 : 0)
                .ThenBy(p => Source(p) is { } s ? SpawnGrouping.EncounterRank(s.Point.Encounter) : int.MaxValue)
                .ThenBy(p => p.Beast.RowId).ToArray(),
        };
    }
}

/// <summary>Preserves arrow selection across refreshes. A captured/removed selection returns to the new lowest.</summary>
public sealed class CompactSelection
{
    public uint? SelectedId { get; private set; }
    public bool FollowFirst { get; private set; } = true;
    public int Sync(IReadOnlyList<NextCatch> queue)
    {
        if (queue.Count == 0) { SelectedId = null; FollowFirst = true; return -1; }
        var i = !FollowFirst && SelectedId.HasValue ? Enumerable.Range(0, queue.Count)
            .FirstOrDefault(n => queue[n].BeastId == SelectedId, -1) : -1;
        if (i < 0) { i = 0; FollowFirst = true; }
        SelectedId = queue[i].BeastId;
        return i;
    }
    public void Move(int delta, IReadOnlyList<NextCatch> queue)
    {
        var current = Sync(queue);
        if (current < 0) return;
        var next = Math.Clamp(current + delta, 0, queue.Count - 1);
        SelectedId = queue[next].BeastId;
        FollowFirst = next == 0;
    }
    public void Reset() { FollowFirst = true; SelectedId = null; }
}
