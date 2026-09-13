// SPDX-License-Identifier: GPL-3.0-only
namespace Beastdex.Models;

/// <summary>
/// One XBMNoteModule preference record, not an unlock record.
/// The offsets are documented by upstream FFXIVClientStructs/XBMNoteModule.
/// </summary>
public readonly record struct XbmPetSetting(
    uint RowId,
    byte Size,
    bool IsNewPetSeen,
    byte Mirage)
{
    public string SizeLabel => Size switch
    {
        0 => "Small",
        1 => "Medium",
        2 => "Large",
        _ => $"Unknown ({Size})",
    };

    public string MirageLabel => Mirage switch
    {
        0 => "Normal",
        1 => "Alternative",
        _ => $"Unknown ({Mirage})",
    };
}
