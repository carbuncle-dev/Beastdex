// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;

namespace Beastdex.Services;

/// <summary>Managed UI cache: no world scans, public downloads or native access on Draw.</summary>
public sealed class BestiaryPlanService(BeastDataService beasts, XbmCapturedService captured, WorldSearchService world)
{
    private int worldRevision = -1;
    private IReadOnlyDictionary<uint, BeastInfo>? previousBeasts;
    private XbmCaptureSnapshot? previousCapture;
    private int? previousLevel;
    public BeastPlan[] Plans { get; private set; } = [];
    public NextCatch[] Next { get; private set; } = [];
    public int Revision { get; private set; }

    public void Update()
    {
        var changed = worldRevision != world.Revision || !ReferenceEquals(previousBeasts, beasts.Beasts);
        var levelChanged = previousLevel != world.BstLevel;
        if (changed)
        {
            worldRevision = world.Revision;
            previousBeasts = beasts.Beasts;
            Plans = beasts.Beasts.Values.Select(b =>
            {
                var options = SourceReconciler.Build(b, world.GetGroups(b.RowId), world.Index);
                return new BeastPlan(b, options, SourceReconciler.Preferred(options, world.Index, world.BstLevel));
            }).ToArray();
        }
        else if (levelChanged)
        {
            // Reaching an enemy level (including equality) changes preferred sources without a world scan/download.
            Plans = Plans.Select(p => p with
                { Preferred = SourceReconciler.Preferred(p.Options, world.Index, world.BstLevel) }).ToArray();
        }
        if (changed || !ReferenceEquals(previousCapture, captured.Snapshot) || levelChanged)
        {
            previousCapture = captured.Snapshot;
            previousLevel = world.BstLevel;
            Next = CatchPlanner.Build(Plans, previousCapture, previousLevel, world.Index);
            Revision++;
        }
    }
    public BeastPlan? Get(uint id) => Plans.FirstOrDefault(p => p.Beast.RowId == id);
}
