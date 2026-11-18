using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// The game's own offensive jammer, and the order that points it.
///
/// This is the half that was missing. <see cref="EcmControl"/> looks for ECM members on the unit
/// itself, and the game does not keep them there: a jammer is a <b>sensor system</b>, one entry in
/// the unit's parameter block, shot through with its own state. Nothing that scans
/// <see cref="ObjectBase"/> for a member called "ECM" will ever find one, which is why the menu
/// offered nothing to click.
///
/// Two things are driven here, and both are the game's own:
///
/// <list type="number">
/// <item><b>The order.</b> <c>setOrder(Order.Type.Jam, target, displayOrderText)</c> is what the
///   weapons panel calls when the player points an EA-6B's pod at a contact, and what a bearing-only
///   jam uses with a position instead of a unit. The game then runs its own jam task: it picks the
///   set, sets the order on the unit, and puts the assignment where the radar calculator reads it.
///   In a session this is also the order the multiplayer mod forwards to the host, so a jam issued
///   through it is a jam in the authoritative simulation rather than a write to a replica.</item>
/// <item><b>The set.</b> The sensor entry is read to answer "does this hull carry a jammer at all"
///   and "is it transmitting right now", and its own teardown (<c>ceaseOffensiveJam</c>) is called
///   to stop a jam that the order started.</item>
/// </list>
///
/// Nothing is assumed about names that the mod has not been able to verify: every member is looked
/// up at runtime, the shapes are matched by signature, and everything found is written to
/// <c>offensive-ecm.txt</c>. A build that names these differently reports what it does have instead
/// of failing silently.
/// </summary>
internal static class OffensiveJam
{
    private static bool looked;
    private static bool reported;
    private static bool reportedFound;

    /// <summary>The field on a unit's parameter block that holds its sensor systems, by parameter
    /// type: the name is looked for on the type the game actually hands over, so no type name has to
    /// be guessed in advance.</summary>
    private static readonly Dictionary<Type, FieldInfo?> SensorFields = new();

    /// <summary>The one that was found, for the report.</summary>
    private static FieldInfo? sensorField;

    /// <summary>The jam order overloads: one takes a unit target, one takes a bearing.</summary>
    private static MethodInfo? orderUnit;
    private static MethodInfo? orderGeo;

    /// <summary>The enum value that means "jam", found on whatever enum the order takes.</summary>
    private static object? jamValue;

    /// <summary>Money: the state of one ECM sensor system.</summary>
    internal sealed class Set
    {
        internal Set(object system, int index)
        {
            System = system;
            Index = index;
        }

        /// <summary>The <c>SensorSystemECM</c> instance, held as an object: no game type is named
        /// in this file, so a build that renames the class still reports instead of failing to
        /// compile.</summary>
        internal object System { get; }

        /// <summary>Its index in the parameter block's sensor list - how the game addresses it.</summary>
        internal int Index { get; }
    }

    // --- discovery -------------------------------------------------------------------------------

    /// <summary>Look everything up once. Cheap and safe to call from anywhere.</summary>
    internal static void Discover()
    {
        if (looked) return;
        looked = true;

        try
        {
            FindOrder();
        }
        catch (Exception error)
        {
            ModLog.Error("Offensive ECM: could not find the game's jam order", error);
        }

        ModLog.Info("Offensive ECM: " + Summary());

        // Written at startup as well, because the install where nothing is found is the one whose
        // report matters most. The sensor list is only read off a live unit, so a second block is
        // written the first time one is found; offensive-ecm.txt is appended to, not overwritten.
        if (!reported)
        {
            reported = true;
            WriteReport();
        }
    }

