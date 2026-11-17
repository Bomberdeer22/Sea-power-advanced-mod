using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SeaPower;
using UnityEngine;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// A guided weapon, reached from the outside.
///
/// The mod has never named a game type in this area on purpose - <c>Missile</c>, <c>WeaponBase</c>
/// and their members are not names that can be checked from here - so everything in this file is
/// found by reflection at runtime and everything found is written to <c>missile-shield.txt</c>.
///
/// Two things are done to a weapon, and they are different in kind:
///
/// <list type="number">
/// <item><b>Break</b> - write null into the member that holds what it is homing on. The weapon keeps
///   flying; it stops knowing where to fly to. This is a state the game has to handle anyway (a
///   missile whose target is sunk mid-flight has exactly this), so it is the safe half.</item>
/// <item><b>Finish</b> - take the weapon off the board, through the game's own terminal method if
///   the build has one (<c>SelfDestruct</c>, <c>Detonate</c>, <c>Expire</c>, <c>Abort</c>...), and
///   only if there is none, by destroying the object outright. This is the half that makes the
///   promise absolute, and it is the reason the feature is opt-in and labelled as a cheat.</item>
/// </list>
///
/// Nothing here is allowed to throw into the caller: a weapon this build does not expose is a
/// reported miss, not a crash.
/// </summary>
internal static class WeaponGuard
{
    /// <summary>What was found on one weapon type, cached.</summary>
    private sealed class Shape
    {
        /// <summary>Members that hold the thing being homed on. Nulled to break the lock.</summary>
        internal List<MemberInfo> Seekers = new();

        /// <summary>The game's own way of ending a weapon's flight, if it has one.</summary>
        internal MethodInfo? Terminal;

        internal string TerminalName = "";

        /// <summary>Everything the search looked at, for the report.</summary>
        internal List<string> Found = new();
    }

    private static readonly Dictionary<Type, Shape> Shapes = new();

    private static int broken;
    private static int finished;
    private static int unreachable;

    private static readonly List<string> Types = new();
    private static bool reported;

    /// <summary>How many weapons have had their lock broken this session.</summary>
    internal static int Broken => broken;

    /// <summary>How many weapons have been taken off the board this session.</summary>
    internal static int Finished => finished;

    // --- the two actions -------------------------------------------------------------------------

    /// <summary>
    /// Breaks the weapon's lock: every member that holds what it is homing on is written to null.
    /// Returns true when at least one of them was writable, so the caller can say whether anything
    /// actually happened.
    /// </summary>
    internal static bool Break(object? weapon, out string note)
    {
        note = "";
        if (weapon == null) return false;

        Shape shape = ShapeOf(weapon);
        bool wrote = false;

        foreach (MemberInfo member in shape.Seekers)
        {
            try
            {
                if (MemberProbe.Write(member, weapon, null)) wrote = true;
            }
            catch (Exception) { }
        }

        if (!wrote)
        {
            note = shape.Seekers.Count == 0
                ? "no homing member found on this weapon (see missile-shield.txt)"
                : "the homing member would not take null";
            unreachable++;
            return false;
        }

        broken++;
        note = "lost its lock";
        return true;
    }

    /// <summary>
    /// Ends the weapon's flight. The game's own terminal method first, because that is the route its
    /// own bookkeeping follows; a plain destroy only when there is no such method.
    /// </summary>
    internal static bool Finish(object? weapon, out string note)
    {
        note = "";
        if (weapon == null) return false;

        // Already gone: nothing to do, and nothing to report as a failure.
        if (weapon is UnityEngine.Object gone && gone == null) return true;

        Shape shape = ShapeOf(weapon);

        if (shape.Terminal != null)
        {
            try
            {
                shape.Terminal.Invoke(weapon, null);
                finished++;
                note = "taken down by " + shape.TerminalName + "()";
                return true;
            }
            catch (Exception error)
            {
                note = shape.TerminalName + "() refused: " + error.GetType().Name;
                ModLog.Warn("Missile shield: " + note + " - trying the destroy route");
            }
        }

        // The blunt route. It is the only thing left that keeps the promise, and the file says so.
        if (weapon is UnityEngine.Object instance)
        {
            try
            {
                UnityEngine.Object.Destroy(instance.gameObject);
                finished++;
                note = "taken down by Destroy (no terminal method on this build)";
                return true;
            }
            catch (Exception error)
            {
                note = "Destroy failed: " + error.GetType().Name;
            }
        }

        unreachable++;
        note = note.Length > 0 ? note : "no way to take this weapon down on this build";
        return false;
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
            if (!IsHoming(field.FieldType, field.Name)) continue;
            shape.Seekers.Add(field);
        }

        foreach (PropertyInfo property in type.GetProperties(all))
        {
            if (!property.CanRead || !property.CanWrite) continue;
            if (!IsHoming(property.PropertyType, property.Name)) continue;
            shape.Seekers.Add(property);
        }

