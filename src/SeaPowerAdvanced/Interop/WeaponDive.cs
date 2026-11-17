using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SeaPower;
using UnityEngine;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// Putting a jammed missile in the water.
///
/// This is what a sea-skimmer does when its seeker loses lock at two miles: it stops climbing and
/// correcting, and the sea wins. The mod's part is small and physical, and it is done in three
/// steps, in order, each one checked before the next is relied on:
///
/// <list type="number">
/// <item><b>Break the lock</b> - write null into the member the weapon is homing on. It keeps
///   flying, it stops knowing where the ship is, and it can no longer correct its dive.</item>
/// <item><b>Push it down</b> - drive its height towards the sea every tick: its own altitude or
///   vertical-speed members if this build has them, and its Unity transform if it has one (weapons
///   are scene objects on this build - the multiplayer mod rotates their transforms).</item>
/// <item><b>Waterline</b> - when it is at or below sea level, or when the descent has run out of
///   time, the weapon is taken off the board. That last step is the backstop: if this build's
///   guidance ignores what is written to it, the missile still ends up in the sea rather than on the
///   ship, and <c>ecm-dive.txt</c> says which route did the work.</item>
/// </list>
///
/// Nothing here claims the missile "misses": it is put down. Every weapon touched is written to
/// <c>ecm-dive.txt</c> with the member that took the descent command, so a build where none of that
/// resolves can be told apart from one where it works.
/// </summary>
internal static class WeaponDive
{
    /// <summary>What was found on one weapon type, and how the descent actually worked.</summary>
    private sealed class Shape
    {
        internal List<MemberInfo> Homing = new();
        internal MemberInfo? Altitude;
        internal MemberInfo? Vertical;
        internal MethodInfo? Terminal;
        internal string TerminalName = "";
        internal bool Transform;
        internal List<string> Found = new();

        /// <summary>Which route has actually moved this type down: 0 unknown, 1 height members,
        /// 2 transform, 3 the waterline backstop.</summary>
        internal int Route;
    }

    /// <summary>One weapon being put down: when it started, and the last height read off it.</summary>
    private sealed class Dive
    {
        internal float Started;
        internal float Height = float.NaN;
    }

    private static readonly Dictionary<Type, Shape> Shapes = new();
    private static readonly Dictionary<object, Dive> Diving = new();

    /// <summary>How long a weapon is given to reach the water before it is taken down anyway.</summary>
    private const float DiveSeconds = 3f;

    /// <summary>Metres of height removed per tick while pushing a weapon down.</summary>
    private const float MetresPerTick = 60f;

    /// <summary>At or below this height the weapon is in the water.</summary>
    private const float WaterlineMetres = 1.5f;

    private static int inTheWater;
    private static int forced;
    private static int unreachable;
    private static bool reported;

    /// <summary>How many missiles have been put in the sea this session.</summary>
    internal static int Sunk => inTheWater;

    /// <summary>How many needed the waterline backstop because nothing else moved them.</summary>
    internal static int Forced => forced;

    // --- the three steps -------------------------------------------------------------------------

    /// <summary>Breaks the weapon's lock. True when a homing member was actually written.</summary>
    internal static bool BreakLock(object? weapon, out string note)
    {
        note = "";
        if (weapon == null) return false;

        Shape shape = ShapeOf(weapon);
        bool wrote = false;

        foreach (MemberInfo member in shape.Homing)
        {
            try { if (MemberProbe.Write(member, weapon, null)) wrote = true; }
            catch (Exception) { }
        }

        note = wrote ? "lock broken"
                     : shape.Homing.Count == 0
                         ? "no homing member found"
                         : "the homing member would not take null";

        if (!wrote) unreachable++;
        return wrote;
    }

