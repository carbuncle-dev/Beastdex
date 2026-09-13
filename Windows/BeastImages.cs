// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using Beastdex.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace Beastdex.Windows;

internal static class BeastImages
{
    public static void Draw(BeastInfo beast, float size = 48, string? tooltip = null)
    {
        var wrap = GetIconForFrame(beast.IconId, out var status);
        var side = size * UiTheme.Scale;
        if (wrap != null) ImGui.Image(wrap.Handle, FitImage(wrap, side));
        else
        {
            var pos = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(side, side));
            ImGui.GetWindowDrawList().AddText(pos + new Vector2(8, side / 3),
                ImGui.ColorConvertFloat4ToU32(UiTheme.Muted), beast.IconId == 0 ? "?" : "...");
        }
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) return;
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(beast.Name);
        if (wrap != null) ImGui.Image(wrap.Handle, FitImage(wrap, 160 * UiTheme.Scale));
        ImGui.TextDisabled($"Bestiary #{beast.RowId} / {status}");
        if (!string.IsNullOrEmpty(tooltip))
        {
            ImGui.PushTextWrapPos(360 * UiTheme.Scale);
            ImGui.TextUnformatted(tooltip);
            ImGui.PopTextWrapPos();
        }
        ImGui.EndTooltip();
    }
    internal static Vector2 FitImage(IDalamudTextureWrap wrap, float side)
    {
        var width = Math.Max(1, wrap.Width);
        var height = Math.Max(1, wrap.Height);
        return new Vector2(width, height) * (side / Math.Max(width, height));
    }

    internal static IDalamudTextureWrap? GetIconForFrame(uint iconId, out string state)
    {
        state = "No icon ID in metadata.";
        if (iconId == 0)
            return null;
        // Prefer the hi-res game artwork; fall back to standard resolution if
        // unavailable or still loading. These are borrowed frame-local wraps.
        var high = TryIconResolution(iconId, true, out var highState);
        if (high != null)
        {
            state = "Loaded (high resolution).";
            return high;
        }
        var normal = TryIconResolution(iconId, false, out var normalState);
        if (normal != null)
        {
            state = "Loaded (standard resolution).";
            return normal;
        }
        state = $"High resolution: {highState} Standard: {normalState}";
        return null;
    }

    internal static IDalamudTextureWrap? TryIconResolution(uint iconId, bool hiRes, out string status)
    {
        try
        {
            if (!Plugin.TextureProvider.TryGetFromGameIcon(new GameIconLookup(iconId, hiRes: hiRes), out var shared)
                || shared == null)
            {
                status = "Game icon path not found.";
                return null;
            }
            if (shared.TryGetWrap(out var wrap, out var error) && wrap != null)
            {
                status = "Loaded.";
                return wrap;
            }
            status = error == null ? "Loading." : $"{error.GetType().Name}: {error.Message}";
            return null;
        }
        catch (Exception ex)
        {
            status = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

}
