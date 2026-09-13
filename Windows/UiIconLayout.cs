// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;

namespace Beastdex.Windows;

/// <summary>Shared logical icon gutter; glyph and texture rows use the same anchors.</summary>
public static class UiIconLayout
{
    public const float Gutter = 20;
    public const float Gap = 4;
    public static float TextOffset(float scale) => (Gutter + Gap) * scale;
    public static Vector2 Center(Vector2 origin, Vector2 cell, Vector2 content) => origin + (cell - content) / 2;
    public static Vector2 Fit(Vector2 size, Vector2 available)
    {
        if (size.X <= 0 || size.Y <= 0 || available.X <= 0 || available.Y <= 0) return Vector2.Zero;
        return size * Math.Min(available.X / size.X, available.Y / size.Y);
    }
}
