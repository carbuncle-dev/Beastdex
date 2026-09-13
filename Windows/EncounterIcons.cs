// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using Beastdex.Models;
using Beastdex.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Beastdex.Windows;

internal static class EncounterIcons
{
    public static void Draw(CaptureOption option, WorldIndex index, bool withLabel = true, float size = 18)
    {
        var info = EncounterPresentation.Describe(option, index);
        var color = info.Kind switch
        {
            EncounterIconKind.OpenWorld => UiTheme.Green,
            EncounterIconKind.Fate => UiTheme.Violet,
            EncounterIconKind.Hunt => UiTheme.Red,
            EncounterIconKind.Unknown or EncounterIconKind.Conditional => UiTheme.Muted,
            _ => UiTheme.Gold,
        };
        // Reserve the same icon gutter/row height as location links. Game textures
        // and fallback font glyphs are centred in it, never sized by button padding.
        var origin = ImGui.GetCursorScreenPos();
        var cell = UiTheme.IconCell;
        ImGui.Dummy(cell);
        var draw = ImGui.GetWindowDrawList();
        var wrap = BeastImages.GetIconForFrame(info.IconId, out _);
        if (wrap != null)
        {
            var image = UiIconLayout.Fit(new Vector2(wrap.Width, wrap.Height),
                new Vector2(Math.Min(size * UiTheme.Scale, cell.X), cell.Y));
            var start = UiIconLayout.Center(origin, cell, image);
            draw.AddImage(wrap.Handle, start, start + image);
        }
        else
        {
            var fallback = info.Kind switch
            {
                EncounterIconKind.OpenWorld => FontAwesomeIcon.Globe,
                EncounterIconKind.Dungeon or EncounterIconKind.Duty => FontAwesomeIcon.Dungeon,
                EncounterIconKind.Trial => FontAwesomeIcon.Crosshairs,
                EncounterIconKind.Raid => FontAwesomeIcon.Users,
                EncounterIconKind.Hunt => FontAwesomeIcon.Crosshairs,
                EncounterIconKind.Fate => FontAwesomeIcon.Bolt,
                _ => FontAwesomeIcon.QuestionCircle,
            };
            using var font = Dalamud.Interface.Utility.Raii.ImRaii.PushFont(UiBuilder.IconFont);
            var glyph = fallback.ToIconString();
            draw.AddText(UiIconLayout.Center(origin, cell, ImGui.CalcTextSize(glyph)),
                ImGui.ColorConvertFloat4ToU32(color), glyph);
        }
        UiTheme.Tooltip(info.Detail);
        if (withLabel)
        {
            ImGui.SameLine(0, UiIconLayout.Gap * UiTheme.Scale);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(color, info.Label);
            UiTheme.Tooltip(info.Detail);
        }
    }
}
