using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;
using UnityEngine;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// The game's own list of live units.
///
/// Everything that acts on "the fleet" needs this, and the game gives it up only through private
/// members: <c>GameMain._objects</c> is a private <c>List&lt;ObjectBase&gt;</c>. Reading it through
/// reflection fails soft - an empty list is reported, and the panel says the fleet is empty rather
/// than pretending it worked.
/// </summary>
internal static class GameWorld
{
    private static MemberInfo? objectsField;
    private static bool looked;

    /// <summary>Every live, non-destroyed unit belonging to the player.</summary>
    internal static List<ObjectBase> PlayerUnits()
    {
        var units = new List<ObjectBase>();
        foreach (ObjectBase unit in AllUnits())
        {
            try
            {
                if (!unit.IsPlayerUnit) continue;
                if (unit.IsDestroyed) continue;
                units.Add(unit);
            }
            catch (Exception) { }
        }
        return units;
    }

    /// <summary>Every unit the game is ticking, player or not.</summary>
    internal static List<ObjectBase> AllUnits()
    {
        var units = new List<ObjectBase>();
        object? main = FindGameMain();
        if (main == null) return units;

        if (!looked)
        {
            looked = true;
            objectsField = MemberProbe.Value(main.GetType(), "_objects");
        }

        if (MemberProbe.Read(objectsField, main) is not IEnumerable list) return units;

        foreach (object? item in list)
        {
            if (item is ObjectBase unit) units.Add(unit);
        }
        return units;
    }

    /// <summary>
    /// The running <c>GameMain</c>. Found by type rather than by a singleton, because the dump does
    /// not show an Instance property and guessing one would be the same mistake the offline scan
    /// made with names.
    /// </summary>
    private static object? FindGameMain()
    {
        try
        {
            Type type = typeof(SeaPower.GameMain);
            UnityEngine.Object? found = UnityEngine.Object.FindObjectOfType(type);
            if (found != null) return found;

            // Not a scene component, or not up yet: fall back to every loaded object of that type.
            UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(type);
            return all.Length > 0 ? all[0] : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Player surface units and submarines - what a formation order should reach.</summary>
    internal static List<ObjectBase> PlayerShips()
    {
        var ships = new List<ObjectBase>();
        foreach (ObjectBase unit in PlayerUnits())
        {
            try { if (unit.IsSurfaceUnit || unit.IsSubUnit) ships.Add(unit); }
            catch (Exception) { }
        }
        return ships;
    }
}
