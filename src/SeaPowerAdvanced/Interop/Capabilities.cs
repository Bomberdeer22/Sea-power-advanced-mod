using System;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// The registry of game levers the mod uses, with what was found in this build.
///
/// The names here are no longer guesses. They were read out of a real
/// <c>Seapower-Scripts.dll</c> dump, which changed the plan in two places:
///
/// * the radar family is <c>EnableAllRadars(bool)</c> / <c>DisableAllRadars()</c> and friends, not
///   the <c>RadarsSetActive(bool)</c> I had guessed - which is exactly why the lookup is by
///   reflection with several candidate names;
/// * <c>SprintAndDriftDoctrine</c> already exists as a per-unit flag, so sprint-and-drift is a
///   fleet-wide toggle rather than a new behaviour.
///
/// Anything genuinely absent is held in <see cref="UnitState"/> instead. That is the rule: use the
/// game's lever when there is one, keep our own state when there is not, and never pretend the
/// second is the first.
/// </summary>
internal static class Capabilities
{
    internal static Capability RadarSwitch { get; } =
        new("radar switch", "turn a unit's radars on or off from code");

    internal static Capability TowedSonarSwitch { get; } =
        new("towed sonar switch", "deploy or retract the towed array");

    internal static Capability SonarSwitch { get; } =
        new("active sonar switch", "turn the active sonars on or off");

    internal static Capability Emcon { get; } =
        new("EMCON", "set the unit's emission control state");

    internal static Capability Doctrine { get; } =
        new("sprint-and-drift", "the game's own sprint-and-drift doctrine flag");

    internal static Capability Chaff { get; } =
        new("chaff", "release chaff from code");

    internal static Capability Reload { get; } =
        new("reload timer", "read and shorten a mount's remaining reload");

    internal static Capability Incoming { get; } =
        new("incoming weapons", "see weapons inbound to a unit");

    internal static Capability Waypoints { get; } =
        new("waypoints", "push a waypoint onto a unit (helo patterns, threat-axis turn)");

    internal static Capability FlightDeck { get; } =
        new("flight deck", "launch an aircraft from a ship's deck");

    private static readonly List<Capability> All = new()
    {
        RadarSwitch, TowedSonarSwitch, SonarSwitch, Emcon, Doctrine, Chaff, Reload, Incoming,
        Waypoints, FlightDeck,
    };

    private static bool discovered;

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
    /// The unit's own parameter block: hull data, weapon systems and signatures.
    ///
    /// This helper went missing in a rewrite and three features kept calling it - which the compiler
    /// reports as CS0117, "Capabilities does not contain a definition for ParametersOf", in files that
    /// look perfectly fine. It is back, and <c>tools/check-source.py</c> now looks for member
    /// references like this one so a rewrite cannot silently take it again.
    /// </summary>
    internal static ObjectBaseParameters? ParametersOf(ObjectBase unit) => unit?._obp;

    /// <summary>Looks everything up once, and logs each result.</summary>
    internal static void Discover()
    {
        if (discovered) return;
        discovered = true;

        Type unit = typeof(ObjectBase);

        Bind(RadarSwitch,
            MemberProbe.Method(unit, 0, "DisableAllRadars"),
            "EnableAllRadars(bool) / DisableAllRadars()");

        Bind(TowedSonarSwitch,
            MemberProbe.Method(unit, 1, "TowedSonarsSetActive", "DeployTowedSonars", "RetractTowedSonars"),
            "TowedSonarsSetActive(bool)");

        Bind(SonarSwitch,
            MemberProbe.Method(unit, 3, "EnableActiveSonars", "DisableActiveSonars"),
            "EnableActiveSonars(bool,bool,bool)");

        Bind(Emcon,
            MemberProbe.Method(unit, 2, "setEMCON"),
            "setEMCON(bool,bool)");

        Bind(Doctrine,
            MemberProbe.Value(unit, "SprintAndDriftDoctrine"),
            "SprintAndDriftDoctrine (ReactiveProperty<bool>)");

        Bind(Chaff,
            MemberProbe.Method(unit, 1, "LaunchChaff"),
            "LaunchChaff(bool)");

        Type mount = typeof(WeaponSystem);
        Bind(Reload,
            MemberProbe.Value(mount, "_remainingReloadTime", "RemainingReloadTime"),
            "WeaponSystem._remainingReloadTime");

        Bind(Incoming,
            MemberProbe.Value(unit, "_incomingWeapons", "_dangerousMissiles"),
            "ObjectBase._incomingWeapons / _dangerousMissiles");

        Bind(Waypoints,
            MemberProbe.Method(unit, 3, "setWaypointTask"),
            "setWaypointTask(GeoPosition, string, an enum this project cannot name)");

        Bind(FlightDeck,
            MemberProbe.Method(unit, null, "FlightDeckLaunchUnit"),
            "FlightDeckLaunchUnit(ObjectType, ..., UnitRoles, MissionType, ...)");

        foreach (Capability capability in All) Report(capability);
    }

    private static void Bind(Capability capability, MemberInfo? member, string tried)
    {
        capability.Member = member;
        capability.Tried = tried;
    }

    /// <summary>Readable report lines, for the log and capabilities.txt.</summary>
    internal static List<string> Describe()
    {
        var lines = new List<string>();
        foreach (Capability capability in All)
        {
            lines.Add((capability.Found ? "  ok   " : "  --   ") + capability.Id.PadRight(18)
                      + capability.Signature);
            lines.Add("         " + capability.Description);
            if (!capability.Found) lines.Add("         tried: " + capability.Tried);
        }
        return lines;
    }

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
