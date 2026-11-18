using System;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// What a unit's sensors and signatures actually are, and how to take them away.
///
/// This is the reading half of the focused jamming mode. It answers three questions about any unit -
/// hostile or friendly, the same type either way - without a single game type named in code:
///
/// <list type="bullet">
/// <item>which sensor families it has (air search, surface search, sonar, towed array, ESM, its own
///   jammer), and whether each is radiating right now;</item>
/// <item>what its hull signature says it looks like - read from its parameter block, read-only;</item>
/// <item>whether each of those can be switched off by the mod, and whether the switch worked.</item>
/// </list>
///
/// The last point is the one that matters, and it is why the answer is a list with a verdict per
/// sensor rather than a single boolean. "Fully blind" is only true if every sensor was actually
/// silenced; if one could not be reached, this says which, and the menu says so too. That is the
/// difference between a feature and a lie.
/// </summary>
internal static class SensorSet
{
    /// <summary>One sensor family on a unit: what it is, whether it is up, and what happened to it.</summary>
    internal sealed class Reading
    {
        internal Reading(string name, bool? radiating)
        {
            Name = name;
            Radiating = radiating;
            Verdict = "";
        }

        internal string Name { get; }

        /// <summary>True/false when the game exposes its state, null when it does not.</summary>
        internal bool? Radiating { get; }

        /// <summary>What the mod did about it: silenced, could not be reached, already quiet.</summary>
        internal string Verdict { get; set; }

        internal bool Silenced => Verdict.StartsWith("silenced", StringComparison.Ordinal);

        internal bool Stubborn => Verdict.StartsWith("STILL", StringComparison.Ordinal);
    }

    /// <summary>Everything this unit is radiating with, as far as the game will say.</summary>
    internal static List<Reading> Read(ObjectBase? unit)
    {
        var readings = new List<Reading>();
        if (unit == null) return readings;

        readings.Add(new Reading("air search radar", SensorControl.GetAirSearchOn(unit)));
        readings.Add(new Reading("surface search radar", SensorControl.GetSurfaceSearchOn(unit)));

        if (SensorControl.HasTowedArray(unit)) readings.Add(new Reading("towed array", null));
        if (SensorControl.HasActiveSonarFit(unit)) readings.Add(new Reading("active sonar", null));

        foreach (Reading reading in ExtraSwitches(unit)) readings.Add(reading);

        return readings;
    }

    /// <summary>
    /// Forces every sensor this unit has to stop, and reports per sensor what happened.
    ///
    /// Called every quarter second while a lock is held, not once: the game's own crew can switch
    /// its radar back on, and the point of a sustained jam is that it is sustained. Each call
    /// re-asserts the whole set.
    /// </summary>
    internal static List<Reading> Silence(ObjectBase? unit)
    {
        var readings = new List<Reading>();
        if (unit == null) return readings;

        // --- radar -------------------------------------------------------------------------
        bool? airWas = SensorControl.GetAirSearchOn(unit);
        bool? surfaceWas = SensorControl.GetSurfaceSearchOn(unit);

        bool allOff = SensorControl.SetAllRadars(unit, false);
        SensorControl.SetAirSearch(unit, false);
        SensorControl.SetSurfaceSearch(unit, false);

        bool? airNow = SensorControl.GetAirSearchOn(unit);
        bool? surfaceNow = SensorControl.GetSurfaceSearchOn(unit);

        readings.Add(Verdict("air search radar", airWas, airNow, allOff));
        readings.Add(Verdict("surface search radar", surfaceWas, surfaceNow, allOff));

        // --- sonar -------------------------------------------------------------------------
        if (SensorControl.HasTowedArray(unit))
        {
            bool ok = SensorControl.SetTowedSonar(unit, false);
            readings.Add(new Reading("towed array", null)
            {
                Verdict = ok ? "silenced" : "STILL deployed - this build's towed-array switch refused",
            });
        }

        if (SensorControl.HasActiveSonarFit(unit))
        {
            bool ok = SensorControl.SetActiveSonar(unit, false);
            readings.Add(new Reading("active sonar", null)
            {
                Verdict = ok ? "silenced" : "STILL pinging - this build's sonar switch refused",
            });
        }

        // --- its own electronic warfare fit -------------------------------------------------
        foreach (Reading reading in SilenceExtraSwitches(unit)) readings.Add(reading);

        return readings;
    }

    /// <summary>
    /// The unit's hull signature, as the game's own parameter block states it. Read-only: this is
    /// what the mod is "reading off" the contact, not something it changes.
    /// </summary>
    internal static string SignatureOf(ObjectBase? unit)
    {
        if (unit == null) return "<none>";

        try
        {
            object? parameters = Capabilities.ParametersOf(unit);
            if (parameters == null) return "<no parameter block>";

            foreach (string candidate in new[] { "signature", "Signature", "rcs", "RCS",
                                                 "RadarCrossSection", "radarCrossSection" })
            {
                MemberInfo? member = MemberProbe.Value(parameters.GetType(), candidate);
                if (member == null) continue;

                object? value = MemberProbe.Read(member, parameters);
                if (value != null) return value.ToString() ?? "<empty>";
            }
        }
        catch (Exception) { }

        return "<not exposed>";
    }

