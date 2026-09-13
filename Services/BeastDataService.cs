// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using Beastdex.Models;
using Lumina.Excel;
using Lumina.Text.ReadOnly;

namespace Beastdex.Services;

/// <summary>
/// Reads the local game's sheets. RawRow avoids depending on newly named
/// XBMPet members being present in the installed Lumina.Excel assembly.
/// Metadata failures cannot turn an existing game row into a missing capture.
/// No third-party familiar catalog, name matching, or network calls are used.
/// </summary>
public sealed class BeastDataService
{
    private Dictionary<uint, BeastInfo> beasts = [];
    private string detail = string.Empty;

    public IReadOnlyDictionary<uint, BeastInfo> Beasts => beasts;
    public IReadOnlySet<uint> RowIds => beasts.Keys.ToHashSet();
    public string Status { get; private set; } = "XBMPet data has not been loaded yet.";
    public bool Available => beasts.Count > 0;

    public void Reload()
    {
        var loaded = new Dictionary<uint, BeastInfo>();
        var diagnostics = new StringBuilder();
        diagnostics.AppendLine($"Lumina: {typeof(RawRow).Assembly.GetName().Version}");
        diagnostics.AppendLine($"Client language: {Plugin.DataManager.Language}");
        diagnostics.AppendLine("Metadata source: local raw XBMPet -> Pet; PlaceName/ContentFinderCondition.");

        try
        {
            // Explicit sheet name: no generated XBMPet row type is required.
            var sheet = Plugin.DataManager.GetExcelSheet<RawRow>(name: "XBMPet");
            var columns = sheet.Columns.Select(c => new SheetColumn(c.Offset, c.Type.ToString())).ToArray();
            diagnostics.AppendLine("XBMPet raw EXH columns: " + string.Join(", ",
                columns.Select((c, i) => $"{i}:{c.Type}@{c.Offset}")));

            // Preserve the entire row-ID set even when the metadata schema is
            // unfamiliar. The working capture-state path only needs these IDs.
            foreach (var raw in sheet)
                if (raw.RowId != 0)
                    loaded[raw.RowId] = BeastInfo.Unresolved(raw.RowId, "Metadata not decoded.");

            if (!XbmSheetLayout.TryBind(columns, out var layout, out var layoutStatus))
            {
                foreach (var id in loaded.Keys.ToArray())
                    loaded[id] = BeastInfo.Unresolved(id, layoutStatus);
                diagnostics.AppendLine(layoutStatus);
                Publish(loaded, diagnostics, $"Loaded {loaded.Count} row IDs. {layoutStatus} Metadata withheld.");
                return;
            }
            diagnostics.AppendLine(layoutStatus);
            diagnostics.AppendLine($"Raw column bindings: Icon={layout!.Icon}, Pet={layout.Pet}, " +
                $"Description={layout.Description}, Location={layout.Location}, LocationKey={layout.LocationKey}.");

            var petSheet = OpenNameSheet("Pet", diagnostics);
            var placeSheet = OpenNameSheet("PlaceName", diagnostics);
            var dutySheet = OpenNameSheet("ContentFinderCondition", diagnostics);

            foreach (var raw in sheet)
            {
                if (raw.RowId == 0)
                    continue;
                if (!XbmMetadataDecoder.TryDecode(raw.RowId, layout,
                        index => ToManaged(raw.ReadColumn(index)), out var metadata, out var error))
                {
                    loaded[raw.RowId] = BeastInfo.Unresolved(raw.RowId, error);
                    continue;
                }

                var issues = new List<string>();
                var name = ResolveName(petSheet, metadata!.PetRowId, out var nameIssue);
                if (nameIssue.Length != 0)
                    issues.Add($"Pet name: {nameIssue}");
                if (metadata.IconId == 0)
                    issues.Add("XBMPet icon ID is zero.");

                var location = ResolveLocation(metadata, placeSheet, dutySheet, out var locationIssue);
                if (locationIssue.Length != 0)
                    issues.Add(locationIssue);

                loaded[raw.RowId] = new BeastInfo(raw.RowId,
                    string.IsNullOrWhiteSpace(name) ? "Name unavailable" : name,
                    metadata.IconId, metadata.Description)
                {
                    PetRowId = metadata.PetRowId,
                    NameResolved = !string.IsNullOrWhiteSpace(name),
                    TrickDescription = metadata.TrickDescription,
                    TemperedDescription = metadata.TemperedDescription,
                    Location = location,
                    MetadataIssue = string.Join(" ", issues),
                };
            }

            var names = loaded.Values.Count(b => b.NameResolved);
            var icons = loaded.Values.Count(b => b.IconId != 0);
            var locations = loaded.Values.Count(b => b.Location.Resolved);
            Publish(loaded, diagnostics, $"Loaded {loaded.Count} familiar rows: {names} names, " +
                $"{icons} icon IDs, {locations} bestiary location hints.");
        }
        catch (Exception ex)
        {
            // If enumeration already yielded IDs, keep them for capture queries.
            // Otherwise publish an empty set, not stale metadata from an old read.
            diagnostics.AppendLine($"Exception: {ex.GetType().Name}: {ex.Message}");
            Publish(loaded, diagnostics, $"Metadata read failed: {ex.GetType().Name}: {ex.Message}");
            Plugin.Log.Warning(ex, "Failed to read Beastmaster metadata from local game sheets.");
        }
    }

