// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace Beastdex.Windows;

/// <summary>A restrained game-style HUD skin. Draws our own frame; never changes native addons.</summary>
internal static class CompactSkin
{
    // The full guide, Sources, Settings and their popups use these same colors.
    // Keep the HUD's API and metrics so its geometry and saved layout do not change.
    internal static readonly Vector4 Gold = UiTheme.Gold;
    internal static readonly Vector4 Text = UiTheme.Text;
    internal static readonly Vector4 Muted = UiTheme.Muted;
    internal static readonly Vector4 Ready = UiTheme.Green;

    public static IDisposable Push() => UiTheme.Push(compact: true);

    public static bool Button(string id, FontAwesomeIcon icon, string tooltip, float side,
        bool enabled = true, Vector4? tint = null)
    {
        ImGui.BeginDisabled(!enabled);
        var clicked = false;
        try
        {
            ImGui.PushStyleColor(ImGuiCol.Text, tint ?? Gold);
            try
            {
                using var font = ImRaii.PushFont(UiBuilder.IconFont);
                clicked = ImGui.Button(icon.ToIconString() + "##" + id, new Vector2(side));
            }
            finally { ImGui.PopStyleColor(); }
        }
        finally { ImGui.EndDisabled(); }
        UiTheme.Tooltip(tooltip);
        return clicked;
    }

    public static void Frame() => UiTheme.Frame();
}
