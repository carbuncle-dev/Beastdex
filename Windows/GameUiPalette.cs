// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;

namespace Beastdex.Windows;

/// <summary>
/// Shared, game-style colors for every Beastdex window and popup.
/// The core colors are shared with the compact HUD. This class has no
/// ImGui/game dependencies so the palette and opacity rules can be tested alone.
/// </summary>
internal static class GameUiPalette
{
    internal static readonly Vector4 Background = new(.075f, .073f, .066f, 1);
    internal static readonly Vector4 Popup = new(.10f, .095f, .083f, .99f);
    internal static readonly Vector4 Gold = new(.80f, .72f, .53f, 1);
    internal static readonly Vector4 Text = new(.91f, .89f, .82f, 1);
    internal static readonly Vector4 Muted = new(.64f, .63f, .58f, 1);
    internal static readonly Vector4 Ready = new(.60f, .80f, .57f, 1);
    internal static readonly Vector4 Danger = new(.88f, .56f, .50f, 1);
    internal static readonly Vector4 Special = new(.73f, .66f, .78f, 1);

    internal static readonly Vector4 Border = new(.48f, .43f, .32f, .90f);
    internal static readonly Vector4 InnerBorder = new(.29f, .27f, .21f, .65f);
    internal static readonly Vector4 EdgeHighlight = new(.56f, .51f, .39f, .48f);
    internal static readonly Vector4 Title = new(.105f, .099f, .083f, 1);
    internal static readonly Vector4 TitleActive = new(.17f, .155f, .115f, 1);
    internal static readonly Vector4 Frame = new(.145f, .137f, .115f, 1);
    internal static readonly Vector4 FrameHovered = new(.235f, .215f, .16f, 1);
    internal static readonly Vector4 FrameActive = new(.29f, .26f, .19f, 1);
    internal static readonly Vector4 Button = new(.18f, .17f, .14f, .65f);
    internal static readonly Vector4 ButtonHovered = new(.32f, .29f, .21f, .95f);
    internal static readonly Vector4 ButtonActive = new(.40f, .35f, .24f, 1);
    internal static readonly Vector4 Selected = new(.29f, .26f, .19f, 1);
    internal static readonly Vector4 Header = new(.20f, .185f, .14f, 1);
    internal static readonly Vector4 HeaderHovered = new(.32f, .29f, .21f, .85f);
    internal static readonly Vector4 TableHeader = new(.155f, .145f, .12f, 1);
    internal static readonly Vector4 TableRule = new(.38f, .345f, .265f, .55f);
    internal static readonly Vector4 TableRuleLight = new(.30f, .28f, .225f, .32f);
    internal static readonly Vector4 AlternateRow = new(.57f, .51f, .37f, .055f);
    internal static readonly Vector4 Separator = new(.43f, .39f, .29f, .65f);
    internal static readonly Vector4 ScrollbarBackground = new(.045f, .043f, .036f, .65f);
    internal static readonly Vector4 Scrollbar = new(.36f, .33f, .25f, 1);
    internal static readonly Vector4 ScrollbarHovered = new(.50f, .45f, .33f, 1);
    internal static readonly Vector4 ScrollbarActive = new(.63f, .56f, .40f, 1);
    internal static readonly Vector4 ResizeGrip = new(.48f, .43f, .32f, .20f);
    internal static readonly Vector4 SelectionBackground = new(.56f, .48f, .30f, .40f);

    internal static Vector4 WindowBackground(float opacity)
    {
        var alpha = float.IsFinite(opacity) ? Math.Clamp(opacity, .65f, 1f) : .97f;
        return new Vector4(Background.X, Background.Y, Background.Z, alpha);
    }
}
