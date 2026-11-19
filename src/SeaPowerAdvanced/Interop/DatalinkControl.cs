using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// The two things a datalink needs from the game, reached by reflection so that neither is named in
/// code: the game's own <b>intel-sharing call</b>, and the ability to read <b>any</b> task force's
/// plotting table rather than only the player's.
///
/// The sharing call is <c>Utils.RevealContactToObject(detector, contact, source, classification)</c>.
/// It is what a recon report or a radio link uses to hand one unit's picture to another: it builds a
/// track and feeds it to the plotting table of the force the <i>detector</i> belongs to. That last
/// part is the whole feature - point it at a unit of the other player's force and that force's table
/// gains the track, on the machine that makes the call.
///
/// The classification argument is passed as <b>0</b> and never as 1, 2 or 3. Those three are the
/// identifying cases: the multiplayer mod's own working code documents that 2 attaches a side, and 3
/// attaches a side and a class, and that 1 - which reads like the mildest - ends up attaching both
/// because it routes to the identify path with <c>wasIdentified</c> hardcoded true. Falling off the
/// switch attaches neither, which is exactly the honest payload of a datalink track: it exists, it is
/// there, and whose it is remains the receiving side's problem.
/// </summary>
internal static class DatalinkControl
{
    private static bool looked;
    private static MethodInfo? reveal;
    private static string revealNote = "not looked up yet";

    private static readonly Dictionary<Type, MemberInfo?> TableMembers = new();
    private static readonly Dictionary<Type, MemberInfo?> UnitTableMembers = new();
    private static readonly Dictionary<Type, MemberInfo?> VehicleUnit = new();
    private static readonly Dictionary<Type, MemberInfo?> VehicleSensors = new();

    /// <summary>Where the sharing call was found, for the report.</summary>
    internal static string RevealSource => reveal == null ? revealNote : "Utils." + reveal.Name;

    /// <summary>True when this build exposes the call the whole feature rests on.</summary>
    internal static bool CanReveal
    {
        get
        {
            Find();
            return reveal != null;
        }
    }

    // --- handing a track to another force -------------------------------------------------------

