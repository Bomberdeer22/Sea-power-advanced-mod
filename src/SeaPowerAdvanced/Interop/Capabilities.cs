using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>A game member the mod needs, plus what happened when it looked for it.</summary>
internal sealed class Capability
{
    internal Capability(string id, string description)
    {
        Id = id;
        Description = description;
    }

    internal string Id { get; }
    internal string Description { get; }

    /// <summary>The member that was found, if any. Never used unless <see cref="Found"/>.</summary>
    internal MemberInfo? Member { get; set; }

    /// <summary>What else was tried, so a failure can be acted on without another report.</summary>
    internal string Tried { get; set; } = "";

    internal bool Found => Member != null;

    internal string Signature => MemberProbe.Describe(Member);
}

/// <summary>
/// The bridge between "the game has no such member" and "the feature is impossible".
///
/// Three different situations were being reported as one BLOCKED/MISS:
///
/// 1. The game has the member and it is public - just call it. (Not the interesting case.)
/// 2. The game has it but under a name the offline scan did not guess, or non-public. Reflection
///    finds these at runtime, and the DLL keeps working when a patch renames one.
/// 3. The game genuinely has no concept - crew fatigue, general quarters. Nothing can be found
///    because nothing exists, so the mod keeps that state itself in <see cref="UnitState"/> and
///    drives it through levers the game does have.
///
/// Only case 3 was ever a real gap, and it is a much smaller set than the report implied.
/// </summary>
internal static class Capabilities
{
    // Lookups are deliberately per-concern, not one giant "find anything called Active".
    internal static Capability RadarSwitch { get; } =
        new("radar switch", "turn a unit's radars on or off from code");

    internal static Capability TowedSonarSwitch { get; } =
        new("towed sonar switch", "raise or lower the towed array");

    internal static Capability SonarSwitch { get; } =
        new("sonar switch", "turn the hull sonars on or off");

    internal static Capability EngineThrottle { get; } =
        new("engine throttle", "order a speed without the UI");

    internal static Capability DecoyRelease { get; } =
        new("decoy release", "fire chaff or decoys without the UI");

    internal static Capability HeadingOrder { get; } =
        new("heading order", "steer a unit without the UI");

    internal static Capability WaypointTask { get; } =
        new("waypoint task", "push a task onto a unit's route");

    private static readonly List<Capability> All = new()
    {
        RadarSwitch, TowedSonarSwitch, SonarSwitch, EngineThrottle, DecoyRelease, HeadingOrder, WaypointTask,
    };

    private static bool discovered;

    /// <summary>How many of the capabilities above were found. For the startup summary.</summary>
    internal static int FoundCount
    {
        get
        {
            int count = 0;
            foreach (Capability capability in All) if (capability.Found) count++;
            return count;
        }
    }

    internal static int TotalCount => All.Count;

    /// <summary>
    /// Looks everything up once. Safe to call more than once; does no work after the first pass.
    /// </summary>
    internal static void Discover()
    {
        if (discovered) return;
        discovered = true;

        Type unit = typeof(ObjectBase);

        RadarSwitch.Member = MemberProbe.Method(unit, new[] { typeof(bool) },
            "RadarsSetActive", "SetRadarsActive", "SetRadarActive", "RadarsOn", "SetRadarsOn");
        RadarSwitch.Tried = "RadarsSetActive(bool), SetRadarsActive(bool), RadarsOn(bool)";

        // TowedSonarsSetActive is confirmed present in the shipped assembly; it also tells us the
        // naming convention the radar switch almost certainly follows.
        TowedSonarSwitch.Member = MemberProbe.Method(unit, new[] { typeof(bool) },
            "TowedSonarsSetActive", "SetTowedSonarsActive", "TowedSonarSetActive", "TowedArraySetActive");
        TowedSonarSwitch.Tried = "TowedSonarsSetActive(bool), TowedSonarSetActive(bool)";

        SonarSwitch.Member = MemberProbe.Method(unit, new[] { typeof(bool) },
            "SonarsSetActive", "SetSonarsActive", "SonarSetActive", "SetSonarActive");
        SonarSwitch.Tried = "SonarsSetActive(bool), SonarSetActive(bool)";

        WaypointTask.Member = MemberProbe.Method(unit, null, "setWaypointTask", "SetWaypointTask");
        WaypointTask.Tried = "setWaypointTask(...)";

        HeadingOrder.Member = MemberProbe.Method(unit, null,
            "setHeading", "SetHeading", "set_Course", "SetCourse", "set_Heading", "SetDesiredHeading");
        HeadingOrder.Tried = "setHeading, SetCourse, SetDesiredHeading";

        // These two live on the unit's parameter block (Ship / Aircraft), not on ObjectBase.
        Type? parameterType = ParameterType();
        if (parameterType != null)
        {
            DecoyRelease.Member = MemberProbe.Value(parameterType, "decoySystems", "DecoySystems", "chaff", "Chaff");
            DecoyRelease.Tried = "decoySystems field on the unit's parameter block";

            MemberInfo? engines = MemberProbe.Value(parameterType, "engines", "engine", "Engines", "motors");
            if (engines != null)
            {
                Type? engineType = TypeOf(engines);
                if (engineType != null)
                {
                    EngineThrottle.Member = MemberProbe.Value(engineType, "throttle", "Throttle",
                        "targetSpeed", "setSpeed", "speed");
                }
                EngineThrottle.Tried = "throttle on the engine reached through " + engines.Name;
            }
            else
            {
                EngineThrottle.Tried = "no engine member on the unit's parameter block";
            }
        }
        else
        {
            EngineThrottle.Tried = "the unit's parameter block could not be identified";
            DecoyRelease.Tried = "the unit's parameter block could not be identified";
        }

        foreach (Capability capability in All) Report(capability);
    }