        foreach (MethodInfo method in type.GetMethods(all))
        {
            if (method.IsStatic || method.GetParameters().Length != 0) continue;
            if (!Ends(method.Name)) continue;

            shape.Terminal = method;
            shape.TerminalName = method.Name;
            break;
        }

        shape.Found.Add("type          " + type.Name);
        shape.Found.Add("homing member " + (shape.Seekers.Count > 0
            ? string.Join(", ", Names(shape.Seekers)) : "<none found>"));
        shape.Found.Add("terminal      " + (shape.Terminal != null ? shape.TerminalName + "()" : "<none found>"));

        Shapes[type] = shape;

        if (!Types.Contains(type.Name)) Types.Add(type.Name);

        // Written the first time a weapon is actually seen, because the member names can only be
        // read off a live instance.
        if (!reported)
        {
            reported = true;
            WriteReport();
        }

        return shape;
    }

    /// <summary>
    /// Whether a member could be the thing a weapon is homing on.
    ///
    /// Shape first - it has to be able to hold null - then the name, because the alternative is
    /// nulling something unrelated. Lists are excluded: a member called "targets" is a collection,
    /// and blanking it would take the whole weapon's bookkeeping with it.
    /// </summary>
    private static bool IsHoming(Type type, string name)
    {
        if (type.IsValueType) return false;
        if (type == typeof(string)) return false;
        if (typeof(IEnumerable).IsAssignableFrom(type)) return false;

        string lower = name.ToLowerInvariant();

        // A lock, a target, a victim: the thing the seeker is flying at.
        bool named = lower.Contains("target") || lower.Contains("seek") || lower.Contains("victim")
                     || lower.Contains("homing") || lower.Contains("aimpoint");

        if (!named) return false;

        // Counters, timestamps and ranges are not objects; they are caught by the type test above,
        // but a member like "_targetCount" on a reference type would not be.
        return !lower.Contains("count") && !lower.Contains("time") && !lower.Contains("range");
    }

    /// <summary>The names of the game's own ways of ending a weapon's flight.</summary>
    private static bool Ends(string name)
    {
        string lower = name.ToLowerInvariant();

        return lower.Contains("selfdestruct")
               || lower.Contains("self-destruct")
               || lower.Contains("detonate")
               || lower.Contains("explode")
               || lower.Contains("expire")
               || lower.Contains("endflight")
               || lower.Contains("terminate")
               || lower.Contains("abort")
               || lower == "kill"
               || lower == "destroy"
               || lower == "cease";
    }

    private static IEnumerable<string> Names(List<MemberInfo> members)
    {
        foreach (MemberInfo member in members) yield return member.Name;
    }

    /// <summary>One line for the menu: what the shield has done this session.</summary>
    internal static string Describe()
    {
        if (broken == 0 && finished == 0) return "nothing has had to be done";

        return broken + " lock(s) broken, " + finished + " weapon(s) taken down";
    }

    /// <summary>
    /// Writes what was found to <c>missile-shield.txt</c>, appended rather than overwritten so a
    /// mission's whole record is in one file.
    /// </summary>
    internal static void WriteReport()
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine();
            text.AppendLine("Sea Power Advanced - missile shield");
            text.AppendLine("---------------------------------");
            text.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            + "   mod " + ModInfo.Version);
            text.AppendLine();

            if (Shapes.Count == 0)
            {
                text.AppendLine("No guided weapon has been seen yet, so nothing has been read off one.");
                text.AppendLine("Open a mission, let a missile be fired, and this block will gain the");
                text.AppendLine("members found on that weapon's type.");
            }
            else
            {
                foreach (KeyValuePair<Type, Shape> entry in Shapes)
                {
                    text.AppendLine("weapon type " + entry.Key.Name);
                    foreach (string line in entry.Value.Found) text.AppendLine("  " + line);
                    text.AppendLine();
                }
            }

            text.AppendLine("What the shield does, in order");
            text.AppendLine("------------------------------");
            text.AppendLine("  1. every tick, null the member the weapon is homing on - it keeps");
            text.AppendLine("     flying, it stops knowing where to;");
            text.AppendLine("  2. when the weapon is on the game's own dangerous list, or inside the");
            text.AppendLine("     close range, end its flight - the game's terminal method if the");
            text.AppendLine("     build has one, otherwise Destroy.");
            text.AppendLine();
            text.AppendLine("A weapon with no readable homing member and no terminal method cannot");
            text.AppendLine("be stopped, and the shield says so rather than claiming a hit would");
            text.AppendLine("have missed. This is the one deliberately unrealistic feature in the");
            text.AppendLine("mod: it is off until you switch it on.");

            string path = ModFolders.AppendText("missile-shield.txt", text.ToString());
            ModLog.Info("Missile shield report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Missile shield: could not write missile-shield.txt", error);
        }
    }
}