    /// <summary>
    /// Reveals <paramref name="contact"/> to the force that <paramref name="detector"/> belongs to.
    ///
    /// Nothing is created and nothing is destroyed: the game builds the track itself, from a detector
    /// that is really there, exactly as it would for a recon report. The mod supplies only the pairing.
    /// </summary>
    internal static bool Reveal(object? detector, ObjectBase? contact, out string note)
    {
        Find();

        if (reveal == null)
        {
            note = revealNote;
            return false;
        }

        if (detector == null || contact == null)
        {
            note = "no detector or no contact";
            return false;
        }

        try
        {
            ParameterInfo[] parameters = reveal.GetParameters();
            var arguments = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;

                // The two objects: the detector first, the contact second. Matched by position
                // because both are the same type and the parameter names are not a contract.
                if (type == typeof(ObjectBase))
                {
                    arguments[i] = i == 0 ? detector : contact;
                    continue;
                }

                if (type == typeof(string))
                {
                    arguments[i] = "None";
                    continue;
                }

                if (type.IsEnum)
                {
                    arguments[i] = Enum.ToObject(type, 0);
                    continue;
                }

                arguments[i] = type.IsValueType ? Activator.CreateInstance(type) : null;
            }

            reveal.Invoke(null, arguments);
            note = "revealed through " + RevealSource;
            return true;
        }
        catch (Exception error)
        {
            Exception? inner = error.InnerException ?? error;
            note = "the sharing call refused it (" + inner.GetType().Name + ": " + inner.Message + ")";
            ModLog.Warn("Datalink: " + note);
            return false;
        }
    }

    private static void Find()
    {
        if (looked) return;
        looked = true;

        try
        {
            Assembly assembly = typeof(ObjectBase).Assembly;
            Type? utils = assembly.GetType("SeaPower.Utils", false);

            if (utils == null)
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.Name == "Utils") { utils = type; break; }
                }
            }

            if (utils == null)
            {
                revealNote = "this build has no SeaPower.Utils to call";
                return;
            }

            // By arity, then by shape: the signature is (ObjectBase, ObjectBase, string, <int-ish>)
            // in the shipped build, but the last argument is an enum in some versions and an int in
            // others, so the candidates are checked rather than assumed.
            foreach (MemberInfo member in MemberProbe.All(utils))
            {
                if (member is not MethodInfo method) continue;
                if (!method.Name.StartsWith("RevealContact", StringComparison.OrdinalIgnoreCase)) continue;
                if (!method.IsStatic) continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 2) continue;
                if (parameters[0].ParameterType != typeof(ObjectBase)) continue;
                if (parameters[1].ParameterType != typeof(ObjectBase)) continue;

                reveal = method;
                revealNote = "found";
                return;
            }

            revealNote = "this build has no RevealContact... method on SeaPower.Utils";
        }
        catch (Exception error)
        {
            revealNote = "the lookup failed (" + error.GetType().Name + ")";
        }
    }

    // --- reading any force's picture ------------------------------------------------------------

    /// <summary>The plotting table of one task force, or null when this build keeps it elsewhere.</summary>
    internal static object? TableOf(object? taskforce)
    {
        if (taskforce == null) return null;

        try
        {
            Type type = taskforce.GetType();

            if (!TableMembers.TryGetValue(type, out MemberInfo? member))
            {
                member = MemberProbe.Value(type, "PlottingTable", "plottingTable");
                TableMembers[type] = member;
            }

            return MemberProbe.Read(member, taskforce);
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Whether this unit has a plotting table of its own - which is what makes it able to carry a
    /// report. The multiplayer mod's own reveal code builds its detector the same way.
    /// </summary>
    internal static bool HasTable(ObjectBase? unit)
    {
        if (unit == null) return false;

        try
        {
            Type type = unit.GetType();

            if (!UnitTableMembers.TryGetValue(type, out MemberInfo? member))
            {
                member = MemberProbe.Value(type, "PlottingTable", "plottingTable");
                UnitTableMembers[type] = member;
            }

            return MemberProbe.Read(member, unit) != null;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// One force's picture, read once: the tracks the table holds and - for each - whether this
    /// machine's own sensors are on it or whether the table is only holding it because something put
    /// it there. Reading it once matters: a sweep asks about every contact in our own picture, and
    /// re-reading the table for each one would walk the whole table fifty times a sweep.
    /// </summary>
    internal sealed class Snapshot
    {
        internal object? Table;

        /// <summary>Every unit the table holds a track for, in the table's own order.</summary>
        internal readonly List<ObjectBase> Tracks = new();

        private readonly List<ObjectBase> local = new();
        private readonly List<ObjectBase> unknown = new();

        internal bool Holds(ObjectBase? unit)
        {
            if (unit == null) return false;

            foreach (ObjectBase present in Tracks)
            {
                if (ReferenceEquals(present, unit)) return true;
            }

            return false;
        }

        /// <summary>
        /// True when this machine's sensors are on the track, false when the table holds it without
        /// them, null when that cannot be read. Null is never treated as "yes" by the caller.
        /// </summary>
        internal bool? DetectedHere(ObjectBase? unit)
        {
            if (unit == null) return null;

            foreach (ObjectBase present in local)
            {
                if (ReferenceEquals(present, unit)) return true;
            }

            foreach (ObjectBase present in unknown)
            {
                if (ReferenceEquals(present, unit)) return null;
            }

            return Holds(unit) ? false : null;
        }

        internal void Add(ObjectBase unit) => Add(Tracks, unit);
        internal void AddLocal(ObjectBase unit) => Add(local, unit);
        internal void AddUnknown(ObjectBase unit) => Add(unknown, unit);
    }

    /// <summary>Reads one force's picture, or an empty snapshot when the table cannot be read.</summary>
    internal static Snapshot Read(object? table)
    {
        var snapshot = new Snapshot { Table = table };
        if (table == null) return snapshot;

        try
        {
            if (MemberProbe.Read(MemberProbe.Value(table.GetType(), "LocalVehicles", "localVehicles"), table)
                is IDictionary local)
            {
                foreach (DictionaryEntry entry in local)
                {
                    if (entry.Key is not ObjectBase unit) continue;

                    snapshot.Add(unit);

                    bool? detected = SensorsOn(entry.Value);

                    if (detected == true) snapshot.AddLocal(unit);
                    else if (detected == null) snapshot.AddUnknown(unit);
                }
            }

            if (MemberProbe.Read(MemberProbe.Value(table.GetType(), "Vehicles", "vehicles"), table)
                is IEnumerable list)
            {
                foreach (object? item in list)
                {
                    if (item == null) continue;

                    Type type = item.GetType();

                    if (!VehicleUnit.TryGetValue(type, out MemberInfo? member))
                    {
                        member = MemberProbe.Value(type, "Unit", "unit");
                        VehicleUnit[type] = member;
                    }

                    if (MemberProbe.Read(member, item) is ObjectBase unit) snapshot.Add(unit);
                }
            }
        }
        catch (Exception) { }

        return snapshot;
    }

    /// <summary>
    /// Whether the vehicle record in a plotting table has sensors on its track. Read from the game's
    /// own <c>DetectingSensors</c>, the same state the game uses to decide a contact is stale.
    /// </summary>
    private static bool? SensorsOn(object? vehicle)
    {
        if (vehicle == null) return null;

        try
        {
            Type type = vehicle.GetType();

            if (!VehicleSensors.TryGetValue(type, out MemberInfo? member))
            {
                member = MemberProbe.Value(type, "DetectingSensors", "detectingSensors");
                VehicleSensors[type] = member;
            }

            object? value = MemberProbe.Read(member, vehicle);
            if (value == null) return null;

            // An enum: the "nothing is on it" member is called Unknown in this build. Compared as a
            // name, so a reordered enum cannot make a track look locally detected.
            string name = value.ToString() ?? "";
            if (name.Length == 0) return null;

            return !string.Equals(name, "Unknown", StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(name, "None", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Every foreign unit a table holds a track for.</summary>
    internal static List<ObjectBase> ContactsIn(object? table) => Read(table).Tracks;

    private static void Add(List<ObjectBase> into, ObjectBase unit)
    {
        foreach (ObjectBase present in into)
        {
            if (ReferenceEquals(present, unit)) return;
        }

        into.Add(unit);
    }

    // --- the forces in the world ----------------------------------------------------------------

    /// <summary>One task force found among the live units, with the units that belong to it.</summary>
    internal sealed class Force
    {
        internal object? Taskforce;
        internal readonly List<ObjectBase> Units = new();
        internal string Name = "<unnamed force>";

        /// <summary>A unit that can carry a report: it has a plotting table of its own.</summary>
        internal ObjectBase? Detector;

        internal bool Is(object? other) => other != null && ReferenceEquals(Taskforce, other);
    }

    /// <summary>Every task force the machine currently holds units for, ours included.</summary>
    internal static List<Force> Forces()
    {
        var forces = new List<Force>();

        foreach (ObjectBase unit in GameWorld.AllUnits())
        {
            try
            {
                if (unit == null || unit.IsDestroyed) continue;

                if (unit._type != ObjectBase.ObjectType.Vessel
                    && unit._type != ObjectBase.ObjectType.Submarine
                    && unit._type != ObjectBase.ObjectType.Aircraft
                    && unit._type != ObjectBase.ObjectType.Helicopter)
                    continue;

                object? taskforce = unit._taskforce;
                if (taskforce == null) continue;

                Force? force = null;
                foreach (Force present in forces)
                {
                    if (present.Is(taskforce)) { force = present; break; }
                }

                if (force == null)
                {
                    force = new Force { Taskforce = taskforce, Name = NameOf(taskforce) };
                    forces.Add(force);
                }

                force.Units.Add(unit);

                // The detector has to be a unit with a table of its own: the game's reveal writes the
                // track into the table of the force the detector belongs to, so a unit without one is
                // no use as the carrier.
                if (force.Detector == null && HasTable(unit)) force.Detector = unit;
            }
            catch (Exception) { }
        }

        return forces;
    }

    /// <summary>The player's own task force, the same member every other feature reads.</summary>
    internal static object? PlayerTaskforce()
    {
        try { return Globals._playerTaskforce; }
        catch (Exception) { return null; }
    }

    /// <summary>The game's own "the enemy's force" and "nobody's force", when it publishes them.</summary>
    internal static object? StaticTaskforce(params string[] names)
    {
        foreach (string name in names)
        {
            try
            {
                MemberInfo? member = typeof(Globals).GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                if (member == null) continue;

                object? value = member.GetValue(null);
                if (value != null) return value;
            }
            catch (Exception) { }
        }

        return null;
    }

    private static string NameOf(object taskforce)
    {
        try
        {
            Type type = taskforce.GetType();

            foreach (string candidate in new[] { "DisplayName", "Name", "Title", "ShortName", "Id" })
            {
                object? value = MemberProbe.Read(MemberProbe.Value(type, candidate), taskforce);
                if (value is string text && text.Length > 0) return text;
            }

            return type.Name;
        }
        catch (Exception) { return "<unnamed force>"; }
    }
}