    /// <summary>
    /// The jam order. Both overloads are three arguments with the order type first and the
    /// "show this in the log" flag last; what tells the two apart is the middle one - a unit, or a
    /// position.
    /// </summary>
    private static void FindOrder()
    {
        foreach (MethodInfo method in typeof(ObjectBase).GetMethods(BindingFlags.Public
                                                                    | BindingFlags.NonPublic
                                                                    | BindingFlags.Instance))
        {
            if (method.Name != "setOrder") continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 3) continue;
            if (!parameters[0].ParameterType.IsEnum) continue;

            object? value = JamValue(parameters[0].ParameterType);
            if (value == null) continue;

            if (parameters[1].ParameterType.IsAssignableFrom(typeof(ObjectBase)))
            {
                if (orderUnit == null) { orderUnit = method; jamValue = value; }
                continue;
            }

            // A bearing: the middle argument is the game's own GeoPosition struct.
            if (parameters[1].ParameterType.IsValueType
                && parameters[1].ParameterType.Name.Contains("GeoPosition"))
            {
                if (orderGeo == null) { orderGeo = method; jamValue ??= value; }
            }
        }
    }

    /// <summary>The enum value named "Jam" (or whose name contains it) on the order type.</summary>
    private static object? JamValue(Type orderType)
    {
        try
        {
            foreach (string name in Enum.GetNames(orderType))
            {
                if (string.Equals(name, "Jam", StringComparison.OrdinalIgnoreCase))
                    return Enum.Parse(orderType, name);
            }

            foreach (string name in Enum.GetNames(orderType))
            {
                if (name.IndexOf("jam", StringComparison.OrdinalIgnoreCase) >= 0)
                    return Enum.Parse(orderType, name);
            }
        }
        catch (Exception) { }

        return null;
    }

    /// <summary>True when the mod can point this build's jammer at something.</summary>
    internal static bool Available
    {
        get
        {
            Discover();
            return orderUnit != null;
        }
    }

    /// <summary>True when a bearing-only jam can be issued (an unidentified contact's position).</summary>
    internal static bool CanJamBearing
    {
        get
        {
            Discover();
            return orderGeo != null;
        }
    }

    // --- the sets on a unit ----------------------------------------------------------------------

    /// <summary>
    /// Every ECM sensor system on a unit, in parameter-block order.
    ///
    /// A hull without one gets an empty list, which is how "no ECM on subs" is answered by the game
    /// itself rather than by a guess about the hull type.
    /// </summary>
    internal static List<Set> Systems(ObjectBase? unit)
    {
        var found = new List<Set>();
        if (unit == null) return found;

        Discover();

        object? sensors = null;
        try
        {
            object? parameters = Capabilities.ParametersOf(unit);
            if (parameters == null) parameters = unit._obp;
            if (parameters == null) return found;

            sensors = MemberProbe.Read(SensorField(parameters.GetType()), parameters);
        }
        catch (Exception) { return found; }

        if (sensors is not IEnumerable list) return found;

        int index = 0;
        foreach (object? entry in list)
        {
            if (entry == null) { index++; continue; }

            if (IsJammer(entry)) found.Add(new Set(entry, index));
            index++;
        }

        if (found.Count > 0 && sensorField != null && !reportedFound)
        {
            reportedFound = true;
            WriteReport();
        }

        return found;
    }

    /// <summary>
    /// The sensor list on a parameter block. Found on the type the game actually hands over, so this
    /// works whether the block is <c>ObjectBaseParameters</c> or a subclass of it, and a build that
    /// renames the field still resolves as long as it keeps one list of sensors.
    /// </summary>
    private static FieldInfo? SensorField(Type parametersType)
    {
        if (SensorFields.TryGetValue(parametersType, out FieldInfo? cached)) return cached;

        FieldInfo? found = null;

        try
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            found = parametersType.GetField("_sensorSystems", all)
                    ?? parametersType.GetField("_sensors", all);

            if (found == null)
            {
                foreach (FieldInfo field in parametersType.GetFields(all))
                {
                    if (!typeof(IEnumerable).IsAssignableFrom(field.FieldType)) continue;
                    if (field.FieldType == typeof(string)) continue;
                    if (field.Name.IndexOf("sensor", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    // More than one candidate means the name is doing the choosing, which it is not
                    // allowed to do here: report nothing rather than pick wrong.
                    if (found != null) { found = null; break; }
                    found = field;
                }
            }
        }
        catch (Exception) { found = null; }

        SensorFields[parametersType] = found;
        sensorField ??= found;
        return found;
    }

    /// <summary>Whether one sensor entry is an ECM set. The game's own class name says so; the
    /// alternative - guessing from a behaviour - would be a name the mod cannot check.</summary>
    private static bool IsJammer(object system)
    {
        string name = system.GetType().Name;
        return name.IndexOf("ECM", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Jam", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Does this unit carry a jammer the game can point?
    ///
    /// The unit answers for itself first: <c>isOffensiveEcmSystemAvailable()</c> and
    /// <c>HasSelfScreeningJammer</c> are the game's own tests, and they are the ones its AI and its
    /// weapons panel use. Only when neither is readable does the sensor list get walked.
    /// </summary>
    internal static bool HasEcm(ObjectBase? unit)
    {
        if (unit == null) return false;

        bool? available = Flag(unit, "isOffensiveEcmSystemAvailable", "IsOffensiveEcmSystemAvailable");
        if (available == true) return true;

        bool? fitted = Flag(unit, "HasSelfScreeningJammer", "HasNoiseJammerSystem");
        if (fitted.HasValue) return fitted.Value;

        return Systems(unit).Count > 0;
    }

    /// <summary>
    /// A boolean the unit reports, asked through a method or a property: the game exposes some of
    /// these as <c>isSomething()</c> and some as a member with a backing field.
    /// </summary>
    private static bool? Flag(ObjectBase unit, params string[] names)
    {
        foreach (string name in names)
        {
            try
            {
                MemberInfo? member = MemberProbe.Value(unit.GetType(), name);

                if (member is MethodInfo method && method.GetParameters().Length == 0
                    && method.ReturnType == typeof(bool))
                {
                    object? result = method.Invoke(unit, null);
                    if (result is bool answer) return answer;
                    continue;
                }

                if (member == null) continue;

                object? value = MemberProbe.Read(member, unit);
                if (value is bool flag) return flag;

                bool? reactive = MemberProbe.ReadReactiveBool(member, unit);
                if (reactive.HasValue) return reactive.Value;
            }
            catch (Exception) { }
        }

        return null;
    }

    /// <summary>A machine-readable line for the menu and the report.</summary>
    internal static string Summary()
    {
        var parts = new List<string>();

        parts.Add(orderUnit != null ? "order=(Jam, unit)" : "order=none");
        if (orderGeo != null) parts.Add("bearing=yes");
        parts.Add(sensorField != null ? "sensors=" + sensorField.Name : "sensors=<not found>");

        return string.Join("  ", parts);
    }

    // --- status ----------------------------------------------------------------------------------

    /// <summary>The unit this set is pointed at, if it is pointed at one.</summary>
    internal static ObjectBase? TargetOf(Set set) => ReadTarget(set.System);

    private static ObjectBase? ReadTarget(object system)
    {
        try
        {
            FieldInfo? field = Member(system.GetType(), "target");
            object? value = MemberProbe.Read(field, system);
            return value as ObjectBase;
        }
        catch (Exception) { return null; }
    }

    /// <summary>True when the set is transmitting, either at a unit or on a bearing.</summary>
    internal static bool IsJamming(Set set)
    {
        if (ReadTarget(set.System) != null) return true;

        try
        {
            FieldInfo? flag = Member(set.System.GetType(), "geo");
            return MemberProbe.Read(flag, set.System) is bool on && on;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Any system on this unit transmitting, and at what (unit name or bearing).</summary>
    internal static string Status(ObjectBase? unit)
    {
        List<Set> sets = Systems(unit);
        if (sets.Count == 0) return "no jammer";

        foreach (Set set in sets)
        {
            ObjectBase? target = ReadTarget(set.System);
            if (target != null) return "jamming " + Name(target);

            if (IsJamming(set)) return "jamming a bearing";
        }

        return "idle";
    }

    /// <summary>
    /// True when the unit is transmitting, at anything.
    ///
    /// <c>IsOffensivelyJamming</c> is the game's own answer, and it is the one the report from a
    /// live install names, so it is asked first; the sensor-level check is the fallback for a build
    /// that does not expose it.
    /// </summary>
    internal static bool IsTransmitting(ObjectBase? unit)
    {
        if (unit == null) return false;

        bool? jamming = Flag(unit, "IsOffensivelyJamming", "isOffensivelyJamming");
        if (jamming.HasValue) return jamming.Value;

        foreach (Set set in Systems(unit))
        {
            if (IsJamming(set)) return true;
        }

        return false;
    }

    /// <summary>True when any of the unit's sets is transmitting at this contact.</summary>
    internal static bool IsJammingAt(ObjectBase? unit, ObjectBase? contact)
    {
        if (unit == null || contact == null) return false;

        foreach (Set set in Systems(unit))
        {
            if (ReferenceEquals(ReadTarget(set.System), contact)) return true;
        }

        return false;
    }

    // --- pointing it -----------------------------------------------------------------------------

    /// <summary>
    /// Points the unit's jammer at a contact, through the game's own jam order.
    ///
    /// The order is what makes the effect real: the game's task selects the set, sets
    /// <c>CurrentOrder</c>, records it in the order log, and registers the assignment where the
    /// radar calculator reads it. Writing the assignment by hand reaches the same place in single
    /// player but skips all of that - and in a session it reaches a replica. So the order is tried
    /// first and always, and the direct write is only the fallback for a build whose order the mod
    /// could not find.
    /// </summary>
    internal static bool PointAt(ObjectBase unit, ObjectBase contact, out string note)
    {
        note = "";
        Discover();

        if (unit == null || contact == null)
        {
            note = "no jammer or no contact";
            return false;
        }

        if (orderUnit != null)
        {
            try
            {
                orderUnit.Invoke(unit, new object?[] { jamValue, contact, true });
                note = "jam order issued on " + Name(unit) + " against " + Name(contact);
                return true;
            }
            catch (Exception error)
            {
                note = "the game's jam order refused: " + error.GetType().Name;
                ModLog.Warn("Offensive ECM: " + note + " - falling back to the assignment itself");
            }
        }

        return Assign(unit, contact, out note);
    }

    /// <summary>Points the jammer at a bearing, which is how a contact nobody has identified is
    /// jammed: the game's own second jam order takes a position, not a unit.</summary>
    internal static bool PointAt(ObjectBase unit, GeoPosition bearing, out string note)
    {
        note = "";
        Discover();

        if (unit == null)
        {
            note = "no jammer";
            return false;
        }

        if (orderGeo == null || jamValue == null)
        {
            note = "this build has no bearing jam the mod can name - see offensive-ecm.txt";
            return false;
        }

        try
        {
            orderGeo.Invoke(unit, new object?[] { jamValue, bearing, true });
            note = "bearing jam issued on " + Name(unit);
            return true;
        }
        catch (Exception error)
        {
            note = "the bearing jam order refused: " + error.GetType().Name;
            ModLog.Warn("Offensive ECM: " + note);
            return false;
        }
    }

    /// <summary>
    /// The direct assignment, which is what the game's own AI does: the target is written onto the
    /// set and the set is registered on the target's list of jammers. Kept as the fallback, and used
    /// to re-assert a jam whose order the crew has since replaced.
    /// </summary>
    private static bool Assign(ObjectBase unit, ObjectBase contact, out string note)
    {
        note = "";

        List<Set> sets = Systems(unit);
        if (sets.Count == 0)
        {
            note = Name(unit) + " has no ECM set this build exposes";
            return false;
        }

        foreach (Set set in sets)
        {
            try
            {
                FieldInfo? target = Member(set.System.GetType(), "target");
                FieldInfo? geo = Member(set.System.GetType(), "geo");

                MemberProbe.Write(target, set.System, contact);
                MemberProbe.Write(geo, set.System, false);

                Register(contact, set.System);

                note = Name(unit) + "'s jammer set to " + Name(contact);
                return true;
            }
            catch (Exception error)
            {
                note = "could not write the jam assignment: " + error.GetType().Name;
                ModLog.Error("Offensive ECM: " + note, error);
            }
        }

        return false;
    }

    /// <summary>
    /// Adds the set to the target's own list of jammers. The game keeps this list ("which systems
    /// are trying to jam me") and its radar calculator reads it, so a jam that is not in it does
    /// nothing at all even though the set looks pointed.
    /// </summary>
    private static void Register(ObjectBase contact, object system)
    {
        try
        {
            FieldInfo? field = typeof(ObjectBase).GetField("_ECMSystemsTryingToJamMe",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) return;

            if (MemberProbe.Read(field, contact) is not IList list) return;
            if (list.Contains(system)) return;

            list.Add(system);
        }
        catch (Exception error)
        {
            ModLog.Warn("Offensive ECM: could not register the jammer on the target - "
                        + error.GetType().Name);
        }
    }

    // --- stopping it -----------------------------------------------------------------------------

    /// <summary>
    /// Stops every set on the unit. <c>ceaseOffensiveJam</c> is the game's own teardown: it clears
    /// the assignment and takes the set off every victim's list, which nothing else does once the
    /// assignment has been replaced.
    /// </summary>
    internal static bool Cease(ObjectBase unit, out string note)
    {
        note = "";
        Discover();

        List<Set> sets = Systems(unit);

        bool stopped = false;

        // The game's own teardown on the unit, which is the name this build actually uses
        // (CeaseOffensiveJamming). It takes the set off every victim's list on its way out, which
        // nothing at the sensor level is guaranteed to do.
        foreach (string name in new[] { "CeaseOffensiveJamming", "ceaseOffensiveJamming",
                                        "CeaseOffensiveJammingOfThisObject" })
        {
            try
            {
                MethodInfo? unitTeardown = MemberProbe.Method(unit.GetType(), 0, name);
                if (unitTeardown == null) continue;

                unitTeardown.Invoke(unit, null);
                stopped = true;
                break;
            }
            catch (Exception error)
            {
                ModLog.Warn("Offensive ECM: " + name + "() refused on " + Name(unit) + " - "
                            + error.GetType().Name);
            }
        }

        foreach (Set set in sets)
        {
            try
            {
                Type type = set.System.GetType();

                // The teardown belongs to the ECMSystem the sensor entry holds, not to the sensor
                // entry itself: the game takes an offensive jam down through that object. The
                // sensor's own method is tried as well, for a build that keeps it there instead.
                object? holder = MemberProbe.Read(Member(type, "system"), set.System);

                MethodInfo? teardown = holder != null ? Teardown(holder.GetType()) : null;
                if (teardown != null && holder != null) teardown.Invoke(holder, null);

                MethodInfo? ownTeardown = Teardown(type);
                if (ownTeardown != null)
                {
                    Type? owner = ownTeardown.DeclaringType;
                    if (owner != null && owner.IsInstanceOfType(set.System))
                        ownTeardown.Invoke(set.System, null);
                }

                MemberProbe.Write(Member(type, "target"), set.System, null);
                MemberProbe.Write(Member(type, "geo"), set.System, false);

                stopped = true;
            }
            catch (Exception error)
            {
                ModLog.Warn("Offensive ECM: could not stop a set on " + Name(unit) + " - "
                            + error.GetType().Name);
            }
        }

        note = stopped
            ? "jammer stopped on " + Name(unit)
            : sets.Count == 0
                ? "no jammer found on " + Name(unit) + " to stop"
                : "the jammer did not respond";
        return stopped;
    }

    private static MethodInfo? Teardown(Type type)
    {
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public
                                                     | BindingFlags.NonPublic))
        {
            if (!method.IsStatic && method.GetParameters().Length == 0
                && method.Name.IndexOf("cease", StringComparison.OrdinalIgnoreCase) == 0)
                return method;
        }

        return null;
    }

    // --- member lookup ---------------------------------------------------------------------------

    /// <summary>
    /// The field on an ECM set for one of three things: the target it is pointed at, the flag that
    /// says it is aiming at a bearing, and the ECMSystem object that holds the teardown.
    ///
    /// Looked up by shape as well as by name, so a build that renames a field still works as long as
    /// the field is there: the target is the only member that can hold a unit, and the bearing flag
    /// is the only bool whose name mentions a position.
    /// </summary>
    private static FieldInfo? Member(Type type, string what)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        string[] names = what switch
        {
            "target" => new[] { "_associatedTarget", "associatedTarget", "_target" },
            "geo" => new[] { "_geoPositionTargeted", "geoPositionTargeted", "_bearingTargeted" },
            "system" => new[] { "_ecm", "ecm", "_ecmSystem", "_system" },
            _ => Array.Empty<string>(),
        };

        foreach (string name in names)
        {
            FieldInfo? field = type.GetField(name, all);
            if (field != null) return field;
        }

        foreach (FieldInfo field in type.GetFields(all))
        {
            switch (what)
            {
                case "target" when field.FieldType.IsAssignableFrom(typeof(ObjectBase)):
                    return field;

                case "geo" when field.FieldType == typeof(bool)
                                && (field.Name.IndexOf("geo", StringComparison.OrdinalIgnoreCase) >= 0
                                    || field.Name.IndexOf("position", StringComparison.OrdinalIgnoreCase) >= 0
                                    || field.Name.IndexOf("bearing", StringComparison.OrdinalIgnoreCase) >= 0):
                    return field;

                case "system" when field.FieldType != typeof(bool)
                                   && field.FieldType.IsClass
                                   && Teardown(field.FieldType) != null:
                    return field;
            }
        }

        return null;
    }

    private static string Name(ObjectBase? unit)
    {
        if (unit == null) return "<none>";

        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }

    // --- report ----------------------------------------------------------------------------------

    /// <summary>
    /// Writes what this layer found to <c>offensive-ecm.txt</c>, so an install whose jammer a
    /// different name can be diagnosed without a console. Called once, when the map first offers the
    /// feature.
    /// </summary>
    internal static void WriteReport()
    {
        try
        {
            Discover();

            var text = new System.Text.StringBuilder();
            text.AppendLine();
            text.AppendLine("Sea Power Advanced - the game's own jammer");
            text.AppendLine("--------------------------------------");
            text.AppendLine("what the mod found: " + Summary());
            text.AppendLine();
            text.AppendLine("  jam order (unit)   " + (orderUnit != null
                ? orderUnit.DeclaringType?.Name + "." + orderUnit.Name : "<not found>"));
            text.AppendLine("  jam order (bearing)" + " " + (orderGeo != null ? "found" : "<not found>"));
            text.AppendLine("  jam value          " + (jamValue?.ToString() ?? "<not found>"));
            text.AppendLine("  sensor list        " + (sensorField != null
                ? sensorField.DeclaringType?.Name + "." + sensorField.Name : "<not found>"));
            text.AppendLine();
            text.AppendLine("Unit-level ECM members this build exposes");
            text.AppendLine("----------------------------------------");
            foreach (string name in new[]
                     {
                         "isOffensiveEcmSystemAvailable", "IsOffensivelyJamming",
                         "CeaseOffensiveJamming", "_jamBearings", "_ECMSystemsTryingToJamMe",
                         "EngageWithECM", "NumberOfFreeJamChannels", "HasSelfScreeningJammer",
                     })
            {
                bool present = MemberProbe.Value(typeof(ObjectBase), name) != null;
                text.AppendLine("  " + (present ? "ok  " : "--  ") + name);
            }

            text.AppendLine();
            text.AppendLine("A jammer is a sensor system, not a member of the unit: it lives in the");
            text.AppendLine("unit's parameter block. Anything scanning the unit type for a member");
            text.AppendLine("called 'ECM' finds nothing, which is why the earlier builds offered no");
            text.AppendLine("menu entry and would not lock.");
            text.AppendLine();
            text.AppendLine("The jam itself goes through the game's own order, so the effect is the");
            text.AppendLine("game's: its radar calculator reads the assignment, the cone appears on the");
            text.AppendLine("map, and in a session the multiplayer mod forwards the same order to the");
            text.AppendLine("host - which is the machine that decides what a jam does.");

            string path = ModFolders.AppendText("offensive-ecm.txt", text.ToString());
            ModLog.Info("Offensive ECM report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Offensive ECM: could not write offensive-ecm.txt", error);
        }
    }
}
