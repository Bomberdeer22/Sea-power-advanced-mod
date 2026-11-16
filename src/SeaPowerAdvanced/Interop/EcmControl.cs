using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// Electronic countermeasures, found rather than assumed.
///
/// The mod does not know what this build of the game calls its jammers, and naming a guessed member
/// is how four builds in a row failed to compile. So the whole ECM interface is discovered at
/// startup by looking for members whose names mention ECM or jamming, and then sorted by shape:
///
/// <list type="bullet">
/// <item>a method or writable bool that switches the set on or off;</item>
/// <item>a member that can hold the missile to be jammed - a field, a property, or a one-argument
///   method whose parameter accepts that missile instance;</item>
/// <item>a power or mode member, if this build has one.</item>
/// </list>
///
/// Everything it finds is written to <c>ecm-report.txt</c> on startup, and everything it does is
/// logged. That file is the ground truth from the installed game: if the members it lists are not
/// the ones the game really uses, the report says what they are instead, and the next build can use
/// those.
/// </summary>
internal static class EcmControl
{
    private static bool discovered;

    private static MethodInfo? switchOn;
    private static MethodInfo? switchOff;
    private static MemberInfo? switchMember;

    private static readonly List<MethodInfo> TargetSetters = new();
    private static readonly List<MemberInfo> TargetHolders = new();
    private static readonly List<MemberInfo> PowerMembers = new();
    private static readonly List<string> ObjectHits = new();
    private static readonly List<string> TypeHits = new();

    /// <summary>True when this build exposes something the mod can switch, aim or level.</summary>
    internal static bool Available
    {
        get
        {
            Discover();
            return HasSwitch || CanHoldLock || PowerMembers.Count > 0;
        }
    }

    private static bool HasSwitch => switchOn != null || switchOff != null || switchMember != null;

    /// <summary>The member used as the on/off switch, for capabilities.txt.</summary>
    internal static MemberInfo? SwitchMember =>
        (MemberInfo?)switchOn ?? switchOff ?? switchMember;

    /// <summary>The member used to hold a lock on a missile, for capabilities.txt.</summary>
    internal static MemberInfo? LockMember
    {
        get
        {
            if (TargetSetters.Count > 0) return TargetSetters[0];
            if (TargetHolders.Count > 0) return TargetHolders[0];
            return null;
        }
    }

    /// <summary>True when a lock can be put on a missile object specifically.</summary>
    internal static bool CanHoldLock
    {
        get
        {
            Discover();
            return TargetSetters.Count > 0 || TargetHolders.Count > 0;
        }
    }

    /// <summary>Run once at startup: find everything, log a summary.</summary>
    internal static void Discover()
    {
        if (discovered) return;
        discovered = true;

        try { ScanType(typeof(ObjectBase)); }
        catch (Exception error) { ModLog.Error("Could not scan the unit type for ECM members", error); }

        try { ScanTypes(typeof(ObjectBase).Assembly); }
        catch (Exception error) { ModLog.Error("Could not scan the game assembly for ECM types", error); }

        ModLog.Info("ECM: " + Summary());
    }

    private static void ScanType(Type type)
    {
        foreach (MemberInfo member in MemberProbe.All(type))
        {
            if (member is MethodInfo method && IsAccessor(method)) continue;
            if (!LooksLikeEcm(member.Name)) continue;

            ObjectHits.Add(MemberProbe.Describe(member));

            switch (member)
            {
                case MethodInfo call:
                    Classify(call);
                    break;

                case PropertyInfo property:
                    Classify(property.Name, property.PropertyType, property.CanWrite, member);
                    break;

                case FieldInfo field:
                    Classify(field.Name, field.FieldType, !field.IsInitOnly, member);
                    break;
            }
        }
    }

