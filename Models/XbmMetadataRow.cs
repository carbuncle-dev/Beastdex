// SPDX-License-Identifier: GPL-3.0-only
namespace Beastdex.Models;

/// <summary>Metadata only. This record contains NO capture flags or mob levels.</summary>
public sealed record XbmMetadataRow(
    uint RowId,
    uint PetRowId,
    uint IconId,
    string Description,
    string TrickDescription,
    string TemperedDescription,
    byte LocationKey,
    uint LocationRowId);
