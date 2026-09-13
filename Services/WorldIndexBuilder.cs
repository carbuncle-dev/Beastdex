// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using Beastdex.Models;
using Dalamud.Plugin.Services;
using Dalamud.Game;
using Lumina.Excel;
using Lumina.Text.ReadOnly;
using Lumina.Data.Files;
using Lumina.Excel.Sheets;

namespace Beastdex.Services;

/// <summary>Only reads local sheets/layout files. Safe to run without any live actor access.</summary>
public static class WorldIndexBuilder
{
    public static WorldIndex Build(IDataManager data, BeastInfo[] beasts, CancellationToken cancellation,
        System.Action<string> progress)
    {
        var diagnostics = new StringBuilder();
        var maps = new Dictionary<uint, MapInfo>();
        var territories = new Dictionary<uint, TerritoryInfo>();
        var aetherytes = new Dictionary<uint, AetheryteInfo>();
        var hints = new Dictionary<uint, uint[]>();
        var points = new List<SpawnPoint>();
        var places = data.GetExcelSheet<PlaceName>();
        string Place(uint id) => id != 0 && places.TryGetRow(id, out var p) ? p.Name.ExtractText() : string.Empty;
        progress("Reading local maps and bestiary area/duty links...");
        foreach (var m in data.GetExcelSheet<Map>())
        {
            cancellation.ThrowIfCancellationRequested();
            if (m.RowId == 0 || m.TerritoryType.RowId == 0) continue;
            var name = Place(m.PlaceName.RowId);
            var sub = Place(m.PlaceNameSub.RowId);
            maps[m.RowId] = new MapInfo(m.RowId, m.TerritoryType.RowId, m.PlaceName.RowId,
                m.PlaceNameSub.RowId, sub.Length == 0 ? name : $"{name} - {sub}", m.SizeFactor, m.OffsetX, m.OffsetY);
        }
        foreach (var t in data.GetExcelSheet<TerritoryType>())
        {
            cancellation.ThrowIfCancellationRequested();
            if (t.RowId == 0 || t.Map.RowId == 0) continue;
            var mapIds = maps.Values.Where(m => m.TerritoryId == t.RowId).Select(m => m.Id).Order().ToArray();
            territories[t.RowId] = new TerritoryInfo(t.RowId, Place(t.PlaceName.RowId), t.Bg.ExtractText(),
                t.PlaceName.RowId, t.ContentFinderCondition.RowId, t.Map.RowId, mapIds);
        }
        var duties = data.GetExcelSheet<ContentFinderCondition>();
        var dutyInfo = new Dictionary<uint, DutyInfo>();
        var localNames = new Dictionary<uint, string>();
        var englishNames = new Dictionary<uint, string>();
        var englishPlaces = new Dictionary<uint, string>();
        var englishBeasts = new Dictionary<uint, string>();
        var huntNames = new HashSet<uint>();
        var huntBases = new HashSet<uint>();
        var huntCatalogAvailable = false;
        var contentIcons = new Dictionary<uint, uint>();
        uint overworldIcon = 0, bstJob = 0, bstIcon = 0;
        try
        {
            foreach (var row in data.GetExcelSheet<ContentType>())
                if (row.RowId != 0 && row.Icon > 0) contentIcons[row.RowId] = row.Icon;
        }
        catch (Exception ex) { diagnostics.AppendLine($"Content-type icons unavailable: {ex.Message}"); }
        try
        {
            foreach (var row in data.GetExcelSheet<MainCommand>(ClientLanguage.English))
                if (row.Name.ExtractText().Equals("Map", StringComparison.OrdinalIgnoreCase) && row.Icon > 0)
                {
                    // MainCommand.Icon is signed; texture IDs are unsigned. Ignore missing/invalid icons.
                    overworldIcon = checked((uint)row.Icon);
                    break;
                }
        }
        catch (Exception ex) { diagnostics.AppendLine($"Map icon unavailable: {ex.Message}"); }
        try
        {
            foreach (var row in data.GetExcelSheet<ClassJob>(ClientLanguage.English))
                if (row.Abbreviation.ExtractText().Equals("BST", StringComparison.OrdinalIgnoreCase))
                {
                    bstJob = row.RowId;
                    // Standard job-icon banks. Check the local game asset, not a hardcoded BST ID.
                    foreach (var candidate in new uint[] { 62000 + bstJob, 62100 + bstJob })
                        if (data.FileExists($"ui/icon/{candidate / 1000 * 1000:D6}/{candidate:D6}.tex"))
                        { bstIcon = candidate; break; }
                    break;
                }
        }
        catch (Exception ex) { diagnostics.AppendLine($"BST icon unavailable: {ex.Message}"); }
        diagnostics.AppendLine($"Game icons: {contentIcons.Count} content types; map={overworldIcon}; BST job={bstJob}, icon={bstIcon}.");
        // English is used only to bind public reports. Display names remain in the client's language.
        try
        {
            foreach (var row in data.GetExcelSheet<BNpcName>())
                if (row.RowId != 0 && row.Singular.ExtractText().Trim() is { Length: > 0 } text)
                    localNames[row.RowId] = text;
            foreach (var row in data.GetExcelSheet<BNpcName>(ClientLanguage.English))
                if (row.RowId != 0 && row.Singular.ExtractText().Trim() is { Length: > 0 } text)
                    englishNames[row.RowId] = text;
            foreach (var row in data.GetExcelSheet<PlaceName>(ClientLanguage.English))
                if (row.RowId != 0 && row.Name.ExtractText().Trim() is { Length: > 0 } text)
                    englishPlaces[row.RowId] = text;
            var englishDuties = data.GetExcelSheet<ContentFinderCondition>(ClientLanguage.English);
            foreach (var row in duties)
                if (row.RowId != 0 && row.TerritoryType.RowId != 0 && englishDuties.TryGetRow(row.RowId, out var en))
                    dutyInfo[row.RowId] = new DutyInfo(row.RowId, row.TerritoryType.RowId,
                        row.Name.ExtractText().Trim(), en.Name.ExtractText().Trim())
                    { ContentTypeId = row.ContentType.RowId };
            // Use the validated raw Pet-name binding; do not depend on a generated Pet.Name field.
            var pets = data.GetExcelSheet<RawRow>(ClientLanguage.English, "Pet");
            var nameColumns = pets.Columns.Select((c, i) => (Column: c, Index: i))
                .Where(x => x.Column.Offset == 0 && x.Column.Type.ToString() == "String").ToArray();
            if (nameColumns.Length == 1)
                foreach (var beast in beasts)
                    if (pets.TryGetRow(beast.PetRowId, out var pet) &&
                        pet.ReadColumn(nameColumns[0].Index) is ReadOnlySeString name)
                        englishBeasts[beast.RowId] = name.ExtractText().Trim();
            diagnostics.AppendLine($"Public-data identity bindings: {localNames.Count} NPC names, {englishNames.Count} English NPC names, " +
                $"{englishBeasts.Count} English familiars, {dutyInfo.Count} duties.");
        }
        catch (Exception ex) { diagnostics.AppendLine($"Public-data identity bindings incomplete: {ex.Message}"); }
        // NotoriousMonster links BNpcName and BNpcBase in columns 0/1. Read raw columns
        // so this does not depend on the user's generated-sheet member spelling.
        // MobHuntTarget is deliberately NOT used: daily bills also name ordinary wildlife.
        try
        {
            var sheet = data.GetExcelSheet<RawRow>(name: "NotoriousMonster");
            if (sheet.Columns.Count() < 2 ||
                !IsIntegerColumn(sheet.Columns[0].Type.ToString()) ||
                !IsIntegerColumn(sheet.Columns[1].Type.ToString()))
                throw new InvalidDataException("Unexpected NotoriousMonster schema; supplemental hunt check unavailable.");
            foreach (var row in sheet)
            {
                cancellation.ThrowIfCancellationRequested();
                var nameId = Convert.ToUInt32(row.ReadColumn(0));
                var baseId = Convert.ToUInt32(row.ReadColumn(1));
                if (nameId == 0) continue;
                huntNames.Add(nameId);
                if (baseId != 0) huntBases.Add(baseId);
            }
            huntCatalogAvailable = huntNames.Count > 0;
            diagnostics.AppendLine($"Elite hunt exclusions: {huntNames.Count} name IDs, {huntBases.Count} base IDs; available={huntCatalogAvailable}.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Retain IDs already read: an unreadable late row must not erase known exclusions.
            diagnostics.AppendLine($"Hunt lookup incomplete: {ex.Message}. Known IDs are retained; other encounter types use their reports.");
        }
        foreach (var id in territories.Keys.ToArray())
            if (dutyInfo.TryGetValue(territories[id].DutyId, out var info) && info.Name.Length > 0)
                territories[id] = territories[id] with { Name = info.Name };
        foreach (var beast in beasts)
        {
            var dutyTerritory = beast.Location.Key == 2 && duties.TryGetRow(beast.Location.RowId, out var duty)
                ? duty.TerritoryType.RowId : 0;
            hints[beast.RowId] = SpawnMatching.ResolveHint(beast, territories.Values, maps.Values, dutyTerritory);
            diagnostics.AppendLine($"XBMPet {beast.RowId} ({beast.Name}): hint {beast.Location.Key}:{beast.Location.RowId}" +
                $" -> territories [{string.Join(",", hints[beast.RowId])}]");
        }
        foreach (var a in data.GetExcelSheet<Aetheryte>())
            if (a.RowId != 0 && a.IsAetheryte && a.Territory.RowId != 0)
                aetherytes[a.RowId] = new AetheryteInfo(a.RowId, a.Territory.RowId, a.Map.RowId,
                    Place(a.PlaceName.RowId), null, null);

        // Prefer the aetheryte's own Level references. The Level sheet is a
        // placement table, not a source of enemy combat levels.
        try
        {
            var placements = data.GetExcelSheet<Level>();
            foreach (var a in data.GetExcelSheet<Aetheryte>())
            {
                cancellation.ThrowIfCancellationRequested();
                if (!aetherytes.TryGetValue(a.RowId, out var destination)) continue;
                foreach (var reference in a.Level)
                {
                    if (reference.RowId == 0 || !placements.TryGetRow(reference.RowId, out var placement) ||
                        placement.Territory.RowId != destination.TerritoryId ||
                        !SpawnMatching.ValidPosition(placement.X, placement.Y, placement.Z)) continue;
                    var mapId = placement.Map.RowId != 0 ? placement.Map.RowId : destination.MapId;
                    if (!maps.TryGetValue(mapId, out var map) || map.TerritoryId != destination.TerritoryId) continue;
                    if (destination.MapId != 0 && destination.MapId != mapId) continue;
                    aetherytes[a.RowId] = destination with { MapId = mapId, X = placement.X, Z = placement.Z };
                    break;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { diagnostics.AppendLine($"Direct aetheryte placements unavailable: {ex.Message}"); }

        // The Level sheet supplies placements, NOT combat levels. Only its
        // Type=12 aetheryte placements are used for optional distance sorting.
        try
        {
            foreach (var l in data.GetExcelSheet<Level>())
            {
                cancellation.ThrowIfCancellationRequested();
                if (l.Type != 12 || !aetherytes.TryGetValue(l.Object.RowId, out var a) ||
                    l.Territory.RowId != a.TerritoryId || !SpawnMatching.ValidPosition(l.X, l.Y, l.Z)) continue;
                if (a.X == null)
                    aetherytes[a.Id] = a with { MapId = l.Map.RowId, X = l.X, Z = l.Z };
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { diagnostics.AppendLine($"Aetheryte placement unavailable: {ex.Message}"); }

        try
        {
            var names = data.GetExcelSheet<BNpcName>();
            var bases = data.GetExcelSheet<BNpcBase>();
            var wanted = hints.Values.SelectMany(ids => ids).Distinct().Order().ToArray();
            var files = 0;
            var rejected = 0;
            var pointLimit = false;
            for (var territoryIndex = 0; territoryIndex < wanted.Length; territoryIndex++)
            {
                cancellation.ThrowIfCancellationRequested();
                var territory = territories[wanted[territoryIndex]];
                progress($"Searching client layouts {territoryIndex + 1}/{wanted.Length}: {territory.Name}");
                var paths = SpawnMatching.LayoutPaths(territory.Bg);
                if (paths.Length == 0) diagnostics.AppendLine($"Territory {territory.Id}: no supported Bg/level path ({territory.Bg}).");
                foreach (var path in paths)
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        if (!data.FileExists(path))
                        {
                            diagnostics.AppendLine($"Missing: {path}");
                            continue;
                        }
                        var file = data.GetFile<LgbFile>(path);
                        if (file == null) { diagnostics.AppendLine($"Unreadable: {path}"); continue; }
                        files++;
                        var before = points.Count;
                        foreach (var layer in file.Layers)
                        foreach (var instance in layer.InstanceObjects)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (instance.AssetType.ToString() != "BattleNPC" || instance.Object == null) continue;
                            if (!LayoutNpcReader.TryRead(instance.Object, out var npc, out var error))
                            {
                                if (rejected++ < 12) diagnostics.AppendLine($"Rejected BNPC in {path}: {error}");
                                continue;
                            }
                            if (!bases.TryGetRow(npc!.BaseId, out _) || !names.TryGetRow(npc.NameId, out var nameRow))
                            { rejected++; continue; }
                            var name = nameRow.Singular.ExtractText().Trim();
                            var pos = instance.Transform.Translation;
                            if (name.Length == 0 || !SpawnMatching.ValidPosition(pos.X, pos.Y, pos.Z))
                            { rejected++; continue; }
                            if (points.Count >= 50000) { pointLimit = true; break; }
                            // Multiple floor maps: keep map unknown instead of choosing
                            // the first floor. The UI will offer an explicit map selector.
                            var mapId = territory.MapIds.Length == 1 ? territory.MapIds[0] : 0;
                            var conditions = $"{npc.Conditions}; layer {layer.LayerId} ({layer.Name})";
                            points.Add(new SpawnPoint(territory.Id, mapId, npc.BaseId, npc.NameId, name,
                                npc.Level, pos.X, pos.Y, pos.Z, SpawnEvidence.ClientLayout,
                                $"{path} / instance {instance.InstanceId}", conditions)
                            { DutyId = territory.DutyId, EnglishName = englishNames.GetValueOrDefault(npc.NameId) ?? name });
                        }
                        diagnostics.AppendLine($"Read {path}: {points.Count - before} named BNPC placement(s).");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { diagnostics.AppendLine($"Layout error {path}: {ex.GetType().Name}: {ex.Message}"); }
                    if (pointLimit) break;
                }
                if (pointLimit) break;
            }
            diagnostics.AppendLine($"Loaded {files} layout files, {points.Count} named NPC placements; rejected {rejected}; capped={pointLimit}.");
            diagnostics.AppendLine("Client layouts are incomplete evidence: server/dynamic spawns, nested shared groups, " +
                "layer activation and relative spawn distributions are not reconstructed. No NPC->familiar capture mapping is asserted.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            diagnostics.AppendLine($"NPC lookup unavailable; area/travel links retained: {ex.GetType().Name}: {ex.Message}");
        }
        diagnostics.AppendLine($"Aetheryte positions: {aetherytes.Values.Count(a => a.X.HasValue && a.Z.HasValue)}/{aetherytes.Count} resolved.");
        return new WorldIndex(territories, maps, aetherytes, hints, points.ToArray(), diagnostics.ToString())
        {
            Duties = dutyInfo, NpcNames = localNames, EnglishNpcNames = englishNames,
            EnglishBeastNames = englishBeasts, EnglishPlaceNames = englishPlaces,
            HuntNameIds = huntNames, HuntBaseIds = huntBases, HuntCatalogAvailable = huntCatalogAvailable,
            ContentTypeIcons = contentIcons, OpenWorldIconId = overworldIcon,
            BeastmasterJobId = bstJob, BeastmasterIconId = bstIcon,
        };
    }

    private static bool IsIntegerColumn(string type) => type is "UInt16" or "UInt32" or "Int16" or "Int32";
}
