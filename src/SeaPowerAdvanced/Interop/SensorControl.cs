using System;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// The sensor, doctrine and countermeasure levers, named from the shipped assembly rather than
/// guessed. Every member here was read out of a real <c>Seapower-Scripts.dll</c> dump:
///
/// <code>
/// ObjectBase:
///   void EnableAllRadars(bool)              void DisableAllRadars()
///   void EnableAirSearchRadars(bool, bool)   void DisableAirSearchRadars(bool)
///   void EnableSurfaceSearchRadars(bool,bool) void DisableSurfaceSearchRadars(bool)
///   void EnableActiveSonars(bool,bool,bool)  void DisableActiveSonars(bool,bool,bool)
///   void DeployTowedSonars(bool,bool,bool)   void RetractTowedSonars(bool)
///   void TowedSonarsSetActive(bool)          void setEMCON(bool, bool)
///   bool HasRadar(Role)  HasAirSearchRadar()  HasSurfaceSearchRadar()
///   bool HasActiveSonar()  HasTowedSonar()
///   ReactiveProperty&lt;bool&gt; IsAirSearchRadarsOn / IsSurfaceSearchRadarsOn / IsActiveSonarsOn
///   ReactiveProperty&lt;bool&gt; Emcon / SprintAndDriftDoctrine / CavitationForbidden
///   void LaunchChaff(bool)      void LaunchNoisemaker(string)
///   virtual float CavitationOnsetKts()        virtual float getNoise()
/// </code>
///
/// Two findings from that dump are worth stating, because they change the plan:
///
/// 1. <b>Sprint-and-drift already exists</b> as <c>SprintAndDriftDoctrine</c>. The feature is not
///    "invent it" - it is "toggle it for a whole formation at once", which the game's own UI does
///    not offer (it ships formation toggles for EMCON, air search, surface search, towed and active
///    sonar, but not for this).
/// 2. <b>CavitationOnsetKts() is the game's own quiet-speed threshold.</b> A sprint phase above it
///    and a drift phase below it is the doctrine the game already models, so the mod does not need
///    to invent a speed rule - it reads the one the simulation uses.
/// </summary>
internal static class SensorControl
{
    private static readonly Dictionary<string, MemberInfo?> Cache = new();

    private static MemberInfo? Find(string key, Func<MemberInfo?> lookup)
    {
        if (Cache.TryGetValue(key, out MemberInfo? cached)) return cached;
        MemberInfo? member = null;
        try { member = lookup(); }
        catch (Exception) { }
        Cache[key] = member;
        return member;
    }

    private static Type Unit => typeof(ObjectBase);

    // --- finds ---------------------------------------------------------------------------------

    private static MemberInfo? RadarsAllOn => Find("radarsAllOn",
        () => MemberProbe.Method(Unit, 1, "EnableAllRadars"));

    private static MemberInfo? RadarsAllOff => Find("radarsAllOff",
        () => MemberProbe.Method(Unit, 0, "DisableAllRadars"));

    private static MemberInfo? AirSearchOn => Find("airOn",
        () => MemberProbe.Method(Unit, 2, "EnableAirSearchRadars"));

    private static MemberInfo? AirSearchOff => Find("airOff",
        () => MemberProbe.Method(Unit, 1, "DisableAirSearchRadars"));

    private static MemberInfo? SurfaceSearchOn => Find("surfaceOn",
        () => MemberProbe.Method(Unit, 2, "EnableSurfaceSearchRadars"));

    private static MemberInfo? SurfaceSearchOff => Find("surfaceOff",
        () => MemberProbe.Method(Unit, 1, "DisableSurfaceSearchRadars"));

    private static MemberInfo? ActiveSonarOn => Find("sonarOn",
        () => MemberProbe.Method(Unit, 3, "EnableActiveSonars"));

    private static MemberInfo? ActiveSonarOff => Find("sonarOff",
        () => MemberProbe.Method(Unit, 3, "DisableActiveSonars"));

    private static MemberInfo? TowedDeploy => Find("towedOn",
        () => MemberProbe.Method(Unit, 1, "TowedSonarsSetActive")
              ?? MemberProbe.Method(Unit, 1, "DeployTowedSonars"));