    /// <summary>
    /// One tick of descent for a weapon that is inside the dive range: push its height down and tell
    /// the caller whether it has reached the water (or run out of time).
    /// </summary>
    internal static bool PushDown(object? weapon, out string note, out bool waterline)
    {
        note = "";
        waterline = false;

        if (weapon == null) return false;

        Shape shape = ShapeOf(weapon);

        if (!Diving.TryGetValue(weapon, out Dive? dive))
        {
            dive = new Dive { Started = Time.unscaledTime };
            Diving[weapon] = dive;
        }

        float now = Time.unscaledTime;

        // 1. The weapon's own height, if this build exposes it.
        float height = ReadHeight(weapon, shape);

        if (!float.IsNaN(height))
        {
            dive.Height = height;

            float target = Math.Max(0f, height - MetresPerTick);

            if (WriteHeight(weapon, shape, target))
            {
                if (shape.Route == 0) shape.Route = 1;
                note = "height " + height.ToString("0") + " m -> " + target.ToString("0") + " m";

                if (target <= WaterlineMetres) { waterline = true; return true; }
            }
        }

        // 2. Its transform, which weapons do have on this build.
        if (weapon is UnityEngine.Object asset && asset != null)
        {
            try
            {
                GameObject? scene = Scene(weapon);

                if (scene != null)
                {
                    Vector3 position = scene.transform.position;
                    dive.Height = position.y;

                    if (position.y > 0f)
                    {
                        position.y = Math.Max(0f, position.y - MetresPerTick);
                        scene.transform.position = position;

                        if (shape.Route == 0) shape.Route = 2;
                        note = "transform height " + dive.Height.ToString("0") + " m -> "
                               + position.y.ToString("0") + " m";

                        if (position.y <= WaterlineMetres) { waterline = true; return true; }
                    }
                    else
                    {
                        waterline = true;
                        return true;
                    }
                }
            }
            catch (Exception) { }
        }

        // 3. Out of time: it is close, it is not correcting, and the sea is the only place left for
        //    it. This is the backstop, and the report says when it was needed.
        if (now - dive.Started >= DiveSeconds)
        {
            waterline = true;
            note = note.Length > 0 ? note + "; out of time" : "out of time - nothing moved it down";
            return true;
        }

        if (note.Length == 0) note = "no altitude member and no transform to push down";
        return false;
    }

    /// <summary>
    /// The weapon reaches the water: the game's own terminal method if it has one, otherwise the
    /// object is destroyed. Either way it is gone before it can touch the ship.
    /// </summary>
    internal static bool Water(object? weapon, out string note)
    {
        note = "";

        if (weapon is null) return true;

        // Already gone: nothing to do, and nothing to report as a failure.
        if (weapon is UnityEngine.Object gone && gone == null) return true;

        Shape shape = ShapeOf(weapon);

        if (shape.Terminal != null)
        {
            try
            {
                shape.Terminal.Invoke(weapon, null);
                inTheWater++;
                note = "in the water" + Altitude(weapon) + " (" + shape.TerminalName + "())";
                Diving.Remove(weapon!);
                return true;
            }
            catch (Exception error)
            {
                note = shape.TerminalName + "() refused: " + error.GetType().Name;
            }
        }

        if (weapon is UnityEngine.Object asset)
        {
            try
            {
                // Destroy the scene object when there is one - a weapon component lives on a
                // GameObject - and the object itself otherwise. UnityEngine.Object has no
                // gameObject member; Component and GameObject do, which is what Scene() is for.
                GameObject? scene = Scene(weapon);
                UnityEngine.Object target = scene != null ? scene : asset;

                UnityEngine.Object.Destroy(target);

                inTheWater++;
                note = "in the water" + Altitude(weapon)
                       + " (destroyed - no terminal method on this build)";
                Diving.Remove(weapon);
                return true;
            }
            catch (Exception error)
            {
                note = "could not remove it: " + error.GetType().Name;
            }
        }

        if (shape.Route == 0) shape.Route = 3;
        forced++;
        note = note.Length > 0 ? note : "no way to take this weapon down";
        return false;
    }

    /// <summary>Where the weapon had got down to when it went in, for the log line.</summary>
    private static string Altitude(object weapon)
    {
        if (!Diving.TryGetValue(weapon, out Dive? dive)) return "";
        if (float.IsNaN(dive.Height)) return "";

        return " from " + dive.Height.ToString("0") + " m";
    }

