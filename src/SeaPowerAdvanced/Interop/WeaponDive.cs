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

        /// <summary>A way to end the flight that does not set the warhead off.</summary>
        internal MethodInfo? Quiet;
        internal string QuietName = "";

        /// <summary>The warhead methods this build has. Found, reported, and never called: a missile
        /// that detonates two miles off the bow is not a missile that fell in the sea.</summary>
        internal List<string> Loud = new();
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
        internal float LastPush;
        internal float Height = float.NaN;
    }

    private static readonly Dictionary<Type, Shape> Shapes = new();
    private static readonly Dictionary<object, Dive> Diving = new();

    /// <summary>
    /// Weapons this feature is completely finished with: put down, or tried and given up on.
    ///
    /// Kept because the game's own inbound lists are not guaranteed to drop a weapon the moment it is
    /// removed - which, in the build that called the game's terminal method every tick, produced an
    /// explosion every tick. Membership here is the one thing that makes a weapon untouchable again.
    /// </summary>
    private static readonly HashSet<object> Settled = new();

    /// <summary>True when this weapon has already been dealt with and must be left alone.</summary>
    internal static bool Done(object? weapon) => weapon != null && Settled.Contains(weapon);

    /// <summary>
    /// Drops weapons the game has destroyed from the settled set, so a long mission with a lot of
    /// missiles does not keep every one of them alive in a list. Only dead ones are dropped: a
    /// weapon still in the world stays settled, which is what stops it being dealt with twice.
    /// </summary>
    private static void Prune()
    {
        if (Settled.Count < 256) return;

        var dead = new List<object>();

        foreach (object weapon in Settled)
        {
            if (weapon is UnityEngine.Object asset && asset == null) dead.Add(weapon);
        }

        foreach (object weapon in dead) Settled.Remove(weapon);
    }

    /// <summary>How long a weapon is given to reach the water before it is taken down anyway.</summary>
    private const float DiveSeconds = 6f;

    /// <summary>
    /// How fast the weapon is pushed down, in metres per second.
    ///
    /// This used to be a fixed number of metres per *tick*, which at the ticker's rate came to more
    /// than a kilometre a second: a sea-skimmer twenty metres up reached the waterline in a single
    /// tick, so there was no dive to see, only a splash. A rate against real time fixes that - a
    /// missile at 20 m takes about a second to go in, one at 300 m about a dozen.
    /// </summary>
    private const float DescentMetresPerSecond = 15f;

    /// <summary>The shortest dive worth watching. Even from a few metres up, it is given this long.</summary>
    private const float MinimumDiveSeconds = 1f;

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
    ///
    /// The descent is a rate against real time, not a fixed step per tick, so the missile is seen to
    /// go down rather than teleporting to the surface between two frames - and it is given a minimum
    /// time even from a few metres up, so the last second of its flight looks like a missile losing
    /// its height reference rather than a copy of it vanishing.
    /// </summary>
    internal static bool PushDown(object? weapon, out string note, out bool waterline)
    {
        note = "";
        waterline = false;

        if (weapon == null) return false;
        if (Settled.Contains(weapon)) { note = "already in the water"; waterline = true; return true; }

        Shape shape = ShapeOf(weapon);

        float now = Time.unscaledTime;

        if (!Diving.TryGetValue(weapon, out Dive? dive))
        {
            dive = new Dive { Started = now, LastPush = now };
            Diving[weapon] = dive;
        }

        float age = now - dive.Started;

        // How far down it goes this frame: a rate against real time, capped so a stalled frame cannot
        // put a missile in the sea between two of them.
        float delta = Mathf.Clamp(now - dive.LastPush, 0f, 0.25f);
        dive.LastPush = now;

        float step = delta * DescentMetresPerSecond;

        // Below the minimum dive time the weapon is held just above the surface, so the fall is
        // visible rather than a copy of the missile vanishing at two miles.
        bool settling = age < MinimumDiveSeconds;

        // 1. The weapon's own height, if this build exposes it.
        float height = ReadHeight(weapon, shape);

        if (!float.IsNaN(height))
        {
            dive.Height = height;

            float target = settling
                ? Math.Max(WaterlineMetres + 0.5f, height - step)
                : Math.Max(0f, height - step);

            if (WriteHeight(weapon, shape, target))
            {
                if (shape.Route == 0) shape.Route = 1;
                note = "height " + height.ToString("0") + " m -> " + target.ToString("0") + " m";

                if (!settling && target <= WaterlineMetres) { waterline = true; return true; }
            }
        }

        // 2. Its transform, which weapons do have on this build.
        GameObject? scene = Scene(weapon);

        if (scene != null)
        {
            try
            {
                Vector3 position = scene.transform.position;
                dive.Height = position.y;

                float floor = settling ? WaterlineMetres + 0.5f : 0f;
                float target = Math.Max(floor, position.y - step);

                if (target < position.y)
                {
                    float before = position.y;
                    position.y = target;
                    scene.transform.position = position;

                    if (shape.Route == 0) shape.Route = 2;
                    note = "height " + before.ToString("0") + " m -> " + target.ToString("0") + " m";
                }

                if (!settling && position.y <= WaterlineMetres)
                {
                    waterline = true;
                    return true;
                }
            }
            catch (Exception) { }
        }

        // 3. Out of time: it is close, it is not correcting, and the sea is the only place left for
        //    it. This is the backstop, and the report says when it was needed.
        if (age >= DiveSeconds)
        {
            waterline = true;
            note = note.Length > 0 ? note + "; out of time" : "out of time - nothing moved it down";
            return true;
        }

        if (note.Length == 0) note = "no altitude member and no transform to push down";
        return false;
    }

    /// <summary>
    /// The weapon reaches the water.
    ///
    /// THREE RULES, and the first one is the whole point of this method:
    ///
    /// <list type="number">
    /// <item><b>Once, ever.</b> A weapon is added to <see cref="Settled"/> before anything is done to
    ///   it, and <see cref="Done"/> answers true for it from then on whatever the outcome. The
    ///   earlier build called the game's terminal method and trusted it to remove the weapon; when it
    ///   did not, the next tick found it still inbound and set it off again - an explosion every
    ///   tick, forever, at two miles from the ship. Nothing here can repeat.</item>
    /// <item><b>Never the warhead.</b> <c>Detonate</c>, <c>Explode</c> and <c>SelfDestruct</c> are not
    ///   called at all. A missile that blows up beside the ship is not a missile that fell into the
    ///   sea - and it is a warhead going off next to the thing the feature exists to protect.</item>
    /// <item><b>At the waterline.</b> The last thing done before it goes is to put it at sea level,
    ///   so the frame it disappears from is the frame it is on the water.</item>
    /// </list>
    /// </summary>
    internal static bool Water(object? weapon, out string note)
    {
        note = "";

        if (weapon is null) return true;

        if (Settled.Contains(weapon))
        {
            note = "already in the water";
            return true;
        }

        // Before anything else, and whatever happens next: this weapon is finished with.
        Settled.Add(weapon);
        Prune();

        // Already gone: nothing to do, and nothing to report as a failure.
        if (weapon is UnityEngine.Object gone && gone == null) return true;

        Shape shape = ShapeOf(weapon);
        string altitude = Altitude(weapon);

        Diving.Remove(weapon);

        // Its last frame is on the surface.
        AtWaterline(weapon, shape);

        if (shape.Quiet != null)
        {
            try
            {
                shape.Quiet.Invoke(weapon, null);
                inTheWater++;
                note = "sank" + altitude + " (" + shape.QuietName + "())";
                return true;
            }
            catch (Exception error)
            {
                note = shape.QuietName + "() refused: " + error.GetType().Name;
            }
        }

        if (weapon is UnityEngine.Object asset)
        {
            try
            {
                // Destroy the scene object when there is one - a weapon is a component on a
                // GameObject - and the object itself otherwise. UnityEngine.Object has no gameObject
                // member; Component and GameObject do, which is what Scene() is for.
                GameObject? scene = Scene(weapon);
                UnityEngine.Object target = scene != null ? scene : asset;

                UnityEngine.Object.Destroy(target);

                inTheWater++;
                note = "sank" + altitude + " (removed at the waterline)";
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

    /// <summary>
    /// Puts the weapon exactly at sea level for the frame it disappears in: its height member if the
    /// build has one, its transform otherwise. Best effort - a weapon that will not go down here is
    /// removed anyway.
    /// </summary>
    private static void AtWaterline(object weapon, Shape shape)
    {
        try
        {
            WriteHeight(weapon, shape, 0f);

            GameObject? scene = Scene(weapon);
            if (scene == null) return;

            Vector3 position = scene.transform.position;
            if (position.y <= 0f) return;

            position.y = 0f;
            scene.transform.position = position;
        }
        catch (Exception) { }
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

            if (Quiet(method.Name))
            {
                shape.Quiet ??= method;
                shape.QuietName = method.Name;
                continue;
            }

            if (Loud(method.Name)) shape.Loud.Add(method.Name);
        }

        shape.Transform = weapon is Component || weapon is GameObject;

        shape.Found.Add("type           " + type.Name);
        shape.Found.Add("homing member  " + List(shape.Homing));
        shape.Found.Add("height member  " + (shape.Altitude?.Name ?? "<none found>"));
        shape.Found.Add("vertical       " + (shape.Vertical?.Name ?? "<none found>"));
        shape.Found.Add("transform      " + (shape.Transform ? "yes" : "no"));
        shape.Found.Add("quiet end      " + (shape.Quiet != null ? shape.QuietName + "()" : "<none found>"));
        shape.Found.Add("warhead (unused)" + " " + (shape.Loud.Count > 0
            ? string.Join(", ", shape.Loud) : "<none found>"));

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

    /// <summary>
    /// A way to end a weapon's flight that does not set its warhead off: it expires, is aborted, or
    /// is removed. These are the only "end it" methods the dive calls.
    /// </summary>
    private static bool Quiet(string name)
    {
        string lower = name.ToLowerInvariant();

        return lower.Contains("expire") || lower.Contains("endflight") || lower.Contains("terminate")
               || lower.Contains("abort") || lower.Contains("despawn") || lower.Contains("remove")
               || lower == "destroy" || lower == "kill";
    }

    /// <summary>
    /// The warhead going off. These are found and written to the report so the player can see what
    /// the build offers, and they are <b>never called</b>: a missile detonating two miles off the bow
    /// is not a missile that fell into the sea, it is an explosion next to the ship - and calling it
    /// every tick, which is what an earlier build did when the weapon was not actually removed, is
    /// an explosion forever.
    /// </summary>
    private static bool Loud(string name)
    {
        string lower = name.ToLowerInvariant();

        return lower.Contains("selfdestruct") || lower.Contains("detonate") || lower.Contains("explode")
               || lower.Contains("explosion") || lower.Contains("impact") || lower.Contains("hit");
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
            text.AppendLine("  2. the weapon's height is driven down at 15 m/s, through its own height");
            text.AppendLine("     member if the build has one, otherwise through its transform, with at");
            text.AppendLine("     least a second before it may reach the surface so the dive is seen;");
            text.AppendLine("  3. at the waterline, or after six seconds of descent, it is taken down -");
            text.AppendLine("     by a method that ends its flight without setting the warhead off, or");
            text.AppendLine("     by removing it at sea level if this build has no such method.");
            text.AppendLine();
            text.AppendLine("If the route says 'nothing moved it down', this build ignores both writes and");
            text.AppendLine("the weapon is simply removed at the end of the dive - it still never reaches");
            text.AppendLine("the ship, but it does not show a visible dive either. That line is the one to");
            text.AppendLine("send if the missile vanishes instead of falling.");
            text.AppendLine();
            text.AppendLine("A weapon is put down ONCE and then never touched again, whatever the outcome;");
            text.AppendLine("the game's inbound lists can lag a removed weapon by a frame or two. The warhead");
            text.AppendLine("methods listed above are never called - an earlier build did, and a missile");
            text.AppendLine("that was not actually removed detonated again on every tick, for ever, two");
            text.AppendLine("miles off the bow.");

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
