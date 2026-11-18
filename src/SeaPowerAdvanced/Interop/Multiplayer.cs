using System;
using System.Reflection;
using System.Text;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// What the multiplayer mod expects of us, and what we may still do.
///
/// The multiplayer mod (<c>com.seapowermultiplayer.plugin</c>, "SeapowerMultiplayer") makes one
/// machine the authority for a piece of world state and replicates the rest. Three of its rules land
/// squarely on this mod, and every one of them is a thing that would otherwise look like "the
/// feature is broken in multiplayer":
///
/// <list type="number">
/// <item><b>Radar switches go through it, and it throttles them.</b> It hooks
///   <c>SensorSystem.Enable/Disable</c> - the same calls the game's own radar buttons make, and the
///   same ones <see cref="SensorControl.SetAirSearch"/> makes - and drops a repeat toggle on the
///   same unit and the same radar group inside <b>ten seconds</b>. A refused toggle does not just go
///   unforwarded: the local call is skipped as well, so the switch does not move at all. Radar blink
///   at the default six seconds would therefore have every second flip eaten, and the radar would sit
///   stuck in whichever state the throttle left it in.</item>
/// <item><b>The client does not decide its own emissions.</b> On the receiving end it re-imposes the
///   host's radar picture every frame, so a switch written locally is reverted within one. That is
///   correct for units the client only renders, and wrong for a blink the player ordered.</item>
/// <item><b>Jamming is an order, not a member write.</b> The game's own jam order
///   (<c>ObjectBase.setOrder(Order.Type.Jam, target, ...)</c>) is what the multiplayer mod forwards to
///   the host, which then runs the same jam task single-player does. Writing a jammer's members
///   directly - which is how <see cref="EcmControl"/> drives this build - never leaves the machine.</item>
/// </list>
///
/// So this file answers two questions and nothing else: <i>is a session running, and on which side of
/// it am I</i>, and <i>given that, what is this mod allowed to promise</i>. It reaches the multiplayer
/// mod by reflection rather than by referencing it, because the mod has to keep building and working
/// for players who do not have it.
///
/// Everything found here is written to <c>multiplayer.txt</c>, including the rules applied.
/// </summary>
internal static class Multiplayer
{
    /// <summary>The multiplayer mod's BepInEx GUID. Its assembly is found by type name below, so a
    /// rename of the assembly does not break detection.</summary>
    internal const string PluginGuid = "com.seapowermultiplayer.plugin";

    /// <summary>
    /// The floor its order deduplicator puts on a radar toggle, plus a margin. Its own constant is
    /// <c>10f</c> per (unit, radar group); a blink that lands exactly on it races the comparison, so
    /// the mod waits a little longer than the limit rather than exactly as long as it.
    /// </summary>
    internal const float ToggleFloorSeconds = 12f;

    private static bool scanned;
    private static bool present;
    private static string version = "not loaded";

    private static Type? pluginType;
    private static Type? networkType;

    private static MemberInfo? instanceMember;
    private static MemberInfo? hostFlagMember;
    private static MemberInfo? networkInstanceMember;
    private static MemberInfo? establishedMember;

    /// <summary>Something the player should be told, once: a switch the session refused, an ECM
    /// order it could not carry. Written to <c>multiplayer.txt</c>.</summary>
    private static string lastNote = "nothing yet";

    internal static bool Present
    {
        get
        {
            Scan();
            return present;
        }
    }

    internal static string Version
    {
        get
        {
            Scan();
            return version;
        }
    }

    /// <summary>A session is up: host or client, connected to another machine.</summary>
    internal static bool InSession
    {
        get
        {
            Scan();
            if (!present) return false;

            object? network = ReadStatic(networkType, networkInstanceMember);
            if (network == null) return false;

            return ReadBool(network, establishedMember);
        }
    }

    /// <summary>
    /// This machine runs the simulation, so its word is final: single player, or the host of a
    /// session. The automatic features are allowed to write world state here.
    /// </summary>
    internal static bool IsAuthority
    {
        get
        {
            Scan();
            if (!present) return true;    // no session: this machine is the only one

            object? plugin = ReadStatic(pluginType, instanceMember);
            if (plugin == null) return true;

            object? flag = MemberProbe.Read(hostFlagMember, plugin);
            if (flag == null) return true;

            // The setting is a BepInEx ConfigEntry<bool>, not a bool: the value is one level in.
            MemberInfo? value = MemberProbe.Value(flag.GetType(), "Value");
            if (value != null && MemberProbe.Read(value, flag) is bool configured) return configured;

            return flag is bool direct && direct;
        }
    }

    /// <summary>Connected to a host that decides the shared picture: our writes are not the last
    /// word on anything.</summary>
    internal static bool IsClient => InSession && !IsAuthority;

    /// <summary>How long a radar set has to stay in one state, so the session does not refuse the
    /// next flip. Single player: no floor beyond the setting's own minimum.</summary>
    internal static float RadarToggleFloorSeconds => InSession ? ToggleFloorSeconds : 2f;

