// SPDX-License-Identifier: GPL-3.0-only
namespace Beastdex.Models;

public sealed record BeastInfo(
    uint RowId,
    string Name,
    uint IconId,
    string Description)
{
    public uint PetRowId { get; init; }
    public bool NameResolved { get; init; }
    public string TrickDescription { get; init; } = string.Empty;
    public string TemperedDescription { get; init; } = string.Empty;
    public BestiaryLocation Location { get; init; } = BestiaryLocation.None;
    public string MetadataIssue { get; init; } = string.Empty;

    public static BeastInfo Unresolved(uint rowId, string reason) =>
        new(rowId, "Name unavailable", 0, string.Empty) { MetadataIssue = reason };
}