    private static void Classify(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();

        if (parameters.Length == 0)
        {
            if (Starts(method.Name, "Disable", "Stop", "Cease", "Off", "Reset")) { switchOff ??= method; }
            else if (Starts(method.Name, "Enable", "Start", "Begin", "On", "Activate", "Set"))
                switchOn ??= method;
            return;
        }

        if (parameters.Length != 1) return;

        Type parameter = parameters[0].ParameterType;

        if (parameter == typeof(bool))
        {
            if (Starts(method.Name, "Disable", "Stop", "Cease", "Off", "Reset")) switchOff ??= method;
            else switchOn ??= method;
            return;
        }

        if (parameter.IsEnum)
        {
            if (LooksLikePower(method.Name)) PowerMembers.Add(method);
            return;
        }

        if (parameter == typeof(float) || parameter == typeof(int) || parameter == typeof(double))
        {
            if (LooksLikePower(method.Name)) PowerMembers.Add(method);
            return;
        }

        // Anything else is a candidate for "point this at that": the missile instance decides, at the
        // moment of use, whether the parameter accepts it. No type name is involved.
        if (!parameter.IsValueType || Nullable.GetUnderlyingType(parameter) != null)
            TargetSetters.Add(method);
    }

    private static void Classify(string name, Type type, bool writable, MemberInfo member)
    {
        if (type == typeof(bool))
        {
            if (writable) switchMember ??= member;
            return;
        }

        if (type.IsEnum)
        {
            if (writable && LooksLikePower(name)) PowerMembers.Add(member);
            return;
        }

        if (type == typeof(float) || type == typeof(int) || type == typeof(double))
        {
            if (writable && LooksLikePower(name)) PowerMembers.Add(member);
            return;
        }

        if (writable && (!type.IsValueType || Nullable.GetUnderlyingType(type) != null))
            TargetHolders.Add(member);
    }

