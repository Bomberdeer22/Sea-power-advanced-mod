using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SeaPower;
using UnityEngine;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// Flying a jammed missile into the sea.
///
/// This is what a sea-skimmer does when its seeker loses lock: it stops correcting, loses its height
/// reference, and the sea wins. The mod's part is to give it that push - and then to get out of the
/// way, because the game already knows what happens when a missile reaches the water. Every missile
/// that misses a ship ends in the sea and is removed by the game itself.
///
/// <b>Nothing here destroys a weapon, and nothing calls the warhead.</b> Two earlier builds did, and
/// both were wrong in ways the player saw:
///
/// <list type="bullet">
/// <item>calling <c>Detonate</c>/<c>Explode</c> to "put it in the sea" set the warhead off two miles
///   from the ship, once per tick;</item>
/// <item>doing <c>Destroy</c> on a live weapon removed an object the game's own
///   <c>GameMain.FixedUpdate</c> was still walking, which is exactly the shape of the
///   <c>NullReferenceException</c> that followed, and what was left on screen was a missile sitting at
///   sea level that the game never knew had gone.</item>
/// </list>
///
/// So the weapon is steered, not killed:
///
/// <list type="number">
/// <item>its lock is broken, so it cannot correct anything that follows;</item>
/// <item>its nose is pointed down and its height is walked down gradually - a few metres a second,
///   with the pitch following - from far enough out that it cannot reach the ship even if the
///   descent is slower than intended;</item>
/// <item>the game does the rest: a weapon at the waterline is the game's own sea impact, handled by
///   the game, which is why this build needs no removal code at all.</item>
/// </list>
///
/// Everything found on a weapon is written to <c>ecm-dive.txt</c>, including the case where the build
/// exposes no height at all and the transform is the only thing that can be moved.
/// </summary>
internal static class WeaponDive
{
    /// <summary>What was found on one weapon type, and which route actually moves it.</summary>
    private sealed class Shape
    {
        /// <summary>Members that hold what the weapon is homing on. Nulled to break the lock.</summary>
        internal List<MemberInfo> Homing = new();

        /// <summary>Its own height above the sea, if this build keeps one. The best route: this is
        /// the number the simulation's own sea impact is judged on, not the drawing.</summary>
        internal MemberInfo? Altitude;

        /// <summary>A vertical speed or pitch, for a build whose height is derived from it.</summary>
        internal MemberInfo? Vertical;

        /// <summary>The scene object, when the weapon has one.</summary>
        internal bool Transform;

        internal List<string> Found = new();

        /// <summary>Which route has actually moved this type down: 0 none yet, 1 height member,
        /// 2 vertical member, 3 transform.</summary>
        internal int Route;
    }

    /// <summary>One weapon on its way down.</summary>
    private sealed class Dive
    {
        internal float Started;
        internal float LastStep;
        internal float Height = float.NaN;

        /// <summary>True once the player has been told this weapon is being put down.</summary>
        internal bool Announced;

        /// <summary>True once it has been reported as reaching the surface.</summary>
        internal bool AtSurface;
    }

    private static readonly Dictionary<Type, Shape> Shapes = new();
    private static readonly Dictionary<object, Dive> Diving = new();

    /// <summary>How fast the nose is walked down, in metres per second. Gradual on purpose: a missile
    /// that drops forty metres in one frame reads as a bug, and the point is that it looks like a
    /// seeker losing its reference.</summary>
    private const float DescentMetresPerSecond = 12f;

    /// <summary>How fast the nose pitches down, in degrees per second, for the builds whose visible
    /// attitude is the transform.</summary>
    private const float PitchDegreesPerSecond = 6f;

    /// <summary>At or below this height the weapon is in the water as far as the mod can tell.</summary>
    private const float WaterlineMetres = 1f;

    private static bool reported;

    /// <summary>How many weapons have been steered towards the sea this session.</summary>
    internal static int Steered { get; private set; }

    /// <summary>How many of those the game has since removed - the sea impact doing its own work.</summary>
    internal static int TakenByTheSea { get; private set; }

    /// <summary>How many reached the surface and were still there afterwards.</summary>
    internal static int Stuck { get; private set; }

    // --- the two steps ---------------------------------------------------------------------------

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

