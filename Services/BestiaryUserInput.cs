// SPDX-License-Identifier: GPL-3.0-only
namespace Beastdex.Services;

public static class BestiaryUserInput
{
    // Event names come from the installed Dalamud event enum. Focus/hover/setup
    // notifications do not constitute player takeover. Input is never suppressed.
    public static bool IsInteraction(string name) => name is
        "MouseDown" or "MouseUp" or "MouseClick" or "MouseDoubleClick" or "MouseWheel" or
        "InputReceived" or "InputNavigation" or "InputBaseInputReceived" or
        "ButtonPress" or "ButtonClick" or "ListButtonPress" or "ListItemClick" or
        "ListItemDoubleClick" or "ListItemPadDragDropBegin" or "ListItemPadDragDropEnd" or
        "DragDropBegin" or "DragDropEnd" or "DragDropClick" or "IconTextClick" or
        "DialogueClose" or "DialogueSubmit" or "WindowChangeScale" or "LinkMouseClick";
}
