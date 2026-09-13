// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;

namespace Beastdex.Services;

public static class CommunityWorldLoader
{
    public static async Task<WorldIndex> LoadAsync(WorldIndex index, IReadOnlySet<uint> beastIds,
        string directory, bool captureReports, bool spawnObservations, bool force,
        System.Action<string> progress, CancellationToken token)
    {
        var points = new List<SpawnPoint>();
        var unresolved = new List<string>();
        var statuses = new List<string>();
        if (captureReports)
        {
            progress("Reading optional FFXIV Collect capture alternatives...");
            try
            {
                ReportImport Parse(string text)
                {
                    var rows = CommunityParsers.ReadCollectHtml(text);
                    var result = CommunityReportBinder.Bind(rows, index, beastIds);
                    if (result.Points.Length == 0)
                        throw new InvalidDataException("No compatible NPC capture reports resolved against this game's English sheets. " +
                            string.Join("; ", result.Unresolved.Take(3)));
                    return result;
                }
                var result = await PublicDataCache.ReadAsync(PublicDataCache.CollectUrl,
                    Path.Combine(directory, "collect-beasts-en-v1.html"), 4_000_000, Parse, force, token).ConfigureAwait(false);
                points.AddRange(result.Value.Points);
                unresolved.AddRange(result.Value.Unresolved);
                statuses.Add($"FFXIV Collect: {result.Value.Points.Length} reported sources; {result.Status}");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { statuses.Add($"FFXIV Collect unavailable: {ex.Message}"); }
        }
        if (spawnObservations)
        {
            progress("Reading optional Teamcraft NPC levels and positions...");
            try
            {
                var result = await PublicDataCache.ReadAsync(PublicDataCache.TeamcraftUrl,
                    Path.Combine(directory, "teamcraft-monsters-v1.json"), 32_000_000,
                    text => CommunityParsers.ReadTeamcraft(text, index, token), force, token).ConfigureAwait(false);
                points.AddRange(result.Value);
                statuses.Add($"Teamcraft: {result.Value.Length} historical positions; {result.Status}");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { statuses.Add($"Teamcraft unavailable: {ex.Message}"); }
        }
        if (statuses.Count == 0) statuses.Add("Community sources off. Only local layouts and your observations are used.");
        return index with { CommunityPoints = points.ToArray(), UnresolvedReports = unresolved.ToArray(),
            CommunityStatus = string.Join("\n", statuses) };
    }
}
