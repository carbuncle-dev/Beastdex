// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using System.Threading;
using Beastdex.Interop;
using Beastdex.Models;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Beastdex.Services;

/// <summary>
/// Verified captures come from XBMManager. XBMNoteModule is read separately for
/// settings diagnostics, never as a substitute for the captured collection.
/// Native reads happen only from Plugin's framework-update callback.
/// </summary>
public sealed class XbmCapturedService : IDisposable
{
    private const int VectorOffset = 0x48;
    private const int VectorSize = 0x18;
    private readonly BeastDataService beastData;
    private readonly MappedXbmManagerReader manager;
    private readonly BestiaryStartupService startup;
    private XbmCaptureSnapshot snapshot = new();
    private int refreshRequested = 1;

    public XbmCapturedService(BeastDataService beastData)
    {
        this.beastData = beastData;
        manager = new MappedXbmManagerReader(typeof(XBMNoteModule).Assembly);
        startup = new BestiaryStartupService(this, beastData);
    }

    public bool ReadApiAvailable => manager.IsBound;
    public string InitializationStatus => startup.Status;
    public void TickStartup() => startup.Tick();
    public void ResetStartupSession() => startup.RequestReset();
    public void RequestInitialization() => startup.RequestRetry();
    public void Dispose() => startup.Dispose();

