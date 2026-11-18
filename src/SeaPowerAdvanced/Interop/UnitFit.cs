using System;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// What a particular unit can actually do, as opposed to what the mod can do in general.
///
/// The menu was showing every entry on every unit, which is wrong twice over: a submarine has no
/// jammer, and an enemy contact is not a thing you order to fly a search pattern. This answers the
/// per-unit questions, and it answers "unknown" rather than guessing when the game does not say -
/// a feature that is hidden because the mod could not read a flag is a feature the player thinks is
/// broken.
///
/// Every lookup is by name against the unit and its parameter block, so nothing here is a type the
/// project has to be able to name.
/// </summary>
internal static class UnitFit
{
    private static readonly Dictionary<(Type, string), MemberInfo?> Cache = new();

    /// <summary>True for the player's own units. Unknown counts as the player's, so features stay.</summary>
    internal static bool IsPlayer(ObjectBase? unit)
    {
        if (unit == null) return false;

        try { return unit.IsPlayerUnit; }
        catch (Exception) { return true; }
    }

    internal static bool IsSubmarine(ObjectBase? unit)
    {
        if (unit == null) return false;

        try { return unit.IsSubUnit; }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// What the mod can say about this hull's jammer.
    ///
    /// Positive evidence only. The sensor list of the unit's own parameter block is asked first,
    /// because it is this hull's actual fit read from the installed game; the game's own unit flags
    /// are the fallback for a build whose parameter block cannot be reached; and when neither
    /// answers, this says <see cref="OffensiveJam.JammerFit.Unknown"/> rather than yes.
    ///
    /// The previous version answered "it is a surface hull, so a jammer is fitted", and any unit that
    /// did not positively deny it was therefore treated as a jammer - a Huak class, with a radar and
    /// an RWR and nothing else, got defensive ECM armed on it, radiated air search for a set it does
    /// not carry, and reported a jam that was never transmitted.
    ///
    /// A submarine is excluded whatever the flags say. An aircraft is not: a pod the hull's own
    /// sensor list holds as an ECM set is a jammer, and the list answers for itself.
    /// </summary>
    internal static OffensiveJam.JammerFit EcmFit(ObjectBase? unit, out string evidence)
    {
        evidence = "no unit";
        if (unit == null) return OffensiveJam.JammerFit.Unknown;

        try
        {
            if (IsSubmarine(unit))
            {
                evidence = "a submarine carries no jammer";
                return OffensiveJam.JammerFit.None;
            }

            // An aircraft is not excluded: a pod that the hull's own sensor list holds as an ECM set
            // is a jammer, and an EA-6B carries one. A fighter's list holds no set, so it answers
            // for itself.
            return OffensiveJam.Fit(unit, out evidence);
        }
        catch (Exception error)
        {
            evidence = "the fit could not be read (" + error.GetType().Name + ")";
            return OffensiveJam.JammerFit.Unknown;
        }
    }

    /// <summary>True only when a jammer is positively fitted on this hull.</summary>
    internal static bool HasEcm(ObjectBase? unit) => EcmFit(unit, out _) == OffensiveJam.JammerFit.Fitted;

    /// <summary>
    /// Whether this unit can put an aircraft up. Same shape of answer: a per-unit flag or a fitted
    /// deck if the game says, otherwise surface hulls are given the benefit of the doubt and
    /// submarines are not.
    /// </summary>
    internal static bool HasAviation(ObjectBase? unit)
    {
        if (unit == null) return false;
        if (IsSubmarine(unit)) return false;

        if (!FlightDeckControl.Available) return false;

        bool? known = Flag(unit, "HasFlightDeck", "HasHelipad", "HasHangar", "HasHelicopter",
                           "CanLaunchHelicopter", "CanLaunchAircraft");
        if (known.HasValue) return known.Value;

        bool? fitted = Fitted(unit, "_flightDeck", "FlightDeck", "_helipad", "Helipad",
                              "_hangar", "Hangar", "_aircraft", "Aircraft");
        if (fitted.HasValue) return fitted.Value;

        return IsSurfaceHull(unit);
    }

    /// <summary>
    /// Whether this unit is in the air: an aircraft, fixed wing or rotary.
    ///
    /// This matters twice. A jammer's reach against something airborne is limited by the radio
    /// horizon and not by the surface one, so it is far longer; and an aircraft's sensor fit is a
    /// single radar rather than a ship's several families, which changes what the report can honestly
    /// claim to have switched off.
    /// </summary>
    internal static bool IsAir(ObjectBase? unit)
    {
        if (unit is null) return false;

        try
        {
            return unit._type == ObjectBase.ObjectType.Aircraft
                || unit._type == ObjectBase.ObjectType.Helicopter;
        }
        catch (Exception) { return false; }
    }

    /// <summary>One word for the menu: aircraft, helicopter, submarine, ship.</summary>
    internal static string Kind(ObjectBase? unit)
    {
        if (unit is null) return "unit";

        try
        {
            if (unit._type == ObjectBase.ObjectType.Aircraft) return "aircraft";
            if (unit._type == ObjectBase.ObjectType.Helicopter) return "helicopter";
        }
        catch (Exception) { }

        if (IsSubmarine(unit)) return "submarine";
        return IsSurfaceHull(unit) ? "ship" : "unit";
    }

    /// <summary>Whether this hull is a surface ship - the only hull type the menu's orders suit.</summary>
    internal static bool IsSurfaceHull(ObjectBase? unit)
    {
        if (unit == null) return false;

        try
        {
            if (unit.IsSubUnit) return false;
            return unit.IsSurfaceUnit;
        }
        catch (Exception) { return false; }
    }

    /// <summary>One line for the log, so a menu that looks wrong can be explained from the file.</summary>
    internal static string Describe(ObjectBase? unit)
    {
        if (unit == null) return "<none>";

        return SafeName(unit) + ": " + Kind(unit) + " player=" + IsPlayer(unit)
               + " surface=" + IsSurfaceHull(unit)
               + " air=" + IsAir(unit)
               + " sub=" + IsSubmarine(unit)
               + " ecm=" + HasEcm(unit)
               + " aviation=" + HasAviation(unit);
    }

    // --- reading the game's own flags -------------------------------------------------------------

    /// <summary>A boolean flag on the unit, if one of those names resolves to one.</summary>
    private static bool? Flag(ObjectBase unit, params string[] names)
    {
        foreach (string name in names)
        {
            MemberInfo? member = Lookup(unit.GetType(), name);
            if (member == null) continue;

            try
            {
                if (member is PropertyInfo property && property.PropertyType == typeof(bool))
                    return property.GetValue(unit) as bool?;

                if (member is FieldInfo field && field.FieldType == typeof(bool))
                    return field.GetValue(unit) as bool?;

                if (member is MethodInfo method && method.ReturnType == typeof(bool)
                    && method.GetParameters().Length == 0)
                    return method.Invoke(unit, null) as bool?;
            }
            catch (Exception) { }
        }

        return null;
    }

    /// <summary>Whether a fitted object (deck, jammer) exists on this unit, by reference or flag.</summary>
    private static bool? Fitted(ObjectBase unit, params string[] names)
    {
        foreach (string name in names)
        {
            MemberInfo? member = Lookup(unit.GetType(), name);
            if (member is not FieldInfo && member is not PropertyInfo) continue;

            try
            {
                object? value = MemberProbe.Read(member, unit);
                if (value == null) return false;

                // A boolean is a flag, not a fitted set.
                if (value is bool flag) return flag;

                return true;
            }
            catch (Exception) { }
        }

        return null;
    }

    /// <summary>
    /// A member by name on the unit's type or its parameter block. The parameter block is checked
    /// because that is where the game keeps what a hull is fitted with.
    /// </summary>
    private static MemberInfo? Lookup(Type type, string name)
    {
        var key = (type, name);
        if (Cache.TryGetValue(key, out MemberInfo? cached)) return cached;

        MemberInfo? found = null;

        try
        {
            found = MemberProbe.Value(type, name)
                    ?? (MemberInfo?)MemberProbe.Method(type, 0, name);
        }
        catch (Exception) { }

        Cache[key] = found;
        return found;
    }

    private static string SafeName(ObjectBase unit)
    {
        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }
}
