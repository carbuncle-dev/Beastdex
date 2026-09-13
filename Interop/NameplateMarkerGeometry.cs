// SPDX-License-Identifier: GPL-3.0-only
using Beastdex.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Beastdex.Interop;

/// <summary>
/// Resizes only our temporary native marker. Resolve every node from the current
/// addon; cached addresses are equality/ownership tokens, never dereferenced.
/// Restore before the next native update, addon finalization, and plugin unload.
/// </summary>
internal sealed unsafe class NameplateMarkerGeometry
{
    private readonly record struct Saved(nint Node, NameplateMarkerSizing.Layout Original,
        NameplateMarkerSizing.Placement Applied);
    private readonly Dictionary<int, Saved> changes = [];
    private nint ownerAddon;

    public bool CanMeasure(nint address, int slot)
    {
        var plate = Resolve(address, slot);
        return plate != null && plate->NameIcon != null && plate->MarkerIcon != null &&
            plate->NameIcon->Width > 0 && plate->NameIcon->Height > 0 &&
            TryScaleToRoot((AtkResNode*)plate->NameIcon, (AtkResNode*)plate->RootComponentNode, out _, out _) &&
            TryScaleToRoot(plate->MarkerIcon->ParentNode, (AtkResNode*)plate->RootComponentNode, out _, out _);
    }

    public bool Fit(nint address, int slot)
    {
        if (ownerAddon != 0 && ownerAddon != address) changes.Clear();
        ownerAddon = address;
        var plate = Resolve(address, slot);
        if (plate == null || plate->MarkerIcon == null) return false;
        var marker = (AtkResNode*)plate->MarkerIcon;
        var original = Read(marker);
        NameplateMarkerSizing.Placement placement = default;
        var fitted = plate->NameIcon != null &&
            TryScaleToRoot((AtkResNode*)plate->NameIcon, (AtkResNode*)plate->RootComponentNode, out var nx, out var ny) &&
            TryScaleToRoot(marker->ParentNode, (AtkResNode*)plate->RootComponentNode, out var mx, out var my) &&
            NameplateMarkerSizing.TryFit(original, plate->NameIcon->Width, plate->NameIcon->Height,
                nx, ny, mx, my, out placement);
        // A missing/invalid measurement must not fall back to the large quest icon.
        // Zero scale hides only our marker for this frame; it is restored next update.
        var applied = fitted ? placement : new NameplateMarkerSizing.Placement(original.X, original.Y, 0, 0);
        changes[slot] = new((nint)marker, original, applied);
        marker->SetScale(applied.ScaleX, applied.ScaleY);
        marker->SetPositionFloat(applied.X, applied.Y);
        return fitted;
    }

    public void Restore(nint address)
    {
        if (address == 0 || address != ownerAddon) { Forget(); return; }
        foreach (var (slot, saved) in changes)
        {
            var plate = Resolve(address, slot);
            if (plate == null || (nint)plate->MarkerIcon != saved.Node) continue;
            var node = (AtkResNode*)plate->MarkerIcon;
            // Do not undo another writer's later changes. Restore owned scale/position
            // independently (the game can reposition a plate between updates).
            if (Near(node->ScaleX, saved.Applied.ScaleX) && Near(node->ScaleY, saved.Applied.ScaleY))
                node->SetScale(saved.Original.ScaleX, saved.Original.ScaleY);
            if (Near(node->X, saved.Applied.X) && Near(node->Y, saved.Applied.Y))
                node->SetPositionFloat(saved.Original.X, saved.Original.Y);
        }
        Forget();
    }

    public void Forget() { changes.Clear(); ownerAddon = 0; }

    private static AddonNamePlate.NamePlateObject* Resolve(nint address, int slot)
    {
        if (address == 0 || slot < 0 || slot >= AddonNamePlate.NumNamePlateObjects) return null;
        var addon = (AddonNamePlate*)address;
        return addon->NamePlateObjectArray == null ? null : &addon->NamePlateObjectArray[slot];
    }

    private static NameplateMarkerSizing.Layout Read(AtkResNode* node) =>
        new(node->X, node->Y, node->ScaleX, node->ScaleY, node->Width, node->Height, node->OriginX, node->OriginY);

    // Both products include the same root. UI scale and the game's distance scale
    // therefore cancel in the ratio, and remain inherited by the native marker.
    // No ImGui/global scale, fixed-pixel diameter, or hand-made distance formula.
    private static bool TryScaleToRoot(AtkResNode* node, AtkResNode* root, out float x, out float y)
    {
        x = y = 1;
        if (root == null) return false;
        for (var depth = 0; node != null && depth < 32; depth++, node = node->ParentNode)
        {
            if (!float.IsFinite(node->ScaleX) || !float.IsFinite(node->ScaleY) ||
                node->ScaleX <= 0 || node->ScaleY <= 0 || !float.IsFinite(node->Rotation) ||
                MathF.Abs(node->Rotation) > .001f) return false;
            x *= node->ScaleX; y *= node->ScaleY;
            if (!float.IsFinite(x) || !float.IsFinite(y)) return false;
            if (node == root) return x > 0 && y > 0;
        }
        return false;
    }

    private static bool Near(float a, float b) => MathF.Abs(a - b) <= .0001f;
}
