using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// Which unit the player has selected, for the map overlay to draw the right cone.
///
/// The game's selection is not in any dump this project has, so it is looked for by name at runtime:
/// a member on the running game object whose name mentions "select" and whose value is a unit or a
/// collection of units. If that is not there, the mod falls back to the last unit whose right-click
/// menu it built - which is what the player just clicked, so it is nearly always the same answer.
///
/// The fallback matters: a cone that appears when you right-click a ship is useful, a cone that never
/// appears because an unnamed member could not be found is not.
/// </summary>
internal static class Selection
{
    private static bool discovered;
    private static MemberInfo? member;

    private static ObjectBase? lastTouched;

    /// <summary>Remembers the unit the player just acted on. Called from the menu patch.</summary>
    internal static void Note(ObjectBase? unit)
    {
        if (unit == null) return;
        lastTouched = unit;
    }

    /// <summary>The selected unit, or the one most recently interacted with.</summary>
    internal static ObjectBase? Current()
    {
        ObjectBase? fromGame = FromGame();
        if (fromGame != null && !IsGone(fromGame)) return fromGame;

        if (lastTouched != null && !IsGone(lastTouched)) return lastTouched;
        return null;
    }

    private static ObjectBase? FromGame()
    {
        Discover();
        if (member == null) return null;

        try
        {
            object? owner = GameWorld.GameMainInstance;
            if (owner == null) return null;

            object? value = MemberProbe.Read(member, owner);

            if (value is ObjectBase single) return single;

            if (value is IEnumerable list)
            {
                foreach (object? item in list)
                {
                    if (item is ObjectBase unit) return unit;
                }
            }
        }
        catch (Exception) { }

        return null;
    }

    private static void Discover()
    {
        if (discovered) return;
        discovered = true;

        try
        {
            object? owner = GameWorld.GameMainInstance;
            if (owner == null) return;

            foreach (MemberInfo candidate in MemberProbe.All(owner.GetType()))
            {
                if (candidate is MethodInfo) continue;

                string name = candidate.Name.ToLowerInvariant();
                if (!name.Contains("select")) continue;

                Type type = candidate is PropertyInfo property ? property.PropertyType
                            : ((FieldInfo)candidate).FieldType;

                if (type != typeof(ObjectBase) && !typeof(IEnumerable).IsAssignableFrom(type)) continue;

                member = candidate;
                ModLog.Info("Selection: using " + MemberProbe.Describe(member)
                            + " to follow the selected unit");
                return;
            }

            ModLog.Info("Selection: no selection member found; the mod follows the unit whose menu "
                        + "was opened last instead");
        }
        catch (Exception error)
        {
            ModLog.Error("Could not look for the game's selection", error);
        }
    }

    private static bool IsGone(ObjectBase unit)
    {
        try { return unit.IsDestroyed; }
        catch (Exception) { return true; }
    }
}