    private static MemberInfo? TowedRetract => Find("towedOff",
        () => MemberProbe.Method(Unit, 1, "TowedSonarsSetActive", "RetractTowedSonars"));

    private static MemberInfo? EmconSetter => Find("emcon",
        () => MemberProbe.Method(Unit, 2, "setEMCON"));

    private static MemberInfo? HasTowed
    {
        get { return Find("hasTowed", () => MemberProbe.Method(Unit, 0, "HasTowedSonar")); }
    }

    private static MemberInfo? HasActiveSonar
    {
        get { return Find("hasSonar", () => MemberProbe.Method(Unit, 0, "HasActiveSonar")); }
    }

    private static MemberInfo? HasRadar
    {
        get { return Find("hasRadar", () => MemberProbe.Method(Unit, 1, "HasRadar")); }
    }

    private static MemberInfo? Doctrine => Find("sprint",
        () => MemberProbe.Value(Unit, "SprintAndDriftDoctrine"));

    private static MemberInfo? Cavitation => Find("cavitation",
        () => MemberProbe.Method(Unit, 0, "CavitationOnsetKts"));

    /// <summary>Confirmed switches, for the log and for capabilities.txt.</summary>
    internal static List<string> Describe()
    {
        var lines = new List<string>
        {
            Line("radar master switch", RadarsAllOn, RadarsAllOff),
            Line("air search radars", AirSearchOn, AirSearchOff),
            Line("surface search radars", SurfaceSearchOn, SurfaceSearchOff),
            Line("active sonar", ActiveSonarOn, ActiveSonarOff),
            Line("towed sonar", TowedDeploy, TowedRetract),
            Line("EMCON", EmconSetter, null),
            Line("sprint-and-drift doctrine", Doctrine, null),
            Line("cavitation onset speed", Cavitation, null),
        };
        return lines;
    }

    private static string Line(string name, MemberInfo? on, MemberInfo? off)
    {
        string state = on != null || off != null ? "ok  " : " -- ";
        string detail = MemberProbe.Describe(on ?? off);
        return "  " + state + " " + name.PadRight(28) + detail;
    }

    // --- actions -------------------------------------------------------------------------------

    /// <summary>Turns every radar on this unit on or off, in one call.</summary>
    internal static bool SetAllRadars(ObjectBase unit, bool on)
    {
        if (unit == null) return false;
        return on
            ? MemberProbe.InvokeFilled(RadarsAllOn, unit, true)
            : MemberProbe.InvokeFilled(RadarsAllOff, unit, true);
    }

    /// <summary>Air-search radars only - a merchant-looking emission profile.</summary>
    internal static bool SetAirSearch(ObjectBase unit, bool on)
    {
        if (unit == null) return false;
        return on
            ? MemberProbe.InvokeFilled(AirSearchOn, unit, true)
            : MemberProbe.InvokeFilled(AirSearchOff, unit, true);
    }

    internal static bool SetSurfaceSearch(ObjectBase unit, bool on)
    {
        if (unit == null) return false;
        return on
            ? MemberProbe.InvokeFilled(SurfaceSearchOn, unit, true)
            : MemberProbe.InvokeFilled(SurfaceSearchOff, unit, true);
    }

    internal static bool SetActiveSonar(ObjectBase unit, bool on)
    {
        if (unit == null) return false;
        return on
            ? MemberProbe.InvokeFilled(ActiveSonarOn, unit, true)
            : MemberProbe.InvokeFilled(ActiveSonarOff, unit, true);
    }

    /// <summary>Deploys or retracts the towed array, for units that have one.</summary>
    internal static bool SetTowedSonar(ObjectBase unit, bool on)
    {
        if (unit == null) return false;
        MemberInfo? member = on ? TowedDeploy : TowedRetract;
        return MemberProbe.InvokeFilled(member, unit, on);
    }

    internal static bool SetEmcon(ObjectBase unit, bool on)
    {
        if (unit == null || EmconSetter == null) return false;
        return MemberProbe.InvokeFilled(EmconSetter, unit, on);
    }

