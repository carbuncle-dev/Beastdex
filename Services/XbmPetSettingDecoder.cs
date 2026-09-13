// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;

namespace Beastdex.Services;

/// <summary>
/// Pure managed decoder for diagnostics. It NEVER determines capture status.
/// Layout: [XBMPet row ID, size, new-entry-seen boolean, mirage], four bytes.
/// Rejects incomplete or inconsistent data rather than guessing capture state.
/// </summary>
public static class XbmPetSettingDecoder
{
    public const int RecordSize = 4;
    public const int MaxPayloadBytes = 4096;

    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        IReadOnlySet<uint>? knownRows,
        out XbmPetSetting[] settings,
        out string error)
    {
        settings = [];
        error = string.Empty;

        if (payload.Length > MaxPayloadBytes)
        {
            error = $"Settings payload exceeds {MaxPayloadBytes} bytes.";
            return false;
        }

        if (payload.Length % RecordSize != 0)
        {
            error = $"Settings payload is {payload.Length} bytes; expected whole four-byte records.";
            return false;
        }

        var decoded = new XbmPetSetting[payload.Length / RecordSize];
        var seenIds = new HashSet<uint>();

        for (var index = 0; index < decoded.Length; index++)
        {
            var record = payload.Slice(index * RecordSize, RecordSize);
            uint id = record[0];
            if (id == 0 || !seenIds.Add(id))
            {
                error = $"Settings record {index}: zero or duplicate XBMPet row ID {id}.";
                return false;
            }

            if (knownRows != null && !knownRows.Contains(id))
            {
                error = $"Settings record {index}: XBMPet row {id} is absent from the loaded game sheet.";
                return false;
            }

            if (record[1] > 2 || record[2] > 1 || record[3] > 1)
            {
                error = $"Settings record {index}, row {id}: unexpected size/seen/mirage values " +
                        $"{record[1]}/{record[2]}/{record[3]}. The layout may have changed.";
                return false;
            }

            // False (0) is valid. Seen/unseen does not mean captured/not captured.
            decoded[index] = new XbmPetSetting(id, record[1], record[2] != 0, record[3]);
        }

        settings = decoded;
        return true;
    }
}