    private long lastAttemptTicks;
    private long lastSuccessTicks;
    public DateTimeOffset? LastAttemptUtc => ReadTimestamp(ref lastAttemptTicks);
    public DateTimeOffset? LastSuccessUtc => ReadTimestamp(ref lastSuccessTicks);
    private static DateTimeOffset? ReadTimestamp(ref long value)
    {
        var ticks = Volatile.Read(ref value);
        return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    public XbmCaptureSnapshot Snapshot => Volatile.Read(ref snapshot);
    public void RequestRefresh() => Interlocked.Exchange(ref refreshRequested, 1);
    public bool ConsumeRefreshRequest() => Interlocked.Exchange(ref refreshRequested, 0) != 0;

    public void Clear(string status)
    {
        Volatile.Write(ref lastSuccessTicks, 0);
        Volatile.Write(ref snapshot, new XbmCaptureSnapshot { Status = status, ManagerBinding = manager.BindingStatus });
    }

    public void Refresh()
    {
        if (!Plugin.Framework.IsInFrameworkUpdateThread)
        {
            RequestRefresh();
            return;
        }

        Volatile.Write(ref lastAttemptTicks, DateTimeOffset.UtcNow.Ticks);
        if (!Plugin.ClientState.IsLoggedIn || !Plugin.PlayerState.IsLoaded || Plugin.PlayerState.ContentId == 0)
        {
            Clear("Waiting for a logged-in character and loaded player data.");
            return;
        }

        var knownRows = beastData.RowIds;
        var result = manager.Read(knownRows);
        var next = new XbmCaptureSnapshot
        {
            Available = result.Available,
            Status = result.Status,
            CapturedRowIds = result.CapturedRowIds,
            ManagerState = result.State,
            ManagerReportedCount = result.ReportedCount,
            ManagerBinding = manager.BindingStatus,
        };

        // A diagnostic failure must NOT erase a valid captured list.
        try
        {
            next = ReadSettings(next, beastData.Available ? knownRows : null);
        }
        catch (Exception ex)
        {
            next = next with { SettingsStatus = $"Settings read failed: {ex.GetType().Name}: {ex.Message}" };
        }

        if (next.Available) Volatile.Write(ref lastSuccessTicks, Volatile.Read(ref lastAttemptTicks));
        Volatile.Write(ref snapshot, next);
    }

    private static unsafe XbmCaptureSnapshot ReadSettings(
        XbmCaptureSnapshot next, IReadOnlySet<uint>? knownRows)
    {
        if (IntPtr.Size != 8 || sizeof(XBMNoteModule) != 0x60)
            return next with { SettingsStatus = "Unrecognized XBMNoteModule size; diagnostic read disabled." };

        var module = XBMNoteModule.Instance();
        if (module == null)
            return next with { SettingsStatus = "XBMNoteModule is not available yet." };

        var vectorAddress = (nint)((byte*)module + VectorOffset);
        if (!ReadOnlyProcessMemory.TryCopy(vectorAddress, VectorSize, out var header, out var error))
            return next with { SettingsStatus = error };

        var begin = (nuint)BitConverter.ToUInt64(header, 0);
        var end = (nuint)BitConverter.ToUInt64(header, 8);
        var capacity = (nuint)BitConverter.ToUInt64(header, 16);
        next = next with { BeginAddress = begin, EndAddress = end, CapacityAddress = capacity };

        byte[] payload = [];
        if (begin != 0 || end != 0 || capacity != 0)
        {
            if (begin == 0 || end == 0 || capacity == 0 || end < begin || capacity < end)
                return next with { SettingsStatus = "Settings vector has invalid/null pointer ordering." };

            var used = end - begin;
            var reserved = capacity - begin;
            if (used > XbmPetSettingDecoder.MaxPayloadBytes || reserved > XbmPetSettingDecoder.MaxPayloadBytes ||
                used % XbmPetSettingDecoder.RecordSize != 0 || reserved % XbmPetSettingDecoder.RecordSize != 0)
                return next with { SettingsStatus = $"Invalid four-byte-record vector: {used} used / {reserved} capacity bytes." };

            if (!ReadOnlyProcessMemory.TryCopy((nint)begin, (int)used, out payload, out error))
                return next with { SettingsStatus = error };
        }

        // Do not accept bytes read across a vector reallocation.
        if (!ReadOnlyProcessMemory.TryCopy(vectorAddress, VectorSize, out var after, out error) ||
            !header.AsSpan().SequenceEqual(after))
            return next with { SettingsStatus = "Settings vector changed during the read; retrying." };

        next = next with { RawPayload = payload };
        if (!XbmPetSettingDecoder.TryDecode(payload, knownRows, out var settings, out error))
            return next with { SettingsStatus = error };

        return next with
        {
            Decoder = "PetSetting[4]",
            Settings = Array.AsReadOnly(settings),
            SettingsStatus = $"Decoded {settings.Length} preference record(s). " +
                             (knownRows == null ? "XBMPet IDs are not sheet-validated yet. " : "IDs validated against XBMPet. ") +
                             "Settings presence and IsNewPetSeen are not capture checks.",
        };
    }

    public static string GetRawPayloadHex(XbmCaptureSnapshot value)
        => value.RawPayload.Length == 0 ? "<empty>" :
            string.Join(' ', value.RawPayload.ToArray().Select(b => b.ToString("X2")));

    public string GetDiagnostics(XbmCaptureSnapshot value)
    {
        var text = new StringBuilder();
        text.AppendLine($"Beastdex {typeof(Plugin).Assembly.GetName().Version} diagnostics");
        text.AppendLine($"FFXIVClientStructs: {typeof(XBMNoteModule).Assembly.GetName().Version}");
        text.AppendLine($"Capture source: XBMManager.IsPetUnlocked; available={value.Available}");
        text.AppendLine($"Auto-refresh: {Plugin.Configuration.AutoRefreshBestiary}; interval={Plugin.Configuration.BestiaryRefreshSeconds}s; attempted={LastAttemptUtc:O}; succeeded={LastSuccessUtc:O}");
        text.Append(startup.Diagnostics);
        text.AppendLine($"Capture status: {value.Status}");
        text.AppendLine($"Manager binding: {value.ManagerBinding}");
        text.AppendLine($"Manager state: {value.ManagerState}; reported count={value.ManagerReportedCount?.ToString() ?? "unknown"}");
        text.AppendLine($"Captured IDs: {string.Join(", ", value.CapturedRowIds)}");
        text.AppendLine($"Game data: {beastData.Status}");
        text.AppendLine(beastData.GetDiagnostics());
        text.AppendLine($"XBM settings decoder: {value.Decoder}");
        text.AppendLine($"Settings status: {value.SettingsStatus}");
        text.AppendLine($"begin=0x{value.BeginAddress:X} end=0x{value.EndAddress:X} capacity=0x{value.CapacityAddress:X}");
        text.AppendLine($"payload({value.RawPayload.Length})={GetRawPayloadHex(value)}");
        text.AppendLine($"Settings IDs (not proof of capture): {string.Join(", ", value.Settings.Select(s => s.RowId))}");
        if (value.Available && value.Decoder == "PetSetting[4]")
        {
            text.AppendLine($"Captured without settings: {string.Join(", ", value.CapturedRowIds.Except(value.Settings.Select(s => s.RowId)))}");
            text.AppendLine($"Settings without capture: {string.Join(", ", value.Settings.Select(s => s.RowId).Except(value.CapturedRowIds))}");
        }
        foreach (var setting in value.Settings)
        {
            var name = beastData.Beasts.TryGetValue(setting.RowId, out var beast) ? beast.Name : "unknown";
            text.AppendLine($"  ID={setting.RowId} name={name} size={setting.SizeLabel} " +
                            $"seen={setting.IsNewPetSeen} mirage={setting.MirageLabel}");
        }
        return text.ToString();
    }
}
