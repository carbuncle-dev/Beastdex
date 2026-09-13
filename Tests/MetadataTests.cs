// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;
using Beastdex.Services;

internal static class MetadataTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Native EXH order, deliberately NOT sorted by physical byte offset.
        // Source: EXDSchema/latest/.github/columns.yml, XBMPet, 2026-09-11.
        SheetColumn[] columns =
        [
            new(16, "Int32"), new(24, "UInt8"), new(25, "UInt8"), new(26, "UInt8"),
            new(12, "UInt32"), new(20, "UInt16"), new(27, "UInt8"), new(22, "UInt16"),
            new(0, "String"), new(4, "String"), new(8, "String"),
            new(33, "Bool"), new(34, "Bool"), new(35, "Bool"), new(36, "Bool"), new(37, "Bool"),
            new(38, "Bool"), new(39, "Bool"), new(40, "Bool"), new(41, "Bool"), new(42, "Bool"), new(43, "Bool"),
            new(28, "UInt8"), new(29, "UInt8"), new(30, "UInt8"), new(31, "UInt8"), new(32, "UInt8"),
        ];
        check(XbmSheetLayout.TryBind(columns, out var bound, out var status), $"Native metadata layout binds: {status}");
        var layout = bound!;
        check(layout.Icon == 4 && layout.Pet == 0 && layout.Description == 8 &&
              layout.Location == 7 && layout.LocationKey == 6,
            "Field binding uses verified offsets, not YAML or raw-column position guesses");

        // Synthetic metadata, not a shipped beast catalog or real capture data.
        object?[] values = new object?[columns.Length];
        values[layout.Pet] = 137;
        values[layout.Icon] = 900001u;
        values[layout.Description] = "  Test description  ";
        values[layout.TrickDescription] = "Test trick";
        values[layout.TemperedDescription] = "Test tempered action";
        values[layout.Location] = (ushort)321;
        values[layout.LocationKey] = (byte)1;
        check(XbmMetadataDecoder.TryDecode(7, layout, i => values[i], out var row, out var error),
            $"Synthetic metadata row decoded: {error}");
        check(row is { RowId: 7, PetRowId: 137, IconId: 900001, LocationKey: 1, LocationRowId: 321 },
            "Bestiary row, linked Pet row, icon and location IDs remain distinct");
        check(row!.Description == "Test description" && row.TrickDescription == "Test trick",
            "Text is preserved and trimmed");

        var reversedColumns = columns.Reverse().ToArray();
        var reversedValues = values.Reverse().ToArray();
        check(XbmSheetLayout.TryBind(reversedColumns, out var reversed, out _),
            "Equivalent reordered EXH still binds");
        check(XbmMetadataDecoder.TryDecode(7, reversed!, i => reversedValues[i], out var reversedRow, out _)
              && row == reversedRow, "Reordering EXH does not change decoded metadata");

        check(!XbmSheetLayout.TryBind(columns[..^1], out var invalidLayout, out _) && invalidLayout == null,
            "Truncated EXH fails closed");
        check(!XbmSheetLayout.TryBind([.. columns, new(44, "UInt8")], out _, out _),
            "Unexpected extra EXH column fails closed");
        var changed = columns.ToArray();
        changed[layout.Icon] = new(12, "Int32");
        check(!XbmSheetLayout.TryBind(changed, out _, out _), "Changed icon column type fails closed");
        changed = columns.ToArray();
        changed[layout.Icon] = new(11, "UInt32");
        check(!XbmSheetLayout.TryBind(changed, out _, out _), "Shifted field offset fails closed");
        changed = columns.ToArray();
        changed[layout.Icon] = new(16, "UInt32");
        check(!XbmSheetLayout.TryBind(changed, out _, out _), "Duplicate physical offset fails closed");

        values[layout.Pet] = -137;
        check(!XbmMetadataDecoder.TryDecode(7, layout, i => values[i], out row, out _) && row == null,
            "Negative Pet reference is not wrapped into a huge unsigned ID");
        values[layout.Pet] = 137;
        values[layout.Icon] = true;
        check(!XbmMetadataDecoder.TryDecode(7, layout, i => values[i], out _, out _),
            "Boolean is not silently converted into icon ID one");
        values[layout.Icon] = "900001";
        check(!XbmMetadataDecoder.TryDecode(7, layout, i => values[i], out _, out _),
            "String is not silently accepted as an icon number");
        values[layout.Icon] = 900001u;
        check(!XbmMetadataDecoder.TryDecode(0, layout, i => values[i], out _, out _),
            "Row zero is excluded");
        check(!XbmMetadataDecoder.TryDecode(7, layout,
                _ => throw new InvalidDataException("Test unavailable column"), out _, out error)
              && error.Contains("Test unavailable column"),
            "Column read failure produces diagnostic details without throwing");

        values[layout.Icon] = 0u;
        values[layout.Pet] = 0;
        check(XbmMetadataDecoder.TryDecode(7, layout, i => values[i], out row, out _)
              && row!.RowId == 7, "Missing icon/name references do not erase a real bestiary row");
        values[layout.LocationKey] = (byte)9;
        check(XbmMetadataDecoder.TryDecode(7, layout, i => values[i], out row, out _)
              && row!.LocationKey == 9, "Unknown location kind retained only for diagnostics");
        check(BestiaryLocation.SheetForKey(1) == "PlaceName", "Area key selects PlaceName");
        check(BestiaryLocation.SheetForKey(2) == "ContentFinderCondition", "Duty key selects ContentFinderCondition");
        check(BestiaryLocation.SheetForKey(0) == null && BestiaryLocation.SheetForKey(9) == null,
            "Unspecified or unknown location kinds do not guess a table");
        check(new BestiaryLocation(2, 321, "Test duty", true).DisplayName == "Test duty",
            "Resolved bestiary hint shows its actual label");
        check(!new BestiaryLocation(2, 321, "", false).Resolved &&
              new BestiaryLocation(2, 321, "", false).DisplayName.Contains("unavailable"),
            "Missing linked location stays explicitly unavailable");
        check(BeastInfo.Unresolved(7, "Schema mismatch").RowId == 7 &&
              !BeastInfo.Unresolved(7, "Schema mismatch").NameResolved,
            "Unresolved metadata preserves identity without inventing a name");
    }
}
