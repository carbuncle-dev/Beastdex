// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Beastdex.Windows;

internal static class UiTheme
{
    // All windows, including the compact tracker, use one palette. Do not introduce
    // per-window accent colors: popups/tooltips inherit this same scoped theme.
    public static readonly Vector4 Accent = GameUiPalette.Gold;
    public static readonly Vector4 Green = GameUiPalette.Ready;
    public static readonly Vector4 Gold = GameUiPalette.Gold;
    public static readonly Vector4 Red = GameUiPalette.Danger;
    public static readonly Vector4 Muted = GameUiPalette.Muted;
    public static readonly Vector4 Violet = GameUiPalette.Special;
    public static readonly Vector4 Text = GameUiPalette.Text;
    public static readonly Vector4 Selected = GameUiPalette.Selected;
    public static float Scale => ImGuiHelpers.GlobalScale;

    // Plugin.DrawUi owns the outer scope. The HUD uses the same colors with its
    // original, tighter metrics, then restores the larger panels' spacing.
    public static IDisposable Push(bool compact = false) => new StyleScope(compact);
    private sealed class StyleScope : IDisposable
    {
        private int colors;
        private int variables;
        private bool disposed;

        public StyleScope(bool compact)
        {
            try
            {
                Color(ImGuiCol.WindowBg, GameUiPalette.WindowBackground(Plugin.Configuration.WindowOpacity));
                Color(ImGuiCol.ChildBg, Vector4.Zero);
                Color(ImGuiCol.PopupBg, GameUiPalette.Popup);
                Color(ImGuiCol.TitleBg, GameUiPalette.Title);
                Color(ImGuiCol.TitleBgActive, GameUiPalette.TitleActive);
                Color(ImGuiCol.TitleBgCollapsed, GameUiPalette.Title);
                Color(ImGuiCol.Border, GameUiPalette.Border);
                Color(ImGuiCol.BorderShadow, Vector4.Zero);
                Color(ImGuiCol.Text, Text);
                Color(ImGuiCol.TextDisabled, Muted);
                Color(ImGuiCol.FrameBg, GameUiPalette.Frame);
                Color(ImGuiCol.FrameBgHovered, GameUiPalette.FrameHovered);
                Color(ImGuiCol.FrameBgActive, GameUiPalette.FrameActive);
                Color(ImGuiCol.Button, GameUiPalette.Button);
                Color(ImGuiCol.ButtonHovered, GameUiPalette.ButtonHovered);
                Color(ImGuiCol.ButtonActive, GameUiPalette.ButtonActive);
                Color(ImGuiCol.Header, GameUiPalette.Header);
                Color(ImGuiCol.HeaderHovered, GameUiPalette.HeaderHovered);
                Color(ImGuiCol.HeaderActive, GameUiPalette.Selected);
                Color(ImGuiCol.CheckMark, Green);
                Color(ImGuiCol.SliderGrab, Gold);
                Color(ImGuiCol.SliderGrabActive, Text);
                Color(ImGuiCol.PlotLines, Gold);
                Color(ImGuiCol.PlotLinesHovered, Text);
                Color(ImGuiCol.PlotHistogram, Gold);
                Color(ImGuiCol.PlotHistogramHovered, Text);
                Color(ImGuiCol.TableHeaderBg, GameUiPalette.TableHeader);
                Color(ImGuiCol.TableBorderStrong, GameUiPalette.TableRule);
                Color(ImGuiCol.TableBorderLight, GameUiPalette.TableRuleLight);
                Color(ImGuiCol.TableRowBg, Vector4.Zero);
                Color(ImGuiCol.TableRowBgAlt, GameUiPalette.AlternateRow);
                Color(ImGuiCol.Separator, GameUiPalette.Separator);
                Color(ImGuiCol.SeparatorHovered, Gold);
                Color(ImGuiCol.SeparatorActive, Text);
                Color(ImGuiCol.ScrollbarBg, GameUiPalette.ScrollbarBackground);
                Color(ImGuiCol.ScrollbarGrab, GameUiPalette.Scrollbar);
                Color(ImGuiCol.ScrollbarGrabHovered, GameUiPalette.ScrollbarHovered);
                Color(ImGuiCol.ScrollbarGrabActive, GameUiPalette.ScrollbarActive);
                Color(ImGuiCol.ResizeGrip, GameUiPalette.ResizeGrip);
                Color(ImGuiCol.ResizeGripHovered, GameUiPalette.ScrollbarHovered);
                Color(ImGuiCol.ResizeGripActive, Gold);
                Color(ImGuiCol.TextSelectedBg, GameUiPalette.SelectionBackground);
                Color(ImGuiCol.DragDropTarget, Gold);

                // Keep each view's existing spacing and click targets. Only the skin
                // is shared: the field guide and settings are not squeezed into HUD metrics.
                Variable(ImGuiStyleVar.WindowPadding, (compact ? new Vector2(8, 8) : new Vector2(14, 12)) * Scale);
                Variable(ImGuiStyleVar.FramePadding, (compact ? new Vector2(4, 2) : new Vector2(8, 5)) * Scale);
                Variable(ImGuiStyleVar.ItemSpacing, (compact ? new Vector2(4, 4) : new Vector2(8, 7)) * Scale);
                Variable(ImGuiStyleVar.CellPadding, (compact ? new Vector2(4, 0) : new Vector2(9, 8)) * Scale);
                Variable(ImGuiStyleVar.WindowRounding, 4 * Scale);
                Variable(ImGuiStyleVar.WindowBorderSize, 1 * Scale);
                Variable(ImGuiStyleVar.FrameRounding, 3 * Scale);
                Variable(ImGuiStyleVar.PopupRounding, 4 * Scale);
                Variable(ImGuiStyleVar.PopupBorderSize, 1 * Scale);
                Variable(ImGuiStyleVar.ScrollbarRounding, 3 * Scale);
                Variable(ImGuiStyleVar.GrabRounding, 3 * Scale);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Color(ImGuiCol id, Vector4 value) { ImGui.PushStyleColor(id, value); colors++; }
        private void Variable(ImGuiStyleVar id, Vector2 value) { ImGui.PushStyleVar(id, value); variables++; }
        private void Variable(ImGuiStyleVar id, float value) { ImGui.PushStyleVar(id, value); variables++; }
        public void Dispose()
        {
            if (disposed) return;
            if (variables > 0) ImGui.PopStyleVar(variables);
            if (colors > 0) ImGui.PopStyleColor(colors);
            disposed = true;
        }
    }

    /// <summary>Quiet gold inner bevel; draws no widgets and does not consume layout space.</summary>
    public static void Frame()
    {
        var s = Scale;
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        var draw = ImGui.GetWindowDrawList();
        // Include the frame/title area, but keep decorations on this window's draw
        // list so they never float above unrelated windows or intercept clicks.
        draw.PushClipRect(min, max, false);
        try
        {
            draw.AddRect(min + new Vector2(3 * s), max - new Vector2(3 * s),
                ImGui.ColorConvertFloat4ToU32(GameUiPalette.InnerBorder), 2 * s);
            draw.AddLine(min + new Vector2(8, 4) * s, new Vector2(max.X - 8 * s, min.Y + 4 * s),
                ImGui.ColorConvertFloat4ToU32(GameUiPalette.EdgeHighlight));
        }
        finally { draw.PopClipRect(); }
    }

    public static void Icon(FontAwesomeIcon icon, Vector4 color)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(color, icon.ToIconString());
    }