    /// <summary>
    /// The game's own sprint-and-drift doctrine flag. This is a ReactiveProperty, so it is written
    /// rather than called - and it is the cheapest possible implementation of the feature, because
    /// the behaviour behind it is already written and tested by the developers.
    /// </summary>
    internal static bool SetSprintDoctrine(ObjectBase unit, bool on)
    {
        if (unit == null) return false;
        return MemberProbe.WriteReactiveBool(Doctrine, unit, on);
    }

    internal static bool? GetSprintDoctrine(ObjectBase unit)
    {
        if (unit == null) return null;
        return MemberProbe.ReadReactiveBool(Doctrine, unit);
    }

    /// <summary>The speed at which this unit starts to cavitate, in knots. Null if not readable.</summary>
    internal static float? CavitationOnsetKnots(ObjectBase unit)
    {
        if (unit == null || Cavitation == null) return null;
        try
        {
            object? result = ((MethodInfo)Cavitation).Invoke(unit, null);
            return result is float knots ? knots : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Whether the air-search radar is currently radiating, per the game's own state.</summary>
    internal static bool? GetAirSearchOn(ObjectBase unit)
    {
        if (unit == null) return null;
        MemberInfo? state = Find("airState",
            () => MemberProbe.Value(Unit, "IsAirSearchRadarsOn"));
        return MemberProbe.ReadReactiveBool(state, unit);
    }

    /// <summary>Whether the surface-search radar is currently radiating, per the game's own state.</summary>
    internal static bool? GetSurfaceSearchOn(ObjectBase unit)
    {
        if (unit == null) return null;
        MemberInfo? state = Find("surfaceState",
            () => MemberProbe.Value(Unit, "IsSurfaceSearchRadarsOn"));
        return MemberProbe.ReadReactiveBool(state, unit);
    }

    internal static bool HasTowedArray(ObjectBase unit) =>
        unit != null && MemberProbe.Invoke(HasTowed, unit);

    /// <summary>
    /// Whether this hull has a towed array, or null when the game does not say. A null is not a
    /// "no": the menu shows the entry, because hiding a feature the mod could not check is worse
    /// than showing one that might do nothing.
    /// </summary>
    internal static bool? TowedArrayFit(ObjectBase? unit)
    {
        if (unit == null) return null;
        return Query(HasTowed, unit);
    }

    /// <summary>Whether this hull has an air-search radar, or null when the game does not say.</summary>
    internal static bool? AirSearchFit(ObjectBase? unit)
    {
        if (unit == null) return null;

        try
        {
            MemberInfo? probe = Find("hasAirSearch",
                () => MemberProbe.Method(Unit, 0, "HasAirSearchRadar", "HasAirSearchRadars"));

            return probe == null ? null : Query(probe, unit);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Whether this hull has a surface-search radar, or null when the game does not say.</summary>
    internal static bool? SurfaceSearchFit(ObjectBase? unit)
    {
        if (unit == null) return null;

        try
        {
            MemberInfo? probe = Find("hasSurfaceSearch",
                () => MemberProbe.Method(Unit, 0, "HasSurfaceSearchRadar", "HasSurfaceSearchRadars"));

            return probe == null ? null : Query(probe, unit);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Whether this hull has an active sonar fit, or null when the game does not say.</summary>
    internal static bool? ActiveSonarFit(ObjectBase? unit)
    {
        if (unit == null) return null;
        return Query(HasActiveSonar, unit);
    }

    private static bool? Query(MemberInfo? member, object unit)
    {
        if (member is not MethodInfo method) return null;

        try
        {
            return method.Invoke(unit, null) as bool?;
        }
        catch (Exception) { return null; }
    }

    internal static bool HasActiveSonarFit(ObjectBase unit) =>
        unit != null && MemberProbe.Invoke(HasActiveSonar, unit);

    /// <summary>Releases chaff. Confirmed present as <c>LaunchChaff(bool)</c>.</summary>
    internal static bool LaunchChaff(ObjectBase unit)
    {
        MemberInfo? member = Find("chaff", () => MemberProbe.Method(Unit, 1, "LaunchChaff"));
        return MemberProbe.InvokeFilled(member, unit, true);
    }
}