    /// <summary>
    /// The game's own jam order, for a machine whose local member writes are not authoritative.
    ///
    /// This is the route the multiplayer mod does forward: it hooks
    /// <c>setOrder(Order.Type.Jam, ...)</c>, sends it upstream, and the host runs the same jam task it
    /// would run in single player. The alternative - writing <c>_associatedTarget</c> and friends the
    /// way <see cref="EcmControl"/> does - reaches <i>this</i> machine's copy of the target and nothing
    /// else, which in a session is a ship the other side is simulating.
    /// </summary>
    internal static bool Jam(ObjectBase? ship, object? target, out string note)
    {
        note = "";

        if (ship == null || target == null)
        {
            note = "no jammer or no target";
            return false;
        }

        try
        {
            MethodInfo? order = JamOrder();
            if (order == null)
            {
                note = "this build has no jam order the mod can name (see multiplayer.txt)";
                return false;
            }

            ParameterInfo[] parameters = order.GetParameters();

            // A ship and a missile are both ObjectBase, so this is normally true; if this build's
            // order takes something narrower, say so rather than throwing inside the invoke.
            if (!parameters[1].ParameterType.IsInstanceOfType(target))
            {
                note = "the jam order takes a " + parameters[1].ParameterType.Name
                       + ", not a " + target.GetType().Name;
                ModLog.Warn("Multiplayer: " + note);
                return false;
            }

            object? type = JamValue(parameters[0].ParameterType);
            if (type == null)
            {
                note = "this build's order enum has no jam value (see multiplayer.txt)";
                return false;
            }

            order.Invoke(ship, new object?[] { type, target, true });
            note = "jam order sent to the host";
            return true;
        }
        catch (Exception error)
        {
            note = "the jam order failed - " + error.Message;
            ModLog.Error("Multiplayer: could not send a jam order", error);
            return false;
        }
    }

    /// <summary>
    /// Why an automatic feature is standing down here, or null when it is allowed to run.
    /// <paramref name="what"/> is the feature's name, for the sentence.
    /// </summary>
    internal static string? RefusedTo(string what)
    {
        if (!IsClient) return null;

        return what + " is switched at the host in a multiplayer session: this machine does not "
               + "decide when a ship radiates, and anything written here is reverted by the host's "
               + "picture within a frame.";
    }

    /// <summary>
    /// Records a one-line outcome for the report and writes it out. The caller logs the same thing in
    /// its own words, so this deliberately does not log twice.
    /// </summary>
    internal static void Note(string note)
    {
        lastNote = note;
        WriteReport();
    }

    /// <summary>One line for the log and the menu: who has the last word here.</summary>
    internal static string Describe()
    {
        if (!Present) return "no multiplayer mod";

        string role = !InSession ? "installed, no session up"
                    : IsAuthority ? "hosting - this machine has the last word"
                    : "connected to a host - the host has the last word";

        return "multimod " + Version + ", " + role;
    }

