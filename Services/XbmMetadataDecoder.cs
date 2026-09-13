// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;

namespace Beastdex.Services;

/// <summary>
/// Pure managed metadata decoder. The adapter extracts plain strings from
/// Lumina's SeStrings before invoking this; the rest remain native CLR numbers.
/// Numeric fields are not guessed/coerced out of strings, floats, or bools.
/// </summary>
public static class XbmMetadataDecoder
{
    public static bool TryDecode(uint rowId, XbmSheetLayout layout,
        Func<int, object?> readColumn, out XbmMetadataRow? row, out string error)
    {
        row = null;
        error = string.Empty;
        if (rowId == 0)
        {
            error = "XBMPet row zero is not a familiar entry.";
            return false;
        }

        try
        {
            if (readColumn(layout.Icon) is not uint icon ||
                readColumn(layout.Pet) is not int pet || pet < 0 ||
                readColumn(layout.Location) is not ushort location ||
                readColumn(layout.LocationKey) is not byte locationKey ||
                readColumn(layout.Description) is not string description ||
                readColumn(layout.TrickDescription) is not string trick ||
                readColumn(layout.TemperedDescription) is not string tempered)
            {
                error = $"XBMPet #{rowId} has values inconsistent with the verified layout.";
                return false;
            }

            // A missing reference should affect a name/location, not erase a
            // real row from the capture API's input. Unknown location kinds are
            // retained for diagnostics, never interpreted as PlaceName by default.
            row = new XbmMetadataRow(rowId, (uint)pet, icon,
                description.Trim(), trick.Trim(), tempered.Trim(), locationKey, location);
            return true;
        }
        catch (Exception ex)
        {
            error = $"XBMPet #{rowId} read failed: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }
}
