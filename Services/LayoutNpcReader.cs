// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Concurrent;
using System.Reflection;

namespace Beastdex.Services;

public sealed record LayoutNpc(uint BaseId, uint NameId, int? Level, string Conditions);

/// <summary>
/// Lumina's BNPCInstanceObject is internal in some builds. Its boxed managed
/// object has public fields; read those by name, never by process-memory offset.
/// A malformed/changed shape is rejected rather than decoded heuristically.
/// </summary>
public static class LayoutNpcReader
{
    private static readonly ConcurrentDictionary<(Type, string), FieldInfo> Fields = new();
    private static object Field(object value, string name)
    {
        var key = (value.GetType(), name);
        var field = Fields.GetOrAdd(key, k => k.Item1.GetField(k.Item2, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingFieldException(k.Item1.FullName, k.Item2));
        return field.GetValue(value) ?? throw new InvalidOperationException($"Null {name}.");
    }

    public static bool TryRead(object value, out LayoutNpc? npc, out string error)
    {
        npc = null;
        error = string.Empty;
        try
        {
            var parent = Field(value, "ParentData");
            var game = Field(parent, "ParentData");
            if (Field(game, "BaseId") is not uint baseId || Field(value, "NameId") is not uint nameId ||
                Field(value, "Level") is not ushort level || baseId == 0 || nameId == 0)
                throw new InvalidOperationException("BNPC identity/level fields have an unsupported value or type.");
            var flags = new List<string>();
            foreach (var name in new[] { "PopEvent", "Nonpop", "NonpopInitZone", "InvalidRepop", "FateLayoutLabelId" })
            {
                var field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
                if (field != null && Convert.ToUInt64(field.GetValue(value)) != 0) flags.Add(name);
            }
            foreach (var name in new[] { "PopWeather", "PopTimeStart", "PopTimeEnd" })
            {
                var field = parent.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
                if (field != null && Convert.ToUInt64(field.GetValue(parent)) != 0) flags.Add(name);
            }
            npc = new LayoutNpc(baseId, nameId, level is > 0 and <= 255 ? level : null,
                flags.Count == 0 ? "Layer/event activation not verified" : string.Join(", ", flags));
            return true;
        }
        catch (Exception ex) { error = $"{ex.GetType().Name}: {ex.Message}"; return false; }
    }
}