    /// <summary>
    /// The scene object behind a weapon: its GameObject when it is a component, itself when it is a
    /// GameObject, and nothing when it is neither. <c>UnityEngine.Object</c> is the base of both and
    /// has no <c>gameObject</c> member, which is why this is asked as a question rather than assumed.
    /// </summary>
    private static GameObject? Scene(object? thing) => thing switch
    {
        Component component => component.gameObject,
        GameObject direct => direct,
        _ => null,
    };

    /// <summary>Forgets a weapon that has gone, so the dive table does not grow all mission.</summary>
    internal static void Forget(object? weapon)
    {
        if (weapon != null) Diving.Remove(weapon);
    }

    /// <summary>One line for the menu: what the close-in dive has done.</summary>
    internal static string Describe()
    {
        if (inTheWater == 0 && forced == 0) return "nothing has had to go in the water";

        string line = inTheWater + " missile(s) put in the water";
        if (forced > 0) line += ", " + forced + " with nothing to move them";

        return line;
    }

    // --- reading and writing the height ----------------------------------------------------------

    private static float ReadHeight(object weapon, Shape shape)
    {
        try
        {
            if (shape.Altitude != null)
            {
                object? raw = MemberProbe.Read(shape.Altitude, weapon);
                if (raw != null) return Convert.ToSingle(raw);
            }
        }
        catch (Exception) { }

        return float.NaN;
    }

    private static bool WriteHeight(object weapon, Shape shape, float metres)
    {
        if (shape.Altitude == null) return false;

        try { return MemberProbe.Write(shape.Altitude, weapon, metres); }
        catch (Exception) { return false; }
    }

    // --- what a weapon looks like ----------------------------------------------------------------

    private static Shape ShapeOf(object weapon)
    {
        Type type = weapon.GetType();
        if (Shapes.TryGetValue(type, out Shape? cached)) return cached;

        var shape = new Shape();
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (FieldInfo field in type.GetFields(all))
        {
            if (IsHoming(field.FieldType, field.Name)) { shape.Homing.Add(field); continue; }

            if (IsHeight(field.FieldType, field.Name)) shape.Altitude ??= field;
            if (IsVertical(field.FieldType, field.Name)) shape.Vertical ??= field;
        }

        foreach (PropertyInfo property in type.GetProperties(all))
        {
            if (!property.CanRead) continue;

            if (property.CanWrite && IsHoming(property.PropertyType, property.Name))
            {
                shape.Homing.Add(property);
                continue;
            }

            if (property.CanWrite && IsHeight(property.PropertyType, property.Name))
                shape.Altitude ??= property;

            if (property.CanWrite && IsVertical(property.PropertyType, property.Name))
                shape.Vertical ??= property;
        }

        foreach (MethodInfo method in type.GetMethods(all))
        {
            if (method.IsStatic || method.GetParameters().Length != 0) continue;
            if (!Ends(method.Name)) continue;

            shape.Terminal = method;
            shape.TerminalName = method.Name;
            break;
        }

        shape.Transform = weapon is Component || weapon is GameObject;

        shape.Found.Add("type           " + type.Name);
        shape.Found.Add("homing member  " + List(shape.Homing));
        shape.Found.Add("height member  " + (shape.Altitude?.Name ?? "<none found>"));
        shape.Found.Add("vertical       " + (shape.Vertical?.Name ?? "<none found>"));
        shape.Found.Add("transform      " + (shape.Transform ? "yes" : "no"));
        shape.Found.Add("terminal       " + (shape.Terminal != null ? shape.TerminalName + "()" : "<none found>"));

        Shapes[type] = shape;

        if (!reported)
        {
            reported = true;
            WriteReport();
        }

        return shape;
    }

    /// <summary>
    /// The thing the weapon is homing on. Shape first - it has to hold null - then the name, so a
    /// member called "targets" (a list) is left alone: blanking it would take the whole weapon's
    /// bookkeeping with it.
    /// </summary>
    private static bool IsHoming(Type type, string name)
    {
        if (type.IsValueType || type == typeof(string)) return false;
        if (typeof(IEnumerable).IsAssignableFrom(type)) return false;

        string lower = name.ToLowerInvariant();

        bool named = lower.Contains("target") || lower.Contains("seek") || lower.Contains("victim")
                     || lower.Contains("homing") || lower.Contains("aimpoint");

        if (!named) return false;

        return !lower.Contains("count") && !lower.Contains("time") && !lower.Contains("range");
    }

