// SPDX-License-Identifier: GPL-3.0-only
namespace Beastdex.Models;

/// <summary>
/// An area/duty hint displayed in the bestiary. It is NOT a spawn coordinate,
/// a TerritoryType row ID, or proof that every matching enemy can be captured.
/// </summary>
public sealed record BestiaryLocation(byte Key, uint RowId, string Name, bool Resolved)
{
    public static BestiaryLocation None { get; } = new(0, 0, string.Empty, false);
    public static string? SheetForKey(byte key) => key switch
    {
        1 => "PlaceName",
        2 => "ContentFinderCondition",
        _ => null,
    };

    public string KindLabel => Key switch
    {
        1 => "Area",
        2 => "Duty",
        0 => "Unspecified",
        _ => $"Unknown kind {Key}",
    };

    public string DisplayName => Resolved ? Name : Key == 0 || RowId == 0
        ? "Not specified in bestiary"
        : $"Location unavailable ({KindLabel}, #{RowId})";
}