        return wrote;
    }

    /// <summary>
    /// One step of the descent: point the weapon down and walk its height towards the sea. Returns
    /// true while it is still being brought down, false once it is at the surface (from then on the
    /// game owns it).
    ///
    /// <paramref name="note"/> is set the first time for the log line, and says which route moved it.
    /// </summary>
    internal static bool Steer(object? weapon, out string note, out bool atSurface)
    {
        note = "";
        atSurface = false;

        if (weapon == null) return false;

        // The game has already removed it: nothing to steer, and nothing to report as a failure.
        if (weapon is UnityEngine.Object gone && gone == null)
        {
            atSurface = true;
            return false;
        }

        Shape shape = ShapeOf(weapon);
        float now = Time.unscaledTime;

        if (!Diving.TryGetValue(weapon, out Dive? dive))
        {
            dive = new Dive { Started = now, LastStep = now };
            Diving[weapon] = dive;
            Steered++;
        }

        float delta = Mathf.Clamp(now - dive.LastStep, 0f, 0.25f);
        dive.LastStep = now;

        float step = delta * DescentMetresPerSecond;

        // 1. The weapon's own height. This is the route that matters: it is the number the
        //    simulation's sea impact is judged on, so writing it is asking the game to do what it
        //    would have done on a miss.
        float height = ReadHeight(weapon, shape);

        if (!float.IsNaN(height))
        {
            dive.Height = height;

            float target = Math.Max(0f, height - step);

            if (WriteHeight(weapon, shape, target))
            {
                if (shape.Route == 0) shape.Route = 1;
                note = "height " + height.ToString("0") + " m -> " + target.ToString("0") + " m";
            }
        }

        // 2. A vertical speed or pitch, for a build whose height is derived from one.
        if (shape.Vertical != null)
        {
            try
            {
                if (MemberProbe.Write(shape.Vertical, weapon, -DescentMetresPerSecond))
                {
                    if (shape.Route == 0) shape.Route = 2;
                    if (note.Length == 0) note = "nose down at " + (int)DescentMetresPerSecond + " m/s";
                }
            }
            catch (Exception) { }
        }

        // 3. Its transform: pitch the nose down and walk the model down with it, so the dive is
        //    visible whatever the simulation is doing.
        GameObject? scene = Scene(weapon);

        if (scene != null)
        {
            try
            {
                Transform model = scene.transform;

                model.Rotate(delta * PitchDegreesPerSecond, 0f, 0f, Space.Self);

                Vector3 position = model.position;

                if (position.y > 0f)
                {
                    float before = position.y;
                    position.y = Math.Max(0f, before - step);
                    model.position = position;

                    if (shape.Route == 0) shape.Route = 3;
                    if (note.Length == 0)
                        note = "height " + before.ToString("0") + " m -> "
                               + position.y.ToString("0") + " m";
                }

                dive.Height = position.y;

                if (position.y <= WaterlineMetres)
                {
                    atSurface = true;
                    return false;
                }
            }
            catch (Exception) { }
        }

        if (!float.IsNaN(dive.Height) && dive.Height <= WaterlineMetres)
        {
            atSurface = true;
            return false;
        }

        if (note.Length == 0 && float.IsNaN(dive.Height))
            note = "no height member and no transform - nothing the mod can steer";

        return true;
    }

    /// <summary>
    /// True the first time this weapon is seen to have finished - either the game removed it after
    /// the sea impact, or it has been sitting at the surface for long enough that it is clearly not
    /// going to be. Called a few times a second; it never touches the weapon.
    /// </summary>
    internal static bool Finished(object? weapon, out string note)
    {
        note = "";

        if (weapon == null) return false;
        if (!Diving.TryGetValue(weapon, out Dive? dive)) return false;

        // Gone from the world: the game's own sea impact did it.
        if (weapon is UnityEngine.Object asset && asset == null)
        {
            Diving.Remove(weapon);
            TakenByTheSea++;
            note = "taken by the sea - the game removed it at the surface";
            return true;
        }

        // At the surface and still here after a few seconds: this build does not remove a weapon
        // that reaches the water. That is the mod's limit, and it is reported rather than patched
        // over with a Destroy, which is what produced a NullReferenceException inside the game's own
        // update the last time it was tried.
        if (dive.AtSurface && Time.unscaledTime - dive.Started > 12f)
        {
            Diving.Remove(weapon);
            Stuck++;
            note = "reached the surface and this build did not remove it - it is on the water, and "
                   + "no Destroy is used because removing a live weapon out of the game's own update "
                   + "throws NullReferenceException in GameMain.FixedUpdate";
            return true;
        }

        return false;
    }

    /// <summary>Marks the moment a weapon was first seen at the surface, for <see cref="Finished"/>.</summary>
    internal static void AtSurface(object? weapon)
    {
        if (weapon != null && Diving.TryGetValue(weapon, out Dive? dive)) dive.AtSurface = true;
    }

    /// <summary>True once this weapon has been announced, so the log gets one line per missile.</summary>
    internal static bool Announce(object? weapon)
    {
        if (weapon == null || !Diving.TryGetValue(weapon, out Dive? dive)) return false;
        if (dive.Announced) return false;

        dive.Announced = true;
        return true;
    }

    /// <summary>One line for the menu: what the close-in steering has done.</summary>
    internal static string Describe()
    {
        if (Steered == 0) return "no missile has had to be put down";

        string line = Steered + " missile(s) pointed at the sea";

        if (TakenByTheSea > 0) line += ", " + TakenByTheSea + " taken by the game at the surface";
        if (Stuck > 0) line += ", " + Stuck + " left on the water (this build does not remove them)";

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

    /// <summary>
    /// The scene object behind a weapon: its GameObject when it is a component, itself when it is a
    /// GameObject, and nothing when it is neither. <c>UnityEngine.Object</c> is the base of both and
    /// has no <c>gameObject</c> member.
    /// </summary>
    private static GameObject? Scene(object? thing) => thing switch
    {
        Component component => component.gameObject,
        GameObject direct => direct,
        _ => null,
    };

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

        shape.Transform = Scene(weapon) != null;

        shape.Found.Add("type           " + type.Name);
        shape.Found.Add("homing member  " + Names(shape.Homing));
        shape.Found.Add("height member  " + (shape.Altitude?.Name ?? "<none found>"));
        shape.Found.Add("vertical       " + (shape.Vertical?.Name ?? "<none found>"));
        shape.Found.Add("transform      " + (shape.Transform ? "yes" : "no"));

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

    /// <summary>An altitude the mod can steer: a number whose name says height above the sea.</summary>
    private static bool IsHeight(Type type, string name)
    {
        if (type != typeof(float) && type != typeof(double) && type != typeof(int)) return false;

        string lower = name.ToLowerInvariant();

        return (lower.Contains("height") || lower.Contains("altitude") || lower == "_y")
               && !lower.Contains("target") && !lower.Contains("agl") && !lower.Contains("above");
    }

    /// <summary>A vertical speed or pitch member, for the builds whose height is derived from it.</summary>
    private static bool IsVertical(Type type, string name)
    {
        if (type != typeof(float) && type != typeof(double)) return false;

        string lower = name.ToLowerInvariant();

        return lower.Contains("verticalspeed") || lower.Contains("verticalvelocity")
               || lower.Contains("climbrate") || lower.Contains("climb")
               || lower.Contains("pitch");
    }

    private static string Names(List<MemberInfo> members)
    {
        if (members.Count == 0) return "<none found>";

        var names = new List<string>();
        foreach (MemberInfo member in members) names.Add(member.Name);

        return string.Join(", ", names);
    }

    // --- report ----------------------------------------------------------------------------------

    /// <summary>
    /// Writes <c>ecm-dive.txt</c>: what was read off each weapon type, which route moves it, and what
    /// the close-in steering has done. Appended, so a mission's whole record is in one file.
    /// </summary>
    internal static void WriteReport()
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine();
            text.AppendLine("Sea Power Advanced - missiles pointed at the sea");
            text.AppendLine("----------------------------------------------");
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
            text.AppendLine("  2. its height is walked down at 12 m/s and its nose pitched down at 6 deg/s,");
            text.AppendLine("     from well outside the range at which it could still reach the ship;");
            text.AppendLine("  3. at the waterline the mod stops touching it. The game's own sea impact is");
            text.AppendLine("     what removes it, exactly as it does for a missile that misses.");
            text.AppendLine();
            text.AppendLine("NOTHING HERE DESTROYS A WEAPON, and nothing here calls Detonate, Explode or");
            text.AppendLine("SelfDestruct. Two earlier builds did, and the player saw both results: a");
            text.AppendLine("warhead going off two miles off the bow once per tick, and - after that was");
            text.AppendLine("narrowed to a Destroy - a NullReferenceException inside GameMain.FixedUpdate");
            text.AppendLine("with the missile left sitting on the water, because the game was still walking");
            text.AppendLine("an object that had been removed from under it.");
            text.AppendLine();
            text.AppendLine("If the totals say 'left on the water', this build does not remove a weapon");
            text.AppendLine("that reaches the surface on its own. That is a fact about the build, not a");
            text.AppendLine("failure to hide: the mod will not destroy it to paper over it, because that is");
            text.AppendLine("what threw the exception.");

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
        2 => "a vertical speed / pitch member",
        3 => "its transform",
        _ => "not used yet",
    };
}