    /// <summary>
    /// One line per sensor, for the log and for <c>offensive-ecm.txt</c>. Written when a lock is
    /// taken and whenever the picture changes, not every tick.
    /// </summary>
    internal static string Describe(List<Reading> readings)
    {
        if (readings.Count == 0) return "no sensors readable";

        var parts = new List<string>();
        foreach (Reading reading in readings)
        {
            string state = reading.Verdict.Length > 0
                ? reading.Verdict
                : reading.Radiating == true ? "radiating"
                : reading.Radiating == false ? "quiet"
                : "state not exposed";

            parts.Add(reading.Name + ": " + state);
        }
        return string.Join("; ", parts);
    }

    /// <summary>True when every reading was silenced or was already quiet, and none is stubborn.</summary>
    internal static bool FullyBlind(List<Reading> readings)
    {
        if (readings.Count == 0) return false;

        foreach (Reading reading in readings)
        {
            if (reading.Stubborn) return false;
        }
        return true;
    }

    private static Reading Verdict(string name, bool? was, bool? now, bool switchWorked)
    {
        var reading = new Reading(name, now ?? was);

        if (now == false) reading.Verdict = was == true ? "silenced" : "already quiet";
        else if (now == true) reading.Verdict = "STILL radiating - the switch reported success but the state did not change";
        else reading.Verdict = switchWorked ? "silenced (state not exposed to confirm)" : "could not be reached";

        return reading;
    }

    // --- ESM and the target's own jammer ---------------------------------------------------

    /// <summary>
    /// Sensor families that are not part of the documented radar/sonar set: electronic support
    /// measures, and the target's own jammer. Found by name, because no dump has them.
    /// </summary>
    private static List<Reading> ExtraSwitches(ObjectBase unit)
    {
        var readings = new List<Reading>();

        foreach (MemberInfo member in ExtraMembers())
        {
            bool? state = null;
            try
            {
                if (member is PropertyInfo property && property.PropertyType == typeof(bool) && property.CanRead)
                    state = MemberProbe.Read(property, unit) as bool?;
            }
            catch (Exception) { }

            readings.Add(new Reading(Label(member.Name), state));
        }

        return readings;
    }

    private static List<Reading> SilenceExtraSwitches(ObjectBase unit)
    {
        var readings = new List<Reading>();

        foreach (MemberInfo member in ExtraMembers())
        {
            bool ok = false;

            try
            {
                switch (member)
                {
                    case MethodInfo call when call.GetParameters().Length == 0
                                              && call.Name.StartsWith("Disable", StringComparison.OrdinalIgnoreCase):
                        call.Invoke(unit, null);
                        ok = true;
                        break;

                    case MethodInfo call when call.GetParameters().Length == 1
                                              && call.GetParameters()[0].ParameterType == typeof(bool):
                        call.Invoke(unit, new object[] { false });
                        ok = true;
                        break;

                    case PropertyInfo property when property.PropertyType == typeof(bool) && property.CanWrite:
                        ok = MemberProbe.Write(property, unit, false);
                        break;

                    case FieldInfo field when field.FieldType == typeof(bool) && !field.IsInitOnly:
                        ok = MemberProbe.Write(field, unit, false);
                        break;
                }
            }
            catch (Exception) { ok = false; }

            var reading = new Reading(Label(member.Name), null);
            reading.Verdict = ok ? "silenced" : "STILL active - no switch the mod can reach";
            readings.Add(reading);
        }

        return readings;
    }

    private static List<MemberInfo>? extraMembers;

    /// <summary>
    /// The unit members that are ESM or a jammer, resolved once. This runs on a timer while a lock is
    /// held, so walking the whole member list every time would be wasteful - it is scanned once and
    /// the result reused for the life of the process.
    /// </summary>
    private static List<MemberInfo> ExtraMembers()
    {
        if (extraMembers != null) return extraMembers;

        var found = new List<MemberInfo>();

        try
        {
            foreach (MemberInfo member in MemberProbe.All(typeof(ObjectBase)))
            {
                if (!IsExtraSensor(member.Name)) continue;
                if (member is MethodInfo method && IsAccessor(method)) continue;
                if (!Switchable(member)) continue;

                found.Add(member);
            }
        }
        catch (Exception) { }

        extraMembers = found;
        return extraMembers;
    }

    /// <summary>
    /// Whether this member is something the mod can actually switch. A read-only strength field that
    /// happens to be named "jammerPower" is not a sensor switch, and counting it would make every
    /// target permanently "not all sensors off" for no reason.
    /// </summary>
    private static bool Switchable(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType == typeof(bool) && property.CanWrite,
        FieldInfo field => field.FieldType == typeof(bool) && !field.IsInitOnly,
        MethodInfo method => method.GetParameters().Length == 0
                             || (method.GetParameters().Length == 1
                                 && method.GetParameters()[0].ParameterType == typeof(bool)),
        _ => false,
    };

    /// <summary>Electronic support measures and the unit's own jammer - not its radars or sonars.</summary>
    private static bool IsExtraSensor(string name)
    {
        string lower = name.ToLowerInvariant();

        if (lower.Contains("radar") || lower.Contains("sonar")) return false;
        return lower.Contains("esm") || lower.Contains("ecm") || lower.Contains("jam")
               || lower.Contains("intercept") || lower.Contains("rwr");
    }

    private static string Label(string memberName)
    {
        string lower = memberName.ToLowerInvariant();
        if (lower.Contains("esm") || lower.Contains("intercept") || lower.Contains("rwr"))
            return "ESM / intercept receiver";
        if (lower.Contains("ecm") || lower.Contains("jam")) return "its own jammer";
        return memberName;
    }

    private static bool IsAccessor(MethodInfo method) =>
        method.IsSpecialName && (method.Name.StartsWith("get_") || method.Name.StartsWith("set_"));
}