    public static bool IconButton(string id, FontAwesomeIcon icon, string tooltip, bool enabled = true)
    {
        ImGui.BeginDisabled(!enabled);
        bool clicked;
        try
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Gold);
            try { clicked = ImGuiComponents.IconButton(id, icon); }
            finally { ImGui.PopStyleColor(); }
        }
        finally { ImGui.EndDisabled(); }
        Tooltip(tooltip);
        return clicked;
    }

    public static bool Button(FontAwesomeIcon icon, string label, string tooltip = "", bool enabled = true)
    {
        ImGui.BeginDisabled(!enabled);
        var clicked = ImGuiComponents.IconButtonWithText(icon, label);
        ImGui.EndDisabled();
        Tooltip(tooltip.Length > 0 ? tooltip : label.Split("##")[0]);
        return clicked;
    }

    public static void Tooltip(string text)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            !ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) return;
        ImGui.BeginTooltip();
        try
        {
            ImGui.PushTextWrapPos(420 * Scale);
            try { ImGui.TextUnformatted(text); }
            finally { ImGui.PopTextWrapPos(); }
        }
        finally { ImGui.EndTooltip(); }
    }

    public static Vector2 IconCell => new(UiIconLayout.Gutter * Scale, ImGui.GetFrameHeight());

    // This is a single real button, not separate clickable icon/text items. Its
    // icon gutter is shared with EncounterIcons, so both rows have identical Xs.
    public static bool AlignedIconButton(FontAwesomeIcon icon, string text, string id,
        string tooltip, bool enabled = true)
    {
        var origin = ImGui.GetCursorScreenPos();
        var cell = IconCell;
        var width = Math.Max(cell.X, ImGui.GetContentRegionAvail().X);
        var size = new Vector2(width, cell.Y);
        var clicked = false;
        ImGui.BeginDisabled(!enabled);
        try
        {
            clicked = ImGui.Button("##" + id, size);
            var draw = ImGui.GetWindowDrawList();
            var color = ImGui.GetColorU32(ImGuiCol.Text);
            var display = Fit(text, Math.Max(0, width - UiIconLayout.TextOffset(Scale)));
            // Text is clipped to the cell; tiny resized columns cannot draw into travel.
            draw.PushClipRect(origin, origin + size, true);
            try
            {
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    var glyph = icon.ToIconString();
                    draw.AddText(UiIconLayout.Center(origin, cell, ImGui.CalcTextSize(glyph)), color, glyph);
                }
                draw.AddText(origin + new Vector2(UiIconLayout.TextOffset(Scale),
                    (cell.Y - ImGui.GetTextLineHeight()) / 2), color, display);
            }
            finally { draw.PopClipRect(); }
        }
        finally { ImGui.EndDisabled(); }
        Tooltip(tooltip);
        return clicked;
    }

    public static void TableHeaders(params (string Label, string Help)[] columns)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        for (var i = 0; i < columns.Length; i++)
        {
            ImGui.TableSetColumnIndex(i);
            ImGui.TableHeader(columns[i].Label);
            Tooltip(columns[i].Help);
        }
    }

    public static string Fit(string text, float width)
    {
        if (width <= 0) return "";
        if (ImGui.CalcTextSize(text).X <= width) return text;
        var dots = "...";
        var limit = width - ImGui.CalcTextSize(dots).X;
        if (limit <= 0) return dots;
        var lo = 0; var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid]).X <= limit) lo = mid;
            else hi = mid - 1;
        }
        if (lo > 0 && char.IsHighSurrogate(text[lo - 1])) lo--;
        return text[..lo] + dots;
    }

    public static void TextFit(string text, Vector4? color = null, string? tooltip = null)
    {
        var display = Fit(text, ImGui.GetContentRegionAvail().X);
        ImGui.TextColored(color ?? Text, display);
        Tooltip(tooltip ?? text);
    }

    public static void Badge(string label, Vector4 color, string? tooltip = null)
    {
        var size = ImGui.CalcTextSize(label) + new Vector2(12, 4) * Scale;
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);
        var bg = new Vector4(color.X * .24f, color.Y * .24f, color.Z * .24f, .95f);
        ImGui.GetWindowDrawList().AddRectFilled(pos, pos + size, ImGui.ColorConvertFloat4ToU32(bg), 4 * Scale);
        ImGui.GetWindowDrawList().AddText(pos + new Vector2(6, 2) * Scale, ImGui.ColorConvertFloat4ToU32(color), label);
        if (tooltip != null) Tooltip(tooltip);
    }

    public static void Heading(FontAwesomeIcon icon, string text, string? tooltip = null)
    {
        ImGui.BeginGroup();
        Icon(icon, Accent); ImGui.SameLine(); ImGui.TextColored(Accent, text);
        ImGui.EndGroup(); Tooltip(tooltip ?? text);
    }

    public static void SyncStatus(Services.XbmCapturedService captured)
    {
        var snapshot = captured.Snapshot;
        var live = Plugin.Configuration.AutoRefreshBestiary;
        var color = !snapshot.Available ? Gold : live ? Green : Muted;
        ImGui.BeginGroup();
        Icon(live ? FontAwesomeIcon.Sync : FontAwesomeIcon.Pause, color);
        ImGui.SameLine();
        var age = captured.LastAttemptUtc.HasValue ? Math.Max(0, (int)(DateTimeOffset.UtcNow - captured.LastAttemptUtc.Value).TotalSeconds) : -1;
        ImGui.TextColored(color, !snapshot.Available ? "Waiting for bestiary data" :
            (live ? "Auto-sync" : "Auto-sync paused") + (age >= 0 ? $" / {age}s ago" : ""));
        ImGui.EndGroup();
        Tooltip($"{snapshot.Status}\n{(live ? $"Checks every {Plugin.Configuration.BestiaryRefreshSeconds}s, including while windows are closed." : "Use Refresh or turn auto-sync back on in settings.")}\n" +
            "Reads only the current character's game state. Does not open the bestiary or download community data each time.");
    }
}
