// SPDX-License-Identifier: GPL-3.0-only
using System.Reflection;
using System.Reflection.Emit;

namespace Beastdex.Interop;

/// <summary>
/// Calls the INSTALLED FFXIVClientStructs XBMManager wrappers, without requiring
/// that newly mapped type at compile time. No signatures or unlock-bit indexing
/// are duplicated here. If the mapping is missing, the reader stays unavailable.
///
/// Small managed IL adapters pass the original native instance address to the
/// mapped getters; they do not box/copy the manager or call methods on a snapshot.
/// All calls must happen on Dalamud's framework thread while logged in.
/// </summary>
public sealed class MappedXbmManagerReader
{
    private const string TypeName = "FFXIVClientStructs.FFXIV.Client.Game.XBMManager";

    private readonly Func<nint>? getInstance;
    private readonly Func<nint, uint, bool>? isPetUnlocked;
    private readonly Func<nint, int>? getState;
    private readonly Func<nint, int>? getCount;
    private readonly int receivedState;
    private readonly Type? stateType;

    public string BindingStatus { get; }
    public bool IsBound => getInstance != null && isPetUnlocked != null &&
                           getState != null && getCount != null;

    public MappedXbmManagerReader(Assembly clientStructsAssembly)
    {
        try
        {
            var type = clientStructsAssembly.GetType(TypeName, throwOnError: false);
            if (type == null)
            {
                BindingStatus = "Installed FFXIVClientStructs has no XBMManager mapping. " +
                                "A Dalamud build containing that upstream mapping is required; " +
                                "update XIVLauncher/Dalamud and restart when available.";
                return;
            }

            if (!type.IsValueType || !type.IsExplicitLayout)
                throw new NotSupportedException("Unexpected XBMManager type layout.");

            var instance = type.GetMethod("Instance", BindingFlags.Public | BindingFlags.Static,
                binder: null, types: Type.EmptyTypes, modifiers: null);
            var unlocked = type.GetMethod("IsPetUnlocked", BindingFlags.Public | BindingFlags.Instance,
                binder: null, types: [typeof(uint)], modifiers: null);
            var state = type.GetField("State", BindingFlags.Public | BindingFlags.Instance);
            var count = type.GetField("NumUnlockedPets", BindingFlags.Public | BindingFlags.Instance);

            if (instance == null || instance.ReturnType != type.MakePointerType() ||
                unlocked == null || unlocked.ReturnType != typeof(bool) ||
                state == null || !state.FieldType.IsEnum ||
                Enum.GetUnderlyingType(state.FieldType) != typeof(int) ||
                count == null || count.FieldType != typeof(int))
                throw new NotSupportedException("XBMManager does not expose the expected read-only API.");

            stateType = state.FieldType;
            receivedState = Convert.ToInt32(Enum.Parse(stateType, "Received", ignoreCase: false));

            // Bind to the actual installed methods/field metadata rather than
            // copying signatures or fixed offsets from a different client build.
            getInstance = BindInstance(instance);
            isPetUnlocked = BindUnlock(unlocked);
            getState = BindIntField(state);
            getCount = BindIntField(count);
            BindingStatus = "Bound installed XBMManager.Instance / IsPetUnlocked / State / NumUnlockedPets.";
        }
        catch (Exception ex)
        {
            getInstance = null;
            isPetUnlocked = null;
            getState = null;
            getCount = null;
            BindingStatus = $"XBMManager API binding unavailable: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public ManagerCaptureResult Read(IReadOnlySet<uint> knownRows)
    {
        if (!IsBound)
            return Failure(BindingStatus);

        if (knownRows.Count == 0)
            return Failure("XBMPet game data is unavailable; capture IDs cannot be enumerated yet.");

        try
        {
            var address = getInstance!();
            if (address == 0)
                return Failure("XBMManager is not available yet. Automatic initialization may still be pending; see Settings.");

            var stateValue = getState!(address);
            var stateName = Enum.GetName(stateType!, stateValue) ?? $"Unknown ({stateValue})";
            var count = getCount!(address);
            if (stateValue != receivedState)
                return Failure($"Waiting for Beastmaster collection data (state: {stateName}). " +
                               "Initialization may still be pending. Use Initialize bestiary now in Settings, or open the game bestiary manually.", stateName, count);

            if (count < 0 || count > knownRows.Count)
                return Failure($"XBMManager reports {count} unlocks, incompatible with " +
                               $"{knownRows.Count} loaded XBMPet rows.", stateName, count);

            // Query ALL real sheet rows. Never restrict queries to PetSettings:
            // a captured familiar may have no local settings entry.
            var ids = knownRows.OrderBy(id => id).Where(id => isPetUnlocked!(address, id)).ToArray();

            // Do not publish a partial/stale result if the snapshot changed.
            if (getInstance!() != address || getState!(address) != stateValue || getCount!(address) != count)
                return Failure("XBMManager changed while reading; retrying on the next refresh.", stateName, count);

            if (ids.Length != count)
                return Failure($"Capture API returned {ids.Length} familiar(s), but XBMManager reports " +
                               $"{count}. Refusing to display an incomplete captured list.", stateName, count);

            return new ManagerCaptureResult(true,
                $"Verified {ids.Length} captured familiar(s) using XBMManager.IsPetUnlocked.",
                stateName, count, Array.AsReadOnly(ids));
        }
        catch (Exception ex)
        {
            return Failure($"XBMManager read unavailable: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static ManagerCaptureResult Failure(string message, string state = "unavailable", int? count = null)
        => new(false, message, state, count, Array.Empty<uint>());

    private static Func<nint> BindInstance(MethodInfo method)
    {
        var adapter = new DynamicMethod("XbmReadInstance", typeof(nint), Type.EmptyTypes,
            typeof(MappedXbmManagerReader).Module, skipVisibility: false);
        var il = adapter.GetILGenerator();
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Ret);
        return (Func<nint>)adapter.CreateDelegate(typeof(Func<nint>));
    }

    private static Func<nint, uint, bool> BindUnlock(MethodInfo method)
    {
        var adapter = new DynamicMethod("XbmReadPetUnlocked", typeof(bool), [typeof(nint), typeof(uint)],
            typeof(MappedXbmManagerReader).Module, skipVisibility: false);
        var il = adapter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Conv_U);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
        return (Func<nint, uint, bool>)adapter.CreateDelegate(typeof(Func<nint, uint, bool>));
    }

    private static Func<nint, int> BindIntField(FieldInfo field)
    {
        var adapter = new DynamicMethod($"XbmRead{field.Name}", typeof(int), [typeof(nint)],
            typeof(MappedXbmManagerReader).Module, skipVisibility: false);
        var il = adapter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Conv_U);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);
        return (Func<nint, int>)adapter.CreateDelegate(typeof(Func<nint, int>));
    }
}

public sealed record ManagerCaptureResult(
    bool Available,
    string Status,
    string State,
    int? ReportedCount,
    IReadOnlyList<uint> CapturedRowIds);
