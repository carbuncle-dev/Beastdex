// SPDX-License-Identifier: GPL-3.0-only
using System.Net;
using System.Net.Http;
using System.Text;

namespace Beastdex.Services;

public sealed record CachedData<T>(T Value, string Status);

/// <summary>Explicit opt-in GET only. Bounded responses, cancellation, TTL, atomic cache and last-good fallback.</summary>
public static class PublicDataCache
{
    public const string CollectUrl = "https://ffxivcollect.com/beasts";
    public const string TeamcraftUrl = "https://raw.githubusercontent.com/ffxiv-teamcraft/ffxiv-teamcraft/staging/libs/data/src/lib/json/monsters.json";

    public static async Task<CachedData<T>> ReadAsync<T>(string url, string cachePath, int maximumBytes,
        Func<string, T> parse, bool force, CancellationToken token)
    {
        string? previous = null;
        DateTime? cachedAt = null;
        if (File.Exists(cachePath) && new FileInfo(cachePath).Length <= maximumBytes)
        {
            previous = await File.ReadAllTextAsync(cachePath, token).ConfigureAwait(false);
            cachedAt = File.GetLastWriteTimeUtc(cachePath);
            if (!force && DateTime.UtcNow - cachedAt.Value < TimeSpan.FromDays(7))
            {
                try { return new CachedData<T>(parse(previous), $"Cached {cachedAt:yyyy-MM-dd HH:mm} UTC"); }
                catch (Exception ex) when (ex is not OperationCanceledException) { previous = null; }
            }
        }
        try
        {
            using var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = false,
                UseCookies = false,
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Beastdex (optional-public-data-import)");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            // Fixed public URLs only; no redirects, credentials, character IDs, encounter logs or game-state uploads.
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > maximumBytes) throw new InvalidDataException("Public response too large.");
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[32768];
            int count;
            while ((count = await input.ReadAsync(chunk, token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + count > maximumBytes) throw new InvalidDataException("Public response exceeded the size limit.");
                buffer.Write(chunk, 0, count);
            }
            var text = Encoding.UTF8.GetString(buffer.ToArray());
            var value = parse(text); // validate BEFORE replacing last-known-good cache
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var temp = cachePath + ".tmp";
            await File.WriteAllTextAsync(temp, text, token).ConfigureAwait(false);
            File.Move(temp, cachePath, overwrite: true);
            return new CachedData<T>(value, $"Downloaded {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            if (previous != null)
            {
                var value = parse(previous);
                return new CachedData<T>(value, $"STALE cache {cachedAt:yyyy-MM-dd HH:mm} UTC; refresh failed: {ex.Message}");
            }
            throw new InvalidDataException($"Public import failed: {ex.GetType().Name}: {ex.Message}", ex);
        }
    }
}
