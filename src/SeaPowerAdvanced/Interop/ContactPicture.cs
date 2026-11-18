using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// The player's actual contact picture.
///
/// This is the second half of what was missing. The old code looked for contact lists on the
/// <b>unit</b> - members whose names mentioned spotting, threats or tracks - and the game does not
/// keep them there. A side's picture lives on its <c>Taskforce</c>, in two places:
///
/// <list type="bullet">
/// <item><c>PlottingTable.LocalVehicles</c> - every track the local machine holds, keyed by the
///   object behind it. This is the map. A contact in here may be completely unidentified and it is
///   still a contact: nothing in this file looks at identification to decide whether it exists.</item>
/// <item><c>_spottedObjects</c> - what the side's sensors have reported.</item>
/// </list>
///
/// Reading both and taking the union means a contact the force can see is offered to the jammer
/// whether or not anybody has identified it, which is the whole point of jamming a bearing.
///
/// Identification is reported alongside, but only as information: <see cref="Contact.Identified"/>
/// is null when the game will not say, and nothing refuses to act on that.
/// </summary>
internal static class ContactPicture
{
    /// <summary>One contact: the unit behind the track, and what the picture knows about it.</summary>
    internal sealed class Contact
    {
        internal Contact(ObjectBase unit, bool? identified, string source)
        {
            Unit = unit;
            Identified = identified;
            Source = source;
        }

        internal ObjectBase Unit { get; }

        /// <summary>True once the picture knows whose it is. Null when the game does not say.
        /// Never used to decide whether the contact can be jammed.</summary>
        internal bool? Identified { get; }

        internal string Source { get; }
    }

    private static readonly Dictionary<ObjectBase, Contact> Cache = new();

    /// <summary>Every foreign contact the player's side holds right now, identified or not.</summary>
    internal static List<Contact> Hostiles()
    {
        var contacts = new List<Contact>();
        Cache.Clear();

        object? player = PlayerTaskforce();

        foreach ((ObjectBase unit, bool? identified, string source) in Read(player))
        {
            if (Already(contacts, unit)) continue;

            contacts.Add(new Contact(unit, identified, source));
            Cache[unit] = contacts[contacts.Count - 1];
        }

        return contacts;
    }

    /// <summary>The unit behind a track, for callers that already have one.</summary>
    internal static Contact? Describe(ObjectBase? unit)
    {
        if (unit == null) return null;
        if (Cache.TryGetValue(unit, out Contact? cached)) return cached;

        // Not in the last sweep: answer the identification question on its own rather than hand back
        // a contact from a stale picture.
        return new Contact(unit, Identified(unit), "not in the player's picture");
    }

    /// <summary>Whether the picture has identified this contact, or null if it will not say.</summary>
    internal static bool? Identified(ObjectBase? unit)
    {
        if (unit == null) return null;

        try
        {
            object? table = PlottingTable();
            if (table == null) return null;

            if (MemberProbe.Read(Member(table.GetType(), "vehicles"), table) is not IEnumerable list)
                return null;

            foreach (object? item in list)
            {
                if (item == null) continue;

                object? behind = MemberProbe.Read(Member(item.GetType(), "unit"), item);
                if (!ReferenceEquals(behind, unit)) continue;

                return State(Member(item.GetType(), "identified"), item);
            }
        }
        catch (Exception) { }

        return null;
    }

    /// <summary>What the picture was read from, for <c>offensive-ecm.txt</c>.</summary>
    internal static string Sources()
    {
        var parts = new List<string>();

        object? player = PlayerTaskforce();
        if (player == null) return "no player task force";

        try
        {
            object? table = PlottingTable();
            parts.Add(table == null ? "plotting table: none" : "plotting table: found");
        }
        catch (Exception) { parts.Add("plotting table: unreadable"); }

        bool spotted = false;
        try { spotted = Member(player.GetType(), "spotted") != null; } catch (Exception) { }
        parts.Add(spotted ? "spotted objects: found" : "spotted objects: not found");

        return string.Join(", ", parts);
    }