    private void Publish(Dictionary<uint, BeastInfo> loaded, StringBuilder diagnostics, string status)
    {
        // Metadata refreshes and drawing are on Dalamud's main thread. Publish
        // replacement dictionaries, never mutate a dictionary already in use.
        beasts = loaded;
        Status = status;
        detail = diagnostics.ToString();
    }

    private sealed record NameSheet(string Name, ExcelSheet<RawRow> Rows, int NameColumn);

    private static NameSheet? OpenNameSheet(string name, StringBuilder diagnostics)
    {
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<RawRow>(name: name);
            // The schema's Name field is the string at physical offset zero in
            // each of Pet, PlaceName and ContentFinderCondition. Find its raw
            // column index; never assume schema order equals raw EXH order.
            var indices = sheet.Columns.Select((c, i) => (Column: c, Index: i))
                .Where(x => x.Column.Offset == 0 && x.Column.Type.ToString() == "String")
                .Select(x => x.Index).ToArray();
            if (indices.Length != 1)
                throw new InvalidOperationException($"{name}.Name is not a unique String at offset 0.");
            diagnostics.AppendLine($"{name}.Name: raw column {indices[0]}, String@0; {sheet.Count} rows.");
            return new NameSheet(name, sheet, indices[0]);
        }
        catch (Exception ex)
        {
            diagnostics.AppendLine($"{name} unavailable: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string ResolveName(NameSheet? sheet, uint id, out string issue)
    {
        issue = string.Empty;
        if (id == 0)
        {
            issue = "Reference ID is zero.";
            return string.Empty;
        }
        if (sheet == null)
        {
            issue = "Name sheet unavailable (see sheet diagnostics).";
            return string.Empty;
        }
        try
        {
            if (!sheet.Rows.TryGetRow(id, out var raw))
            {
                issue = $"{sheet.Name} row {id} does not exist.";
                return string.Empty;
            }
            var name = ToManaged(raw.ReadColumn(sheet.NameColumn)) as string ?? string.Empty;
            name = name.Trim();
            if (name.Length == 0)
                issue = $"{sheet.Name} row {id} has an empty name.";
            return name;
        }
        catch (Exception ex)
        {
            issue = $"{sheet.Name} row {id}: {ex.GetType().Name}: {ex.Message}";
            return string.Empty;
        }
    }

    private static BestiaryLocation ResolveLocation(XbmMetadataRow metadata,
        NameSheet? places, NameSheet? duties, out string issue)
    {
        issue = string.Empty;
        if (metadata.LocationKey == 0 || metadata.LocationRowId == 0)
            return new BestiaryLocation(metadata.LocationKey, metadata.LocationRowId, string.Empty, false);

        var sheetName = BestiaryLocation.SheetForKey(metadata.LocationKey);
        if (sheetName == null)
        {
            issue = $"Unknown bestiary location kind {metadata.LocationKey}; reference not followed.";
            return new BestiaryLocation(metadata.LocationKey, metadata.LocationRowId, string.Empty, false);
        }

        var name = ResolveName(metadata.LocationKey == 1 ? places : duties,
            metadata.LocationRowId, out var lookupIssue);
        if (lookupIssue.Length != 0)
            issue = $"{sheetName} location: {lookupIssue}";
        return new BestiaryLocation(metadata.LocationKey, metadata.LocationRowId, name, name.Length != 0);
    }

    private static object ToManaged(object value) => value is ReadOnlySeString text
        ? text.ExtractText() : value;

    public string GetDiagnostics()
    {
        var text = new StringBuilder(detail);
        foreach (var beast in beasts.Values.OrderBy(b => b.RowId))
        {
            text.AppendLine($"  XBMPet={beast.RowId} Pet={beast.PetRowId} name={beast.Name} " +
                $"nameResolved={beast.NameResolved} icon={beast.IconId} " +
                $"locationKind={beast.Location.Key} locationRow={beast.Location.RowId} " +
                $"location={beast.Location.DisplayName}");
            if (beast.MetadataIssue.Length != 0)
                text.AppendLine($"    Metadata issue: {beast.MetadataIssue}");
        }
        return text.ToString();
    }
}