    /// <summary>An altitude the mod can write: a number whose name says height above the sea.</summary>
    private static bool IsHeight(Type type, string name)
    {
        if (type != typeof(float) && type != typeof(double) && type != typeof(int)) return false;

        string lower = name.ToLowerInvariant();

        return (lower.Contains("height") || lower.Contains("altitude") || lower == "_y")
               && !lower.Contains("target") && !lower.Contains("agl");
    }

    /// <summary>A vertical-speed or pitch member, for the builds whose height is derived from it.</summary>
    private static bool IsVertical(Type type, string name)
    {
        if (type != typeof(float) && type != typeof(double)) return false;

        string lower = name.ToLowerInvariant();

        return lower.Contains("climb") || lower.Contains("vertical") || lower.Contains("verticalvelocity")
               || lower.Contains("vy") || lower.Contains("pitch");
    }

    private static bool Ends(string name)
    {
        string lower = name.ToLowerInvariant();

        return lower.Contains("selfdestruct") || lower.Contains("detonate") || lower.Contains("explode")
               || lower.Contains("expire") || lower.Contains("endflight") || lower.Contains("terminate")
               || lower.Contains("abort") || lower == "kill" || lower == "destroy";
    }

    private static string List(List<MemberInfo> members)
    {
        if (members.Count == 0) return "<none found>";

        var names = new List<string>();
        foreach (MemberInfo member in members) names.Add(member.Name);

        return string.Join(", ", names);
    }

    // --- report ----------------------------------------------------------------------------------

    /// <summary>
    /// Writes <c>ecm-dive.txt</c>: what was read off each weapon type, which route moved it down, and
    /// what the close-in dive has done. Appended, so a mission's whole record is in one file.
    /// </summary>
    internal static void WriteReport()
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine();
            text.AppendLine("Sea Power Advanced - missiles put in the water");
            text.AppendLine("--------------------------------------------");
            text.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            + "   mod " + ModInfo.Version);
            text.AppendLine();

            if (Shapes.Count == 0)
            {
                text.AppendLine("No guided weapon has been seen yet, so nothing has been read off one.");
            }
            else
            {
                foreach (KeyValuePair<Type, Shape> entry in Shapes)
                {
                    text.AppendLine("weapon type " + entry.Key.Name);
                    foreach (string line in entry.Value.Found) text.AppendLine("  " + line);
                    text.AppendLine("  route used    " + RouteName(entry.Value.Route));
                    text.AppendLine();
                }
            }

            text.AppendLine("Totals this session: " + Describe());
            text.AppendLine();
            text.AppendLine("What happens, in order");
            text.AppendLine("----------------------");
            text.AppendLine("  1. the lock is broken - the member the weapon is homing on is set to null;");
            text.AppendLine("  2. the weapon's height is driven down every tick, through its own height");
            text.AppendLine("     member if the build has one, otherwise through its transform;");
            text.AppendLine("  3. at the waterline, or after three seconds of descent, it is taken down.");
            text.AppendLine();
            text.AppendLine("If the route says 'nothing moved it down', this build ignores both writes and");
            text.AppendLine("the weapon is simply removed at the end of the dive - it still never reaches");
            text.AppendLine("the ship, but it does not show a visible dive either. That line is the one to");
            text.AppendLine("send if the missile vanishes instead of falling.");

            string path = ModFolders.AppendText("ecm-dive.txt", text.ToString());
            ModLog.Info("Missile dive report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Missile dive: could not write ecm-dive.txt", error);
        }
    }

    private static string RouteName(int route) => route switch
    {
        1 => "the weapon's own height member",
        2 => "its transform",
        3 => "nothing moved it down - removed at the end of the dive",
        _ => "not used yet",
    };
}
