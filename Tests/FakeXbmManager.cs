// SPDX-License-Identifier: GPL-3.0-only
// TEST-ONLY synthetic implementation. Never compiled into the Dalamud plugin.
using System.Runtime.InteropServices;

namespace FFXIVClientStructs.FFXIV.Client.Game;

[StructLayout(LayoutKind.Explicit, Size = 0x20)]
public unsafe struct XBMManager
{
    [FieldOffset(0x00)] public ulong TestUnlockBits;
    [FieldOffset(0x10)] public int NumUnlockedPets;
    [FieldOffset(0x14)] public DataState State;
    public static nint Current;
    public static nint LastQueriedInstance;
    public static int QueryCount;

    public static XBMManager* Instance() => (XBMManager*)Current;

    public bool IsPetUnlocked(uint id)
    {
        QueryCount++;
        fixed (XBMManager* self = &this)
            LastQueriedInstance = (nint)self;
        // Synthetic test indexing; production code calls the real API instead.
        return id is >= 1 and <= 64 && (TestUnlockBits & (1UL << (int)(id - 1))) != 0;
    }

    public enum DataState { None = 0, Requested = 1, Received = 3 }
}
