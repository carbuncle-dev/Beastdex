// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Models;

namespace Beastdex.Services;

public enum EncounterIconKind { OpenWorld, Dungeon, Trial, Raid, Duty, Fate, Hunt, Conditional, Unknown }
public sealed record EncounterDescriptor(EncounterIconKind Kind, string Label, uint IconId, string Detail);

/// <summary>Encounter type is separate from capture confidence and player level.</summary>
public static class EncounterPresentation
{
    public static EncounterDescriptor Describe(CaptureOption option, WorldIndex index)
    {
        var p = option.Point;
        var duty = TravelPlanning.DutyId(p, index);
        EncounterIconKind kind;
        string label;
        uint icon = 0;
        if (EncounterPolicy.IsKnownHunt(p, index) || p.Encounter == EncounterKind.Hunt)
        { kind = EncounterIconKind.Hunt; label = "Hunt"; icon = index.ContentTypeIcons.GetValueOrDefault(33u); }
        else if (p.FateId is > 0 || p.Encounter == EncounterKind.Fate)
        { kind = EncounterIconKind.Fate; label = "FATE"; icon = index.ContentTypeIcons.GetValueOrDefault(8u); }
        else if (duty != 0)
        {
            var type = index.Duties.GetValueOrDefault(duty)?.ContentTypeId ?? 0;
            (kind, label) = type switch
            {
                2 or 21 or 22 or 30 => (EncounterIconKind.Dungeon, "Dungeon"),
                4 => (EncounterIconKind.Trial, "Trial"),
                5 or 28 or 37 => (EncounterIconKind.Raid, "Raid"),
                _ => (EncounterIconKind.Duty, "Duty"),
            };
            icon = index.ContentTypeIcons.GetValueOrDefault(type);
        }
        else if (p.Encounter == EncounterKind.Conditional)
        { kind = EncounterIconKind.Conditional; label = "Conditional"; }
        else if (!option.IsHintOnly && p.Encounter == EncounterKind.Regular && p.FateId == 0 &&
            index.Territories.ContainsKey(p.TerritoryId) && !ReportIdentity.HasConflictingHuntIdentity(p, index))
        { kind = EncounterIconKind.OpenWorld; label = "Open world"; icon = index.OpenWorldIconId; }
        else
        { kind = EncounterIconKind.Unknown; label = option.IsHintOnly ? "Area hint" : "Type unknown"; }
        var detail = label + (option.IsHintOnly ? " / bestiary hint only" : option.IsReported
            ? " / community-reported capture source" : " / capture unverified");
        if (p.Encounter == EncounterKind.Conditional && kind != EncounterIconKind.Conditional) detail += " / special conditions";
        if (option.IsReported && kind == EncounterIconKind.OpenWorld && !index.HuntCatalogAvailable)
            detail += " / local hunt cross-check unavailable";
        return new(kind, label, icon, detail);
    }
}