    /// <summary>Writes <c>multiplayer.txt</c>: what was found, and the rules that follow from it.</summary>
    internal static void WriteReport()
    {
        try
        {
            var text = new StringBuilder();

            text.AppendLine("Sea Power Advanced - multiplayer compatibility");
            text.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            + "   mod " + ModInfo.Version);
            text.AppendLine();
            text.AppendLine("The multiplayer mod");
            text.AppendLine("--------------------");
            text.AppendLine("  present: " + (Present ? "yes" : "no") + "   (" + PluginGuid + ")");
            text.AppendLine("  version: " + Version);
            text.AppendLine("  session: " + (InSession ? "up" : "not up"));
            text.AppendLine("  this machine: " + (IsAuthority ? "authority (single player or host)"
                                                               : "client"));
            text.AppendLine("  state:   " + Describe());
            text.AppendLine();

            text.AppendLine("What this mod does about it");
            text.AppendLine("---------------------------");
            if (InSession)
            {
                text.AppendLine("  radar blink:      half-cycle at least " + ToggleFloorSeconds
                                + "s, so the session does not refuse the flip");
                text.AppendLine("                    (the multiplayer mod drops a repeat radar toggle on the");
                text.AppendLine("                    same unit and radar group inside 10 seconds, and a");
                text.AppendLine("                    refused toggle does not move the switch locally either)");
            }
            else
            {
                text.AppendLine("  radar blink:      unchanged - with no session up there is nothing");
                text.AppendLine("                    throttling a radar switch, so Features/BlinkSeconds is");
                text.AppendLine("                    used as written");
            }

            if (IsClient)
            {
                text.AppendLine("  radar blink:      the host still has the final say on whether a");
                text.AppendLine("                    radar radiates. If a flip does not take, the blink");
                text.AppendLine("                    stops itself and says so instead of pretending.");
                text.AppendLine("  defensive ECM:    stands down - missile defence is decided by the");
                text.AppendLine("                    host, and a jam written here is reverted by it.");
                text.AppendLine("  offensive ECM:    sent as the game's own jam order, which the");
                text.AppendLine("                    multiplayer mod forwards. The signature lock's");
                text.AppendLine("                    switch-off assertion is not attempted: the target is");
                text.AppendLine("                    simulated on the host, so there is nothing to switch");
                text.AppendLine("                    off from here. There is no 'stop jamming' order to");
                text.AppendLine("                    send, so a jam started from a client runs until the");
                text.AppendLine("                    host's jam task ends.");
            }
            else
            {
                text.AppendLine("  everything:       runs here and replicates outward, because this");
                text.AppendLine("                    machine simulates the world. There is nothing to");
                text.AppendLine("                    adapt.");
            }

            text.AppendLine("  ECM cones:        read-only, drawn from whatever jamming the game");
            text.AppendLine("                    reports on this machine - including jamming the");
            text.AppendLine("                    session replicated in from the other side.");
            text.AppendLine("  menus, hatches,   orders through the game's own interfaces, which the");
            text.AppendLine("  salvo, readiness: multiplayer mod already forwards.");
            text.AppendLine();
            text.AppendLine("Last thing worth knowing");
            text.AppendLine("------------------------");
            text.AppendLine("  " + lastNote);

            ModFolders.WriteText("multiplayer.txt", text.ToString());
        }
        catch (Exception error)
        {
            ModLog.Error("Could not write multiplayer.txt", error);
        }
    }

    // --- discovery ------------------------------------------------------------------------------

    private static void Scan()
    {
        if (scanned) return;
        scanned = true;

        try
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    string name = assembly.GetName().Name ?? "";
                    if (name.IndexOf("multiplayer", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (name.StartsWith("SeaPowerAdvanced", StringComparison.OrdinalIgnoreCase)) continue;

                    Type? found = assembly.GetType("SeapowerMultiplayer.Plugin", false);
                    if (found == null) continue;

                    pluginType = found;
                    present = true;
                    break;
                }
                catch (Exception) { }
            }

            if (!present || pluginType == null)
            {
                present = false;
                version = "not loaded";
                return;
            }

            Type plugin = pluginType;

            version = ReadVersion(plugin) ?? "unknown";

            instanceMember = Static(plugin, "Instance");
            hostFlagMember = MemberProbe.Value(plugin, "CfgIsHost", "IsHost", "IsServer");

            // The network manager lives in the same assembly and owns "is the session up". Looked up
            // by name first, because GetTypes can throw on an assembly with an optional dependency
            // missing - and a detection failure there must not cost the session check.
            networkType = plugin.Assembly.GetType("SeapowerMultiplayer.NetworkManager", false);

            if (networkType == null)
            {
                try
                {
                    foreach (Type type in plugin.Assembly.GetTypes())
                    {
                        if (type.Name != "NetworkManager") continue;
                        networkType = type;
                        break;
                    }
                }
                catch (Exception) { }
            }

            if (networkType != null)
            {
                networkInstanceMember = Static(networkType, "Instance");
                establishedMember = MemberProbe.Value(networkType, "IsEstablished", "IsConnected");
            }
        }
        catch (Exception error)
        {
            present = false;
            ModLog.Error("Multiplayer: detection failed; treating this as single player", error);
        }
    }

    private static string? ReadVersion(Type plugin)
    {
        try
        {
            Type? info = plugin.Assembly.GetType("SeapowerMultiplayer.PluginInfo", false);
            FieldInfo? field = info?.GetField("PLUGIN_VERSION",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            return field?.GetValue(null) as string;
        }
        catch (Exception) { return null; }
    }

    /// <summary>A static field or property. <see cref="MemberProbe.Value"/> only walks instance
    /// members, and everything this file needs from the other mod is static.</summary>
    private static MemberInfo? Static(Type? type, string name)
    {
        if (type == null) return null;

        try
        {
            return (MemberInfo?)type.GetProperty(name,
                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                   ?? type.GetField(name,
                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        }
        catch (Exception) { return null; }
    }

    private static object? ReadStatic(Type? type, MemberInfo? member)
    {
        if (type == null || member == null) return null;

        try
        {
            return member is PropertyInfo property ? property.GetValue(null)
                 : member is FieldInfo field ? field.GetValue(null)
                 : null;
        }
        catch (Exception) { return null; }
    }

    private static bool ReadBool(object owner, MemberInfo? member)
    {
        if (member == null) return false;

        try { return MemberProbe.Read(member, owner) is bool value && value; }
        catch (Exception) { return false; }
    }

    /// <summary>The jam order overload the multiplayer mod itself hooks: an enum order type, the
    /// target object, and a flag. Picked by shape because the enum's name is not importable here.</summary>
    private static MethodInfo? JamOrder()
    {
        try
        {
            foreach (MethodInfo method in typeof(ObjectBase).GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (method.Name != "setOrder") continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 3) continue;
                if (!parameters[0].ParameterType.IsEnum) continue;
                if (!parameters[1].ParameterType.IsAssignableFrom(typeof(ObjectBase))) continue;

                if (JamValue(parameters[0].ParameterType) != null) return method;
            }
        }
        catch (Exception) { }

        return null;
    }

    private static object? JamValue(Type orderType)
    {
        try
        {
            foreach (string name in Enum.GetNames(orderType))
            {
                if (string.Equals(name, "Jam", StringComparison.OrdinalIgnoreCase))
                    return Enum.Parse(orderType, name);
            }
        }
        catch (Exception) { }

        return null;
    }
}
