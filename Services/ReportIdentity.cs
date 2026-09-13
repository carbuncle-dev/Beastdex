// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;

namespace Beastdex.Services;

/// <summary>Retains duplicate display-name IDs without treating them as a global capture map.</summary>
public static class ReportIdentity
{
    public static bool MatchesName(SpawnPoint report, uint nameId) => nameId != 0 &&
        (report.NameId != 0 ? report.NameId == nameId : report.PossibleNameIds.Contains(nameId));

    public static bool HasNameIdentity(SpawnPoint point) =>
        point.NameId != 0 || point.PossibleNameIds.Any(id => id != 0);

    public static IEnumerable<uint> Names(SpawnPoint point) => point.NameId != 0
        ? new[] { point.NameId } : point.PossibleNameIds.Where(id => id != 0).Distinct();

    // A source with ambiguous name IDs including a hunt is not classified as common.
    // Other ambiguity (e.g. the same name in a different dungeon) does not invalidate a report.
    public static bool HasConflictingHuntIdentity(SpawnPoint point, WorldIndex index) =>
        point.NameId == 0 && point.PossibleNameIds.Any(id => index.HuntNameIds.Contains(id));
}