    /// <summary>One line per capability, for the log and capabilities.txt.</summary>
    internal static List<string> Describe()
    {
        var lines = new List<string>();
        foreach (Capability capability in All)
        {
            lines.Add((capability.Found ? "  ok   " : "  --   ") + capability.Id.PadRight(20)
                      + capability.Signature);
            lines.Add("         " + capability.Description);
            if (!capability.Found) lines.Add("         tried: " + capability.Tried);
        }
        return lines;
    }

    // --- actions -------------------------------------------------------------------------------

    /// <summary>Turn radars on or off. Returns false when the game has no such member.</summary>
    internal static bool SetRadars(ObjectBase unit, bool on)
    {
        if (unit == null || !RadarSwitch.Found) return false;
        return MemberProbe.Invoke(RadarSwitch.Member, unit, on);
    }

    internal static bool SetTowedSonar(ObjectBase unit, bool on)
    {
        if (unit == null || !TowedSonarSwitch.Found) return false;
        return MemberProbe.Invoke(TowedSonarSwitch.Member, unit, on);
    }

    internal static bool SetSonars(ObjectBase unit, bool on)
    {
        if (unit == null || !SonarSwitch.Found) return false;
        return MemberProbe.Invoke(SonarSwitch.Member, unit, on);
    }

    /// <summary>
    /// Order a speed by writing the engine throttle. The unit of the value is the game's, not ours,
    /// so the caller logs what it wrote and the first in-game test is what confirms it.
    /// </summary>
    internal static bool SetThrottle(ObjectBase unit, float value)
    {
        if (unit == null || !EngineThrottle.Found) return false;

        foreach (object engine in EnginesOf(unit))
        {
            if (MemberProbe.Write(EngineThrottle.Member, engine, value)) return true;
        }
        return false;
    }

    /// <summary>Every engine object on the unit, in the game's own order.</summary>
    internal static List<object> EnginesOf(ObjectBase unit)
    {
        var engines = new List<object>();
        object? parameters = ParametersOf(unit);
        if (parameters == null) return engines;

        MemberInfo? member = MemberProbe.Value(parameters.GetType(), "engines", "engine", "motors");
        object? value = MemberProbe.Read(member, parameters);
        if (value == null) return engines;

        if (value is IEnumerable sequence && value is not string)
        {
            foreach (object? item in sequence) if (item != null) engines.Add(item);
            return engines;
        }

        engines.Add(value);
        return engines;
    }

    /// <summary>The unit's parameter block (<c>_obp</c>), which owns weapons, sensors and engines.</summary>
    internal static object? ParametersOf(ObjectBase unit) =>
        MemberProbe.Read(MemberProbe.Value(typeof(ObjectBase), "_obp", "ObjectBaseParameters"), unit);

    private static MemberInfo? ObjectBaseParametersMember() =>
        MemberProbe.Value(typeof(ObjectBase), "_obp", "ObjectBaseParameters");

    private static Type? ParameterType()
    {
        MemberInfo? member = ObjectBaseParametersMember();
        return member == null ? null : TypeOf(member);
    }

    private static Type? TypeOf(MemberInfo member) => member switch
    {
        FieldInfo field => field.FieldType,
        PropertyInfo property => property.PropertyType,
        _ => null,
    };

    private static void Report(Capability capability)
    {
        try
        {
            ModLog.Info("Capability " + capability.Id + ": "
                        + (capability.Found ? capability.Signature : "not found (" + capability.Tried + ")"));
        }
        catch (Exception) { }
    }
}