    /// <summary>Writes what the picture looked like, so a mission with no jammable contact can be
    /// told apart from a mission with no contacts.</summary>
    internal static void WriteReport(List<Contact> contacts)
    {
        try
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine();
            text.AppendLine("Sea Power Advanced - the contact picture");
            text.AppendLine("---------------------------------------");
            text.AppendLine("read from: " + Sources());
            text.AppendLine("foreign contacts held: " + contacts.Count);

            int unidentified = 0;
            int unknownState = 0;
            foreach (Contact contact in contacts)
            {
                if (contact.Identified == false) unidentified++;
                else if (contact.Identified == null) unknownState++;
            }

            text.AppendLine("  identified: " + (contacts.Count - unidentified - unknownState)
                            + "   not identified: " + unidentified
                            + "   state not exposed: " + unknownState);
            text.AppendLine();
            text.AppendLine("A contact does not have to be identified to be jammed. The jam orders in");
            text.AppendLine("this build come in two forms - one aimed at a unit, one at a bearing -");
            text.AppendLine("and the bearing form is exactly the unidentified case.");

            string path = ModFolders.AppendText("offensive-ecm.txt", text.ToString());
            ModLog.Info("Offensive ECM: contact picture written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Offensive ECM: could not write the contact picture", error);
        }
    }

    // --- the two lists ---------------------------------------------------------------------------

    private static IEnumerable<(ObjectBase, bool?, string)> Read(object? player)
    {
        var found = new List<(ObjectBase, bool?, string)>();
        if (player == null) return found;

        // 1. The plotting table: the map itself, including everything unidentified.
        try
        {
            object? table = PlottingTable();

            if (table != null
                && MemberProbe.Read(Member(table.GetType(), "local"), table) is IDictionary local)
            {
                foreach (DictionaryEntry entry in local)
                {
                    if (entry.Key is not ObjectBase unit) continue;
                    if (!Foreign(unit, player)) continue;

                    found.Add((unit, Identified(unit), "plotting table (LocalVehicles)"));
                }
            }

            // The same tracks as a list, for a build whose dictionary this mod cannot read.
            if (table != null
                && MemberProbe.Read(Member(table.GetType(), "vehicles"), table) is IEnumerable vehicles)
            {
                foreach (object? item in vehicles)
                {
                    if (item == null) continue;

                    if (MemberProbe.Read(Member(item.GetType(), "unit"), item) is not ObjectBase unit) continue;
                    if (!Foreign(unit, player)) continue;

                    found.Add((unit, State(Member(item.GetType(), "identified"), item),
                                "plotting table (Vehicles)"));
                }
            }
        }
        catch (Exception error)
        {
            ModLog.Warn("Offensive ECM: could not read the plotting table - " + error.GetType().Name);
        }

        // 2. What the side's sensors have reported, which can include a contact the table has not
        //    been given a track for yet.
        try
        {
            MemberInfo? spotted = Member(player.GetType(), "spotted");

            if (MemberProbe.Read(spotted, player) is IEnumerable list)
            {
                foreach (object? item in list)
                {
                    if (item is not ObjectBase unit) continue;
                    if (!Foreign(unit, player)) continue;

                    found.Add((unit, null, "task force spotted objects"));
                }
            }
        }
        catch (Exception error)
        {
            ModLog.Warn("Offensive ECM: could not read the spotted objects - " + error.GetType().Name);
        }

        return found;
    }

    /// <summary>
    /// True when the unit is not one of ours. Read from the taskforce the game put it in, which is
    /// the same test the game's own contact code uses, rather than from an "is this the player's"
    /// flag that only means something for a unit under command.
    /// </summary>
    private static bool Foreign(ObjectBase unit, object player)
    {
        try
        {
            if (unit.IsDestroyed) return false;

            // Chaff, missiles, wrecks and other clutter are not jamming targets.
            if (unit._type == ObjectBase.ObjectType.Aircraft
                || unit._type == ObjectBase.ObjectType.Helicopter
                || unit._type == ObjectBase.ObjectType.Vessel
                || unit._type == ObjectBase.ObjectType.Submarine
                || unit._type == ObjectBase.ObjectType.LandUnit)
            {
                object? taskforce = unit._taskforce;
                if (taskforce != null && ReferenceEquals(taskforce, player)) return false;

                return true;
            }

            return false;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// A boolean the game keeps behind a reactive property, read either way round: a plain bool, or
    /// an object with a <c>Value</c> on it. Which shape it is differs between the game's own classes,
    /// and this question is only ever "did the picture identify it", never a decision to refuse.
    /// </summary>
    private static bool? State(MemberInfo? member, object target)
    {
        object? value = MemberProbe.Read(member, target);
        if (value is bool simple) return simple;

        return MemberProbe.ReadReactiveBool(member, target);
    }

    private static bool Already(List<Contact> contacts, ObjectBase unit)
    {
        foreach (Contact contact in contacts)
        {
            if (ReferenceEquals(contact.Unit, unit)) return true;
        }

        return false;
    }

    // --- reaching the game's own objects ---------------------------------------------------------

    private static object? PlayerTaskforce()
    {
        try { return Globals._playerTaskforce; }
        catch (Exception) { return null; }
    }

    private static object? PlottingTable()
    {
        try
        {
            object? player = PlayerTaskforce();
            if (player == null) return null;

            object? table = MemberProbe.Read(Member(player.GetType(), "table"), player);
            if (table != null) return table;

            // Taskforce.PlottingTable is public in this build; the field is the fallback.
            return MemberProbe.Read(Member(player.GetType(), "tableField"), player);
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// The four members this file needs, looked up by name with a shape fallback. Names first,
    /// because they are the ones taken from the game's own contact code; the fallbacks exist so a
    /// renamed field still resolves instead of silently emptying the picture.
    /// </summary>
    private static MemberInfo? Member(Type type, string what)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        string[] names = what switch
        {
            "local" => new[] { "LocalVehicles", "localVehicles" },
            "vehicles" => new[] { "Vehicles", "vehicles" },
            "unit" => new[] { "BaseObject", "baseObject" },
            "identified" => new[] { "Identified", "identified" },
            "spotted" => new[] { "_spottedObjects", "spottedObjects" },
            "table" => new[] { "PlottingTable", "plottingTable" },
            "tableField" => new[] { "_plottingTable", "plottingTable" },
            _ => Array.Empty<string>(),
        };

        foreach (string name in names)
        {
            MemberInfo? member = type.GetField(name, all) as MemberInfo
                                 ?? type.GetProperty(name, all);
            if (member != null) return member;
        }

        if (what == "spotted")
        {
            foreach (MemberInfo member in type.GetMembers(all))
            {
                if (member is not FieldInfo && member is not PropertyInfo) continue;
                if (member.Name.IndexOf("spott", StringComparison.OrdinalIgnoreCase) >= 0) return member;
            }
        }

        return null;
    }
}
