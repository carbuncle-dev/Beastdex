// SPDX-License-Identifier: GPL-3.0-only
namespace Beastdex.Models;

/// <summary>A managed snapshot published after a framework-thread refresh.</summary>
public sealed record XbmCaptureSnapshot
{
    public bool Available { get; init; }
    public string Status { get; init; } = "Waiting for a framework update.";
    public IReadOnlyList<uint> CapturedRowIds { get; init; } = Array.Empty<uint>();
    public string ManagerState { get; init; } = "unavailable";
    public int? ManagerReportedCount { get; init; }
    public string ManagerBinding { get; init; } = "not initialized";
    public string Decoder { get; init; } = "none";
    public string SettingsStatus { get; init; } = "Settings have not been read.";
    public IReadOnlyList<XbmPetSetting> Settings { get; init; } = Array.Empty<XbmPetSetting>();
    public ReadOnlyMemory<byte> RawPayload { get; init; } = ReadOnlyMemory<byte>.Empty;
    public nuint BeginAddress { get; init; }
    public nuint EndAddress { get; init; }
    public nuint CapacityAddress { get; init; }
}
