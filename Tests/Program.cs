// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Interop;
using Beastdex.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

internal static class Program
{
    private static int tests;

    private static int Main()
    {
        try
        {
            RunDecoderTests();
            RunManagerTests();
            MetadataTests.Run(Check);
            WorldTests.Run(Check);
            SourceTests.Run(Check);
            PlanningTests.Run(Check);
            PreferenceTests.Run(Check);
            IdentityAndMarkerTests.Run(Check);
            MarkerLayoutTests.Run(Check);
            ThemeTests.Run(Check);
            StartupTests.Run(Check);
            Console.WriteLine($"PASS: {tests} regression checks. No game process or Dalamud installation used.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL after {tests} checks: {ex}");
            return 1;
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException(name);
        tests++;
        Console.WriteLine($"  PASS: {name}");
    }

    private static void RunDecoderTests()
    {
        var hex = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "captured-settings.hex"));
        var sample = Convert.FromHexString(string.Concat(hex.Where(c => !char.IsWhiteSpace(c))));
        uint[] expected = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 25, 22, 11, 12, 14, 19,
            16, 15, 27, 13, 31, 21, 20, 26, 41, 36, 42, 30, 34, 40, 29];
        var known = expected.ToHashSet();
        Check(sample.Length == 124, "User fixture has 124 bytes");
        Check(XbmPetSettingDecoder.TryDecode(sample, known, out var records, out var error), $"User fixture decoded: {error}");
        Check(records.Length == 31, "Four-byte stride gives exactly 31 settings records");
        Check(records.Select(r => r.RowId).SequenceEqual(expected), "Preserves every observed ID and its order");
        Check(records.Where(r => r.Size == 2).Select(r => r.RowId).SequenceEqual(new uint[] { 3, 25 }),
            "Size 2 belongs only to IDs 3 and 25");
        Check(records.All(r => r.IsNewPetSeen && r.Mirage == 0), "Seen and mirage decoded separately");

        Check(XbmPetSettingDecoder.TryDecode(new byte[] { 1, 0, 0, 1 }, known, out records, out _),
            "Small, unseen, alternative appearance is a valid record");
        Check(records is [{ IsNewPetSeen: false, Size: 0, Mirage: 1 }], "Unseen is not discarded");
        Check(XbmPetSettingDecoder.TryDecode([], known, out records, out _) && records.Length == 0,
            "Empty settings decode as zero settings, not a capture assertion");
        Check(XbmPetSettingDecoder.TryDecode(sample, null, out records, out _) && records.Length == 31,
            "Diagnostic-only parsing works without metadata");
        Check(!XbmPetSettingDecoder.TryDecode(sample, new HashSet<uint>(), out records, out _) && records.Length == 0,
            "An explicitly empty known-ID set cannot validate nonempty settings");
        Check(!XbmPetSettingDecoder.TryDecode(sample.AsSpan(0, sample.Length - 1), known, out _, out _),
            "Rejects partial records");
        Check(!XbmPetSettingDecoder.TryDecode(new byte[4100], null, out _, out _), "Rejects oversized payloads");
        var duplicate = (byte[])sample.Clone();
        duplicate[4] = duplicate[0];
        Check(!XbmPetSettingDecoder.TryDecode(duplicate, known, out records, out _) && records.Length == 0,
            "Rejects duplicates without publishing a partial list");
        foreach (var (offset, value) in new (int, byte)[] { (0, 0), (0, 255), (1, 3), (2, 2), (3, 2) })
        {
            var bad = (byte[])sample.Clone();
            bad[offset] = value;
            Check(!XbmPetSettingDecoder.TryDecode(bad, known, out _, out _),
                $"Rejects invalid ID/enum at byte {offset}: {value}");
        }
    }

    private static unsafe void RunManagerTests()
    {
        var reader = new MappedXbmManagerReader(typeof(XBMManager).Assembly);
        Check(reader.IsBound, $"Manager API adapters bind: {reader.BindingStatus}");
        var known = Enumerable.Range(1, 50).Select(x => (uint)x).ToHashSet();
        var fake = new XBMManager();
        XBMManager.Current = (nint)(&fake);
        try
        {
            XBMManager.QueryCount = 0;
            fake.State = XBMManager.DataState.None;
            Check(!reader.Read(known).Available && XBMManager.QueryCount == 0,
                "State None is unavailable, not an empty captured list");
            fake.State = XBMManager.DataState.Requested;
            Check(!reader.Read(known).Available && XBMManager.QueryCount == 0,
                "State Requested does not query captures");
            fake.State = XBMManager.DataState.Received;
            var result = reader.Read(known);
            Check(result.Available && result.CapturedRowIds.Count == 0, "Received plus zero unlocks is valid");
            Check(XBMManager.LastQueriedInstance == (nint)(&fake), "Adapter passes original instance, not a boxed copy");

            // ID 50 is deliberately absent from the settings fixture above.
            fake.TestUnlockBits = (1UL << 0) | (1UL << 49);
            fake.NumUnlockedPets = 2;
            XBMManager.QueryCount = 0;
            result = reader.Read(known);
            Check(result.Available && result.CapturedRowIds.SequenceEqual(new uint[] { 1, 50 }),
                "Authoritative API includes captures absent from settings");
            Check(XBMManager.QueryCount == known.Count, "Queries every game-data row, not only settings IDs");

            // Reported 2026-09-11: 32 confirmed captures, but only 31 settings.
            // Familiar 18 is captured without a preferences record.
            uint[] currentCaptures = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
                18, 19, 20, 21, 22, 25, 26, 27, 29, 30, 31, 34, 36, 40, 41, 42];
            fake.TestUnlockBits = currentCaptures.Aggregate(0UL, (bits, id) => bits | (1UL << ((int)id - 1)));
            fake.NumUnlockedPets = 32;
            result = reader.Read(known);
            Check(result.Available && result.CapturedRowIds.SequenceEqual(currentCaptures),
                "Latest 32-capture fixture includes familiar 18 despite absent settings");
            fake.TestUnlockBits = (1UL << 0) | (1UL << 49);

            fake.NumUnlockedPets = 3;
            result = reader.Read(known);
            Check(!result.Available && result.CapturedRowIds.Count == 0, "Count mismatch cannot publish partial captures");
            fake.NumUnlockedPets = -1;
            Check(!reader.Read(known).Available, "Negative count rejected");
            fake.NumUnlockedPets = 51;
            Check(!reader.Read(known).Available, "Out-of-range count rejected");
            Check(!reader.Read(new HashSet<uint>()).Available, "Missing sheet metadata stays unavailable");
            XBMManager.Current = 0;
            Check(!reader.Read(known).Available, "Null manager stays unavailable");
        }
        finally { XBMManager.Current = 0; }

        var missing = new MappedXbmManagerReader(typeof(string).Assembly);
        Check(!missing.IsBound && !missing.Read(known).Available, "Older assembly without XBMManager fails closed");
    }
}
