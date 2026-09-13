// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Beastdex.Models;

namespace Beastdex.Services;

public sealed record CollectRow(uint BeastId, string EnglishName, string[] SourceLines);
public sealed record CaptureReport(uint BeastId, string Enemy, int Level, string Area,
    EncounterKind Encounter, string Conditions, float? MapX, float? MapY, string Original);

/// <summary>Independent, bounded readers for public data; no HTML is executed or displayed as markup.</summary>
public static class CommunityParsers
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    private static MatchCollection Matches(string text, string pattern) => Regex.Matches(text, pattern,
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, RegexTimeout);
    private static string Replace(string text, string pattern, string replacement) => Regex.Replace(text,
        pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, RegexTimeout);
    private static string PlainText(string html)
    {
        html = Replace(html, @"<(script|style)\b[^>]*>.*?</\1\s*>", "");
        html = Replace(html, @"<br\b[^>]*>|</(?:div|p|li)\s*>", "\n");
        html = WebUtility.HtmlDecode(Replace(html, @"<[^>]+>", " "));
        return Replace(html, @"[^\S\r\n]+", " ").Trim();
    }

    public static CollectRow[] ReadCollectHtml(string html)
    {
        if (html.Length > 4_000_000) throw new InvalidDataException("Capture report page is larger than the supported limit.");
        var rows = new List<CollectRow>();
        foreach (Match tr in Matches(html, @"<tr\b[^>]*>(.*?)</tr\s*>"))
        {
            var cells = Matches(tr.Groups[1].Value, @"<td\b[^>]*>(.*?)</td\s*>");
            if (cells.Count < 4) continue;
            var link = Matches(cells[0].Groups[1].Value, "href=[\"'](?:https://ffxivcollect\\.com)?/beasts/(\\d+)(?:\\?[^\"']*)?[\"']");
            if (link.Count != 1 || !uint.TryParse(link[0].Groups[1].Value, out var id) || id == 0) continue;
            // Require matching beast link in the name cell; fail closed if the table order changes.
            var nameLink = Matches(cells[2].Groups[1].Value, "href=[\"'](?:https://ffxivcollect\\.com)?/beasts/" + id + "(?:\\?[^\"']*)?[\"']");
            if (nameLink.Count != 1) continue;
            var name = PlainText(cells[2].Groups[1].Value);
            // Actual public table uses a span.source for EACH source, including non-levelled vendors.
            // Preserve those boundaries so a vendor record cannot be appended to a duty/NPC name.
            var sourceHtml = Replace(cells[3].Groups[1].Value,
                @"<span\b[^>]*\bclass\s*=\s*[""'][^""']*\bsource(?:\s|[""'])[^>]*>", "\n");
            var content = PlainText(sourceHtml);
            if (name.Length is 0 or > 200 || content.Length > 16000) continue;
            // Source boundaries are normally span.source. Also handle a source cell containing adjacent Lv entries.
            var lines = content.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(line => Regex.Split(line, @"(?<!^)(?=\bLv\.?\s*\d)", RegexOptions.IgnoreCase, RegexTimeout))
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            rows.Add(new CollectRow(id, name, lines));
            if (rows.Count > 2000) throw new InvalidDataException("Too many beast rows in capture report page.");
        }
        if (rows.Count == 0) throw new InvalidDataException("No supported beast/source table found. The website may have changed; old cache retained.");
        if (rows.Select(r => r.BeastId).Distinct().Count() != rows.Count)
            throw new InvalidDataException("Duplicate beast IDs in capture report page.");
        return rows.ToArray();
    }

    public static string LocationKey(string text)
    {
        var key = SpawnMatching.NormalizeName(text);
        return key.StartsWith("the ", StringComparison.Ordinal) ? key[4..] : key;
    }

    public static bool TryReadCaptureSource(uint beastId, string line, IReadOnlySet<string> locationKeys,
        out CaptureReport? report, out string error)
    {
        report = null;
        error = string.Empty;
        var match = Regex.Match(line, @"^Lv\.?\s*(?<level>\d{1,3})\s+(?:-\s*)?(?<body>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        if (!match.Success) { error = "Not a supported levelled NPC source (for example a vendor or quest)."; return false; }
        if (!int.TryParse(match.Groups["level"].Value, out var level) || level is < 1 or > 255)
        { error = "Invalid reported NPC level."; return false; }
        var body = match.Groups["body"].Value.Trim();
        float? mapX = null, mapY = null;
        var coords = Regex.Match(body, @"\(\s*(?<x>\d+(?:\.\d+)?)\s*,\s*(?<y>\d+(?:\.\d+)?)\s*\)\s*$",
            RegexOptions.CultureInvariant, RegexTimeout);
        if (coords.Success)
        {
            mapX = float.Parse(coords.Groups["x"].Value, CultureInfo.InvariantCulture);
            mapY = float.Parse(coords.Groups["y"].Value, CultureInfo.InvariantCulture);
            if (mapX is < 1 or > 100 || mapY is < 1 or > 100) { error = "Map coordinates outside supported bounds."; return false; }
            body = body[..coords.Index].Trim();
        }
        var parts = Regex.Split(body, @"\s+(?:-|–|—|/)\s+", RegexOptions.CultureInvariant, RegexTimeout);
        var areaStart = -1;
        var area = string.Empty;
        for (var i = 1; i < parts.Length; i++)
        {
            var possible = string.Join(" - ", parts[i..]);
            if (!locationKeys.Contains(LocationKey(possible))) continue;
            areaStart = i; area = possible; break; // longest matching suffix, e.g. Second Coil - Turn 1
        }
        if (areaStart < 1) { error = "Area/duty text has no exact English game-sheet match; not guessing."; return false; }
        var conditions = string.Join(" - ", parts.Skip(1).Take(areaStart - 1));
        var kind = Regex.IsMatch(conditions, @"\bFATE\b", RegexOptions.IgnoreCase, RegexTimeout) ? EncounterKind.Fate
            : Regex.IsMatch(conditions, @"\b(?:hunt|elite mark)\b", RegexOptions.IgnoreCase, RegexTimeout) ? EncounterKind.Hunt
            : conditions.Length == 0 ? EncounterKind.Regular : EncounterKind.Conditional;
        var enemy = parts[0].Trim();
        // Ambiguous hyphen-separated text might be another enemy OR a spawn restriction.
        // Preserve the restriction until the binder proves that every segment names a real NPC.
        if (kind == EncounterKind.Conditional && !conditions.Contains(':'))
            enemy = string.Join(" or ", parts[..areaStart]);
        report = new CaptureReport(beastId, enemy, level, area, kind, conditions, mapX, mapY, line);
        return true;
    }

    public static SpawnPoint[] ReadTeamcraft(string json, WorldIndex index, CancellationToken cancellation = default)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected Teamcraft monster object keyed by BNpcName ID.");
        var points = new List<SpawnPoint>();
        var structurallyValid = false;
        foreach (var monster in doc.RootElement.EnumerateObject())
        {
            cancellation.ThrowIfCancellationRequested();
            if (!uint.TryParse(monster.Name, out var nameId) || monster.Value.ValueKind != JsonValueKind.Object ||
                !monster.Value.TryGetProperty("positions", out var positions) || positions.ValueKind != JsonValueKind.Array) continue;
            structurallyValid = true;
            if (!index.NpcNames.TryGetValue(nameId, out var name)) continue;
            foreach (var position in positions.EnumerateArray())
            {
                cancellation.ThrowIfCancellationRequested();
                if (position.ValueKind != JsonValueKind.Object || !UInt(position, "map", out var mapId) ||
                    !index.Maps.TryGetValue(mapId, out var map) || !index.Territories.ContainsKey(map.TerritoryId) ||
                    !Float(position, "x", out var x) || !Float(position, "y", out var y) || !Float(position, "z", out var z) ||
                    !SpawnMatching.ValidPosition(x, y, z)) continue;
                int? level = UInt(position, "level", out var lv) && lv is > 0 and <= 255 ? (int)lv : null;
                uint? fate = UInt(position, "fate", out var f) ? f : null;
                // zoneid is PlaceName, NOT TerritoryType. Resolve territory from Map.
                // baseid is aggregate per-name metadata, NOT a reliable per-position BNpcBase; intentionally omit it.
                points.Add(new SpawnPoint(map.TerritoryId, mapId, 0, nameId, name, level, x, y, z,
                    SpawnEvidence.CommunityObservation, "FFXIV Teamcraft / Mappy public monster observations",
                    "Historical community observation; current spawn and capture eligibility unverified")
                {
                    BaseIdReliable = false,
                    EnglishName = index.EnglishNpcNames.GetValueOrDefault(nameId) ?? name,
                    Encounter = fate.HasValue ? fate > 0 ? EncounterKind.Fate : EncounterKind.Regular : EncounterKind.Unknown,
                    FateId = fate,
                    DutyId = index.Territories[map.TerritoryId].DutyId,
                });
                if (points.Count > 250000) throw new InvalidDataException("Too many community positions; import rejected.");
            }
        }
        if (!structurallyValid) throw new InvalidDataException("Unsupported Teamcraft schema; old cache retained.");
        if (points.Count == 0) throw new InvalidDataException("No community positions matched the current game maps/NPC names.");
        return points.ToArray();
    }
    private static bool UInt(JsonElement e, string key, out uint value)
    {
        value = 0;
        return e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetUInt32(out value);
    }
    private static bool Float(JsonElement e, string key, out float value)
    {
        value = 0;
        return e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetSingle(out value) && float.IsFinite(value);
    }
}
