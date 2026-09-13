// SPDX-License-Identifier: GPL-3.0-only
namespace Beastdex.Services;

/// <summary>A managed copy of a game's EXH column descriptor; no game pointers.</summary>
public readonly record struct SheetColumn(int Offset, string Type);

/// <summary>
/// Binds semantic XBMPet fields to RAW column indices by their verified offsets.
/// EXDSchema names are in physical-offset order, NOT necessarily raw EXH order.
/// The layout is validated against the upstream schema before rows are decoded.
/// This checks structure, not semantic correctness after a future game update.
/// </summary>
public sealed record XbmSheetLayout(
    int Description, int TrickDescription, int TemperedDescription,
    int Icon, int Pet, int Location, int LocationKey)
{
    // Full known layout. Check even the fields we do not consume, so a changed
    // EXH does not accidentally become a plausible-but-wrong familiar catalog.
    private static readonly SheetColumn[] Expected =
    [
        new(0, "String"), new(4, "String"), new(8, "String"),
        new(12, "UInt32"), new(16, "Int32"),
        new(20, "UInt16"), new(22, "UInt16"),
        new(24, "UInt8"), new(25, "UInt8"), new(26, "UInt8"), new(27, "UInt8"),
        new(28, "UInt8"), new(29, "UInt8"), new(30, "UInt8"), new(31, "UInt8"), new(32, "UInt8"),
        new(33, "Bool"), new(34, "Bool"), new(35, "Bool"), new(36, "Bool"), new(37, "Bool"),
        new(38, "Bool"), new(39, "Bool"), new(40, "Bool"), new(41, "Bool"), new(42, "Bool"), new(43, "Bool"),
    ];

    public static bool TryBind(IReadOnlyList<SheetColumn> columns,
        out XbmSheetLayout? layout, out string status)
    {
        layout = null;
        if (columns.Count != Expected.Length)
        {
            status = $"Unsupported XBMPet layout: {columns.Count} columns, expected {Expected.Length}.";
            return false;
        }

        var indices = new Dictionary<int, int>();
        for (var index = 0; index < columns.Count; index++)
        {
            if (!indices.TryAdd(columns[index].Offset, index))
            {
                status = $"Unsupported XBMPet layout: repeated offset {columns[index].Offset}.";
                return false;
            }
        }

        foreach (var expected in Expected)
        {
            if (!indices.TryGetValue(expected.Offset, out var index) ||
                !string.Equals(columns[index].Type, expected.Type, StringComparison.Ordinal))
            {
                status = $"Unsupported XBMPet layout: expected {expected.Type} at offset {expected.Offset}.";
                return false;
            }
        }

        layout = new XbmSheetLayout(indices[0], indices[4], indices[8],
            indices[12], indices[16], indices[22], indices[27]);
        status = "Verified 27-column XBMPet layout; fields bound by EXH type and offset.";
        return true;
    }
}