    /// <summary>Types in the game assembly whose names mention ECM, jamming or decoys.</summary>
    private static void ScanTypes(Assembly assembly)
    {
        Type?[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException partial) { types = partial.Types ?? Array.Empty<Type?>(); }
        catch (Exception) { return; }

        foreach (Type? type in types)
        {
            try
            {
                if (type == null) continue;

                string name = type.Name.ToLowerInvariant();
                if (!name.Contains("ecm") && !name.Contains("jam") && !name.Contains("decoy")) continue;

                TypeHits.Add(type.FullName ?? type.Name);
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Points the ship's ECM at one weapon and switches it on.
    ///
    /// <paramref name="radiate"/> decides whether the air-search radar is switched on first. The
    /// defensive feature does, because a ship defending itself wants a fire-control quality bearing
    /// on the missile and will pay for it. Offensive jamming against a contact does not: it runs on
    /// EMCON, silent, which is the whole point of using ECM instead of radar.
    /// </summary>
    internal static bool Engage(ObjectBase unit, object? weapon, bool radiate, out string note)
    {
        var steps = new List<string>();
        note = "";

        if (unit == null)
        {
            note = "no ship";
            return false;
        }

        Discover();

        // Nothing to drive means nothing to radiate for: a ship should not give its position away
        // for a jammer that cannot work.
        if (!HasSwitch && !CanHoldLock)
        {
            note = "this build exposes no ECM switch or jam target (see ecm-report.txt)";
            return false;
        }

        if (radiate)
        {
            if (SensorControl.SetAirSearch(unit, true)) steps.Add("air search on");
        }
        else
        {
            steps.Add("on EMCON (no radar)");
        }

        string aimed = "";
        if (weapon != null && Aim(unit, weapon, out aimed)) steps.Add(aimed);

        if (Raise(unit, out string power)) steps.Add(power);

        bool on = Switch(unit, true);
        if (on) steps.Add("jamming");

        if (steps.Count == 0)
        {
            note = "this build exposes no ECM the mod can drive (see ecm-report.txt)";
            return false;
        }

        note = string.Join(", ", steps);
        return on || aimed.Length > 0;
    }

    /// <summary>Switches the set off. The radars are the caller's business, not this method's.</summary>
    internal static bool StandDown(ObjectBase unit, out string note)
    {
        note = "";

        if (unit == null) return false;

        Discover();

        bool off = Switch(unit, false);
        note = off ? "jamming stopped" : "this build's ECM switch did not respond";
        return off;
    }

    // --- the three channels ---------------------------------------------------------------------

    private static bool Switch(ObjectBase unit, bool on)
    {
        MemberInfo? member = on ? (MemberInfo?)switchOn ?? switchMember : (MemberInfo?)switchOff ?? switchMember;
        if (member == null) return false;

        if (member is MethodInfo method)
        {
            try
            {
                if (method.GetParameters().Length == 0)
                {
                    method.Invoke(unit, null);
                    return true;
                }

                method.Invoke(unit, new object[] { on });
                return true;
            }
            catch (Exception error)
            {
                ModLog.Error("The ECM switch failed", error);
                return false;
            }
        }

        return MemberProbe.Write(member, unit, on);
    }

    private static bool Aim(ObjectBase unit, object weapon, out string note)
    {
        note = "";

        foreach (MethodInfo setter in TargetSetters)
        {
            ParameterInfo parameter = setter.GetParameters()[0];
            if (!parameter.ParameterType.IsInstanceOfType(weapon)) continue;

            try
            {
                setter.Invoke(unit, new[] { weapon });
                note = "lock on the missile via " + setter.Name;
                return true;
            }
            catch (Exception error)
            {
                ModLog.Error("An ECM target setter refused the missile", error);
            }
        }

        foreach (MemberInfo holder in TargetHolders)
        {
            Type type = holder is PropertyInfo property ? property.PropertyType : ((FieldInfo)holder).FieldType;
            if (!type.IsInstanceOfType(weapon)) continue;

            if (MemberProbe.Write(holder, unit, weapon))
            {
                note = "lock on the missile via " + holder.Name;
                return true;
            }
        }

        note = "no member of this build accepts a weapon as a jam target";
        return false;
    }

    /// <summary>
    /// Raises a power or mode control to its maximum, where this build has one the mod can trust.
    /// A number with units the mod does not know is left alone rather than set to a guess.
    /// </summary>
    private static bool Raise(ObjectBase unit, out string note)
    {
        note = "";

        foreach (MemberInfo member in PowerMembers)
        {
            try
            {
                if (member is MethodInfo method)
                {
                    Type parameter = method.GetParameters()[0].ParameterType;
                    object? value = parameter.IsEnum ? MaxValue(parameter)
                                    : parameter == typeof(int) ? 1
                                    : parameter == typeof(float) ? 1f : 1.0;

                    if (value == null) continue;

                    method.Invoke(unit, new[] { value });
                    note = "power to maximum via " + method.Name;
                    return true;
                }

                Type kind = member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
                object? current = MemberProbe.Read(member, unit);

                if (kind.IsEnum)
                {
                    object? value = MaxValue(kind);
                    if (value != null && MemberProbe.Write(member, unit, value))
                    {
                        note = "power to " + value + " via " + member.Name;
                        return true;
                    }
                    continue;
                }

                // A level already above 1 has units the mod cannot know, so it is left as it is.
                if (kind == typeof(float) && current is float level && level <= 1f)
                {
                    if (MemberProbe.Write(member, unit, 1f))
                    {
                        note = "power to maximum via " + member.Name;
                        return true;
                    }
                }
                else if (kind == typeof(int) && current is int count && count <= 1)
                {
                    if (MemberProbe.Write(member, unit, 1))
                    {
                        note = "power to maximum via " + member.Name;
                        return true;
                    }
                }
            }
            catch (Exception) { }
        }

        return false;
    }

    private static object? MaxValue(Type type)
    {
        try
        {
            foreach (string name in Enum.GetNames(type))
            {
                string lower = name.ToLowerInvariant();
                if (lower.Contains("max") || lower.Contains("high") || lower.Contains("full")
                    || lower.Contains("strong") || lower.Contains("active"))
                    return Enum.Parse(type, name);
            }
        }
        catch (Exception) { }
        return null;
    }

    // --- naming ----------------------------------------------------------------------------------

    private static bool LooksLikeEcm(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower.Contains("ecm") || lower.Contains("jam");
    }

    private static bool LooksLikePower(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower.Contains("power") || lower.Contains("gain") || lower.Contains("strength")
               || lower.Contains("level") || lower.Contains("mode");
    }

    private static bool Starts(string name, params string[] prefixes)
    {
        foreach (string prefix in prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsAccessor(MethodInfo method) =>
        method.IsSpecialName && (method.Name.StartsWith("get_") || method.Name.StartsWith("set_"));

    /// <summary>One line for the log and for capabilities.txt.</summary>
    internal static string Summary()
    {
        if (!Available) return "no ECM member found in this build (see ecm-report.txt)";

        var parts = new List<string>();
        if (switchOn != null) parts.Add("on=" + switchOn.Name);
        if (switchOff != null) parts.Add("off=" + switchOff.Name);
        if (switchMember != null) parts.Add("switch=" + switchMember.Name);
        if (CanHoldLock) parts.Add("lock=yes");
        if (PowerMembers.Count > 0) parts.Add("power=" + PowerMembers[0].Name);

        return string.Join("  ", parts);
    }

    /// <summary>The lines the menu and the report show, so the player can see what was found.</summary>
    internal static List<string> Describe()
    {
        Discover();

        var lines = new List<string>();
        if (!Available)
        {
            lines.Add("  --   ECM                          no ECM member found in this build");
            return lines;
        }

        lines.Add("  " + (HasSwitch ? "ok  " : "--  ") + " ECM switch                   "
                  + (switchOn?.Name ?? switchOff?.Name ?? switchMember?.Name ?? "<none>"));
        lines.Add("  " + (CanHoldLock ? "ok  " : "--  ") + " ECM lock on a missile        "
                  + (TargetSetters.Count > 0 ? TargetSetters[0].Name
                     : TargetHolders.Count > 0 ? TargetHolders[0].Name : "<none>"));
        lines.Add("  " + (PowerMembers.Count > 0 ? "ok  " : "--  ") + " ECM power or mode           "
                  + (PowerMembers.Count > 0 ? PowerMembers[0].Name : "<none>"));
        return lines;
    }

    /// <summary>
    /// Writes <c>ecm-report.txt</c>: every ECM-ish member and type this install exposes, and what the
    /// mod does with each. Written on startup, so it exists before the feature is ever used.
    /// </summary>
    internal static void WriteReport()
    {
        Discover();

        try
        {
            var text = new StringBuilder();
            text.AppendLine("Sea Power Advanced - ECM report");
            text.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            + "   mod " + ModInfo.Version);
            text.AppendLine();
            text.AppendLine("What the mod discovered");
            text.AppendLine("-----------------------");
            foreach (string line in Describe()) text.AppendLine(line);
            text.AppendLine();
            text.AppendLine("ECM members found on " + typeof(ObjectBase).FullName);
            text.AppendLine("----------------------------------------");
            if (ObjectHits.Count == 0)
            {
                text.AppendLine("  (none - either this build names its ECM something the scan does not");
                text.AppendLine("   recognise, or jamming is modelled somewhere other than the unit type)");
            }
            foreach (string hit in ObjectHits) text.AppendLine("  " + hit);
            text.AppendLine();
            text.AppendLine("Types in the game assembly mentioning ecm, jam or decoy");
            text.AppendLine("-------------------------------------------------------");
            if (TypeHits.Count == 0) text.AppendLine("  (none)");
            foreach (string hit in TypeHits) text.AppendLine("  " + hit);
            text.AppendLine();
            text.AppendLine("What the mod does with it");
            text.AppendLine("-------------------------");
            text.AppendLine("  On a vampire: air-search radar on (that is the cost - a lock needs a");
            text.AppendLine("  fire-control emission), the missile handed to the jammer as its target, the");
            text.AppendLine("  switch thrown, and the target re-pointed every quarter second for as long as");
            text.AppendLine("  the missile is tracked. Power is raised only where this build has a power or");
            text.AppendLine("  mode member whose units the mod can trust.");
            text.AppendLine();
            text.AppendLine("If the members above are not the ones the game really uses, send this file:");
            text.AppendLine("it is read from the installed game, so it is ground truth rather than a guess.");

            string path = ModFolders.WriteText("ecm-report.txt", text.ToString());
            ModLog.Info("ECM report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Could not write ecm-report.txt", error);
        }
    }
}
