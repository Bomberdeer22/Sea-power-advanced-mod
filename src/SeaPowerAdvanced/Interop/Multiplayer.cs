using System;
using System.Collections.Generic;
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

    // --- who commands which ship ----------------------------------------------------------------

    /// <summary>
    /// The two rules the multiplayer mod already keeps, read rather than re-invented.
    ///
    /// <c>SeapowerMultiplayer.UnitLockManager.BlocksOrdersFor(ObjectBase)</c> is the co-op lock: the
    /// other player has claimed this unit, and the mod itself refuses orders for it. Its own comment
    /// says every order path used to re-implement that check and one of them forgot - which is
    /// exactly the mistake this file is not going to make.
    ///
    /// <c>SeapowerMultiplayer.TaskforceAssignmentManager.ClientMayControl(ObjectBase)</c> is the
    /// host's task-force assignment: a client given one task force may not order another's units.
    /// </summary>
    private static MethodInfo? blocksOrders;
    private static MethodInfo? mayControl;
    private static MethodInfo? lockedByRemote;
    private static Type? lockType;
    private static Type? assignmentType;

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
            Attempt("offensive ECM jam", ship, true, note);
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

        // The table is the authority when there is a row for this feature: its sentence is what the
        // report shows, so the menu and the report cannot say different things.
        ClientRule? rule = RuleFor(what);
        if (rule != null) return rule.Allowed ? null : ClientRefusal(what);

        return what + " is switched at the host in a multiplayer session: this machine does not "
               + "decide what the simulation does, and anything written here is reverted by the "
               + "host's picture within a frame.";
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
                // The session drops a repeat radar toggle on the same unit and radar group inside
                // ten seconds, and a dropped toggle does not move the switch locally either - so
                // the blink's half-cycle has a floor. That is the one number the table cannot
                // carry, because it is a throttle rather than a posture.
                text.AppendLine("  radar blink:      half-cycle at least " + ToggleFloorSeconds + "s, above the");
                text.AppendLine("                    session's radar-toggle throttle, so no flip is dropped");
            }
            else
            {
                text.AppendLine("  radar blink:      unchanged - with no session up nothing throttles a");
                text.AppendLine("                    radar switch, so BlinkSeconds is used as written");
            }

            if (!IsClient)
            {
                text.AppendLine("  everything:       runs here and replicates outward, because this");
                text.AppendLine("                    machine simulates the world. There is nothing to");
                text.AppendLine("                    adapt, so the table below is what a client would see.");
                text.AppendLine();
            }

            text.AppendLine("Who commands what");
            text.AppendLine("------------------");
            text.AppendLine("  ownership rule: "
                            + (!InSession
                                ? "not needed - with no session up every unit here is this machine's"
                                : OwnershipReadable
                                    ? "read from the multiplayer mod ("
                                      + (blocksOrders != null ? "UnitLockManager.BlocksOrdersFor" : "")
                                      + (blocksOrders != null && mayControl != null ? " + " : "")
                                      + (mayControl != null
                                          ? "TaskforceAssignmentManager.ClientMayControl" : "")
                                      + ")"
                                    : "NOT READABLE - this build of the multiplayer mod publishes "
                                      + "no rule the mod can read"));
            text.AppendLine("  this machine: " + (IsAuthority
                                ? "the authority - it acts on everything"
                                : "a client - defensive and offensive ECM act only on the ships this "
                                  + "client commands"));

            if (InSession)
            {
                int mine = 0;
                int theirs = 0;

                try
                {
                    foreach (ObjectBase ship in Features.FleetTicker.Tracked())
                    {
                        if (Commands(ship, out _)) mine++;
                        else theirs++;
                    }
                }
                catch (Exception) { }

                text.AppendLine("  the fleet:    " + mine + " ship(s) this machine commands, "
                                + theirs + " it does not");

                if (theirs > 0)
                {
                    text.AppendLine("                Those " + theirs + " are never armed, engaged, "
                                    + "jammed or held by this");
                    text.AppendLine("                machine, whatever the menu was used for on the "
                                    + "other screen.");
                }
            }

            text.AppendLine();
            text.AppendLine("Every feature, by how it reaches the simulation");
            text.AppendLine("---------------------------------------------");
            text.AppendLine("  ORDER = the game's own order, forwarded by the multiplayer mod");
            text.AppendLine("  CALL  = a call on the game's own object; sent, and the host may override it");
            text.AppendLine("  READ  = reads and draws, writes nothing");
            text.AppendLine("  LOCAL = a write the host would revert, so it is not attempted on a client");
            text.AppendLine();

            foreach (ClientRule rule in rules)
            {
                string route = rule.Kind switch
                {
                    Route.Order => "ORDER",
                    Route.Call => "CALL ",
                    Route.Read => "READ ",
                    _ => "LOCAL",
                };

                text.AppendLine("  " + route + "  " + rule.Feature);
                text.AppendLine("           drives: " + rule.How);

                if (IsClient)
                {
                    text.AppendLine("           here:   "
                                    + (rule.Allowed ? "" : "REFUSED - ") + rule.OnClient);
                }
            }

            text.AppendLine();
            text.AppendLine("What has actually been tried on this machine");
            text.AppendLine("-------------------------------------------");

            if (attempts.Count == 0)
            {
                text.AppendLine("  nothing yet this session. Every path above is reasoned from the");
                text.AppendLine("  mod's own code; only a session says whether the multiplayer mod");
                text.AppendLine("  carries each one, and this section is where that answer appears.");
            }
            else
            {
                foreach (KeyValuePair<string, Attempted> entry in attempts)
                {
                    Attempted tried = entry.Value;

                    text.AppendLine("  " + entry.Key + ": tried " + tried.Count + " time(s), last "
                                    + (tried.Ok ? "took" : "no effect")
                                    + (tried.Unit.Length > 0 ? " on " + tried.Unit : ""));

                    if (tried.Note.Length > 0) text.AppendLine("      " + tried.Note);
                }

                // Only ORDER and CALL rows are paths a client can act on, so only those can be
                // proven. A LOCAL row is a refusal by design (there is nothing to send) and the
                // READ row writes nothing, so counting them as "untried" would make the number look
                // worse than the truth - which is the same dishonesty in the other direction.
                int provable = 0;
                int proved = 0;

                foreach (ClientRule rule in rules)
                {
                    if (rule.Kind != Route.Order && rule.Kind != Route.Call) continue;

                    provable++;
                    if (attempts.ContainsKey(rule.Feature)) proved++;
                }

                text.AppendLine();
                text.AppendLine("  " + proved + " of " + provable + " path(s) a client can act on have "
                                + "been tried" + (proved < provable
                                    ? "; " + (provable - proved) + " are still unproven."
                                    : ". Every one of them has been exercised here."));
                text.AppendLine("  The " + (rules.Count - provable) + " other row(s) are marked LOCAL or READ: "
                                + "a LOCAL row is refused before");
                text.AppendLine("  anything is written, and a READ row writes nothing at all - neither "
                                + "is a path that can fail.");
            }

            text.AppendLine();
            text.AppendLine("Last thing worth knowing");
            text.AppendLine("------------------------");
            text.AppendLine("  " + lastNote);

            lastWrite = UnityEngine.Time.unscaledTime;
            ModFolders.WriteText("multiplayer.txt", text.ToString());
        }
        catch (Exception error)
        {
            ModLog.Error("Could not write multiplayer.txt", error);
        }
    }


    // --- what a client can and cannot do ---------------------------------------------------------

    /// <summary>
    /// How a feature reaches the simulation, which is the only thing that decides whether it can
    /// work on a client. This is a rule, not a list of exceptions: a feature is added to the table
    /// with its route, and the menu, the refusal and the report all read the same row.
    /// </summary>
    internal enum Route
    {
        /// <summary>The game's own order - a jam, a waypoint, a launch. The multiplayer mod forwards
        /// these, so the host runs exactly what was asked for.</summary>
        Order = 0,

        /// <summary>A call on the game's own object. The session may forward it and may not; it is
        /// attempted here and the outcome is recorded, which is how a client session proves which
        /// ones take.</summary>
        Call = 1,

        /// <summary>Nothing is written: the feature reads state and draws it. Works the same on
        /// either side of a session.</summary>
        Read = 2,

        /// <summary>The mod's own write, to state the host simulates or to another unit's data. A
        /// client's version is reverted by the host within a frame, so it is not attempted and the
        /// menu says why.</summary>
        Local = 3,
    }

    /// <summary>One row of the rule table: a feature, how it reaches the sim, and what a client gets.</summary>
    internal sealed class ClientRule
    {
        internal ClientRule(string feature, Route route, string how, string onClient)
        {
            Feature = feature;
            Kind = route;
            How = how;
            OnClient = onClient;
        }

        /// <summary>The name the feature passes to <see cref="Attempt"/> and <see cref="RefusedTo"/>.</summary>
        internal string Feature { get; }

        /// <summary>
        /// Named Kind, not Route: a member called Route inside a class whose enclosing class nests a
        /// type called Route makes <c>Route.Local</c> an instance access on a static - CS0176.
        /// </summary>
        internal Route Kind { get; }

        /// <summary>Which lever it drives, in one clause.</summary>
        internal string How { get; }

        /// <summary>What happens on a client, and why - written to be read by a player.</summary>
        internal string OnClient { get; }

        internal bool Allowed => Kind != Route.Local;
    }

    /// <summary>
    /// Every feature, and what a client can expect of it.
    ///
    /// The names here are the ones the features themselves pass in, so a row that is never named is
    /// visibly a row nobody uses: <see cref="Attempt"/> records by the same key, and the report
    /// prints the table, so the two cannot drift apart the way prose can.
    /// </summary>
    private static readonly List<ClientRule> rules = new()
    {
        new("radar and sonar switches", Route.Call,
            "SensorControl, through the game's own Enable/Disable calls",
            "sent, and the session throttles a repeat toggle on the same set within ten seconds. A "
            + "throttled toggle does not move locally either, so the menu's own label may take a "
            + "moment to catch up"),

        new("radar blink", Route.Call,
            "the same radar switches, on a timer",
            "half-cycle at least 12 s so the throttle does not eat a flip, and each flip is checked "
            + "four seconds later - a flip the host refused stops the blink instead of pretending"),

        new("Auto-ARM shutoff", Route.Local,
            "radar switches, triggered by an inbound weapon",
            "not offered: the host decides when a ship radiates, and a cut written here is reverted"),

        new("Defensive ECM", Route.Local,
            "jam assignments on the ship, plus the dive on the missile",
            "not offered: weapons are simulated by the host, so missile defence has to be armed "
            + "there"),

        new("defensive ECM on someone else's ship", Route.Local,
            "the same jam assignments, on a hull this client does not command",
            "refused everywhere, host or client: in a co-op session the fleet list holds the other "
            + "player's ships, and arming one from here put their ship on the air without them asking. "
            + "Arming a whole task force also needs a readable ownership rule, or it is refused"),

        new("offensive ECM jam", Route.Order,
            "the game's own jam order, unit or bearing form",
            "sent, and the multiplayer mod forwards it; the host runs the game's own jam task"),

        new("offensive ECM on someone else's ship", Route.Local,
            "the game's own jam order, issued for a hull this client does not command",
            "refused: the contact menu picks the nearest jammer, and the nearest one may be the other "
            + "player's ship. Their jammer is theirs to task"),

        new("offensive ECM suppression", Route.Local,
            "the target's own sensor switches",
            "not attempted: the target is simulated by the host, so there is nothing here to switch "
            + "off. The lock's jam order still works"),

        new("laser rangefinder", Route.Call,
            "the game's own reveal call, onto a hull this machine commands",
            "works: it writes this machine's own contact picture, which is what the player is "
            + "looking at. Nothing about the host's simulation changes, and the track is not "
            + "carried to the other player - that is the datalink's job, not this one's"),

        new("ECM cones", Route.Read,
            "the game's own jammer state, drawn on the map",
            "unchanged - it draws what this machine knows, including jamming replicated in"),

        new("hatch control", Route.Call,
            "WeaponContainer.openHatches / closeHatches on the ship's own cells",
            "this machine's model only. The check marks show this machine's view, and the host's "
            + "own launch sequence still opens what it needs"),

        new("salvo deconfliction", Route.Call,
            "SetWeaponStatus, resolved from the game's own signature",
            "sent for each ship; whether the host adopts it is the host's answer, and the report "
            + "records what was sent"),

        new("readiness", Route.Call,
            "SetWeaponStatus for the posture, and the reload clock for the speed-up",
            "the weapons status is sent; the reload speed-up is skipped here, because the reload "
            + "clock belongs to the host and a shave written here would be reverted"),

        new("commercial disguise", Route.Call,
            "radar switches, telegraph and the hull's signature string",
            "the radars and telegraph are sent; the signature string is skipped, because that is "
            + "this machine's copy of the ship's data and the host would overwrite it"),

        new("local control", Route.Call,
            "the discipline enum on each mount, found by type name",
            "sent; the host decides whether a mount actually goes to local control"),

        new("sprint and drift", Route.Call,
            "the game's own SprintAndDriftDoctrine flag, and the towed array",
            "sent; the host runs the doctrine it accepts"),

        new("breakaway", Route.Call,
            "telegraph, rudder, chaff and the noisemaker",
            "sent; the host decides the ship's course, so treat the turn as a request in a session"),

        new("threat axis and helicopter patterns", Route.Order,
            "setWaypointTask, and FlightDeckLaunchUnit for a launch",
            "sent as the game's own orders, which the multiplayer mod forwards"),

        new("replenishment", Route.Call,
            "RearmAll and the two Replenish calls",
            "sent; the host decides what a magazine actually takes on"),
    };

    /// <summary>The row for a feature, matched case-insensitively on the name it passes in.</summary>
    internal static ClientRule? RuleFor(string feature)
    {
        if (string.IsNullOrEmpty(feature)) return null;

        foreach (ClientRule rule in rules)
        {
            if (string.Equals(rule.Feature, feature, StringComparison.OrdinalIgnoreCase)) return rule;
        }

        return null;
    }

    /// <summary>
    /// Why this feature cannot act from here, or null when it can.
    ///
    /// A feature whose name has no row is allowed: an unclassified feature is one nobody has
    /// reasoned about yet, and silently refusing it would be worse than letting it try and
    /// recording the outcome.
    /// </summary>
    internal static string? ClientRefusal(string feature)
    {
        if (!IsClient) return null;

        ClientRule? rule = RuleFor(feature);
        if (rule == null || rule.Allowed) return null;

        return rule.Feature + ": " + rule.OnClient + ".";
    }

    // --- what has actually been tried here -------------------------------------------------------

    private sealed class Attempted
    {
        internal int Count;
        internal string Unit = "";
        internal bool Ok;
        internal string Note = "";
    }

    /// <summary>
    /// What each path has done on this machine during this session, by feature name.
    ///
    /// This exists because a client path cannot be proven by reading the code: only a client can
    /// say whether the session carried it. The report prints one line per feature that was tried,
    /// with the count and the last outcome, and closes with how many of the paths a client can act
    /// on are still unproven - so a session that exercised three of twelve says nine, rather than
    /// implying that twelve work.
    /// </summary>
    private static readonly Dictionary<string, Attempted> attempts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When the report was last written, so a fleet-wide action cannot write it per ship.</summary>
    private static float lastWrite;

    /// <summary>
    /// Records one attempt at a feature and refreshes <c>multiplayer.txt</c>. The caller logs its
    /// own line, so this deliberately does not log twice.
    /// </summary>
    internal static void Attempt(string feature, ObjectBase? unit, bool ok, string note)
    {
        if (string.IsNullOrEmpty(feature)) return;

        try
        {
            if (!attempts.TryGetValue(feature, out Attempted? found) || found == null)
            {
                found = new Attempted();
                attempts[feature] = found;
            }

            Attempted entry = found;

            entry.Count++;
            entry.Ok = ok;
            entry.Note = note ?? "";

            try { entry.Unit = unit == null ? "" : unit.DisplayNameShort; }
            catch (Exception) { entry.Unit = "<unit>"; }

            // Only a session has anything to report; in single player this would be noise. The
            // write is throttled because a fleet-wide action records one attempt per ship, and the
            // report is rewritten whole each time.
            if (InSession && UnityEngine.Time.unscaledTime - lastWrite >= 1f)
            {
                lastWrite = UnityEngine.Time.unscaledTime;
                WriteReport();
            }
        }
        catch (Exception error)
        {
            ModLog.Warn("Multiplayer: could not record the '" + feature + "' attempt - "
                        + error.GetType().Name);
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

            // The ownership rules, by the names the multiplayer mod publishes them under. Missing is
            // a supported answer - an older or newer build of that mod - and it is reported.
            lockType = plugin.Assembly.GetType("SeapowerMultiplayer.UnitLockManager", false);
            assignmentType = plugin.Assembly.GetType("SeapowerMultiplayer.TaskforceAssignmentManager", false);

            blocksOrders = StaticBool(lockType, "BlocksOrdersFor", typeof(ObjectBase));
            mayControl = StaticBool(assignmentType, "ClientMayControl", typeof(ObjectBase));
            lockedByRemote = StaticBool(lockType, "IsLockedByRemote", typeof(int));

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

    /// <summary>A public static bool method taking one parameter of the given type, by name.</summary>
    private static MethodInfo? StaticBool(Type? type, string name, Type parameter)
    {
        if (type == null) return null;

        try
        {
            MethodInfo? method = type.GetMethod(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (method == null || method.ReturnType != typeof(bool)) return null;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 1) return null;
            if (!parameters[0].ParameterType.IsAssignableFrom(parameter)) return null;

            return method;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Whether the session's ownership rule could be read at all. False means the multiplayer mod is
    /// present but this build does not publish the members that say who controls what - in which case
    /// a fleet-wide action would be a guess, and this mod does not guess with other people's ships.
    /// </summary>
    internal static bool OwnershipReadable
    {
        get
        {
            Scan();
            return blocksOrders != null || mayControl != null;
        }
    }

    /// <summary>
    /// Whether this machine may act on this unit: in single player or on the host, everything of ours
    /// is ours; in a session, only what the session says this machine commands.
    ///
    /// <paramref name="why"/> says which rule answered, in a sentence a player can act on, and it is
    /// what the menu shows instead of an entry that would do nothing.
    /// </summary>
    internal static bool Commands(ObjectBase? unit, out string why)
    {
        why = "no unit";
        if (unit == null) return false;

        Scan();

        if (!InSession)
        {
            why = "no session - every unit here is this machine's";
            return true;
        }

        bool ours;
        try { ours = unit.IsPlayerUnit; }
        catch (Exception) { ours = true; }

        if (!ours)
        {
            why = "it is not one of ours";
            return false;
        }

        if (blocksOrders != null)
        {
            try
            {
                if (blocksOrders.Invoke(null, new object?[] { unit }) is bool blocked && blocked)
                {
                    why = "the other player has claimed it in this co-op session, and the "
                          + "multiplayer mod refuses orders for it (UnitLockManager)";
                    return false;
                }
            }
            catch (Exception) { }
        }

        if (mayControl != null)
        {
            try
            {
                if (mayControl.Invoke(null, new object?[] { unit }) is bool may && !may)
                {
                    why = "this client is assigned a different task force, so its orders do not "
                          + "belong here (TaskforceAssignmentManager)";
                    return false;
                }
            }
            catch (Exception) { }
        }

        why = OwnershipReadable
            ? "this client commands it"
            : "the session publishes no ownership rule the mod can read, so this is taken to be this "
              + "client's";
        return true;
    }

    /// <summary>
    /// Whether a fleet-wide action may run here. Per-ship entries are the player clicking a ship they
    /// can see, which is evidence enough; "every ship I have" is not, unless the session can say which
    /// of them are mine.
    /// </summary>
    internal static bool CanActFleetWide(out string why)
    {
        Scan();

        if (!InSession)
        {
            why = "no session";
            return true;
        }

        if (OwnershipReadable)
        {
            why = "the session's ownership rule is readable";
            return true;
        }

        why = "this build of the multiplayer mod publishes no ownership rule the mod can read, so a "
              + "fleet-wide action cannot tell your ships from the other player's. Do it ship by ship";
        return false;
    }

    // --- the shared picture ---------------------------------------------------------------------

    /// <summary>
    /// The multiplayer mod's own contact sharing, read and (when asked) poked.
    ///
    /// That mod has a whole contact pipeline - <c>ContactSyncManager</c> for track numbers and
    /// classification, <c>ContactRevealManager</c> for contacts only one machine holds - and it is
    /// gated on one flag of its own: <c>CoopSessionActive</c>, which is
    /// <i>not PvP and a session is up</i>. When that flag is off, the two players' pictures are
    /// <b>meant</b> to differ, and no amount of revealing here changes the other screen.
    ///
    /// So this is worth reading for two reasons. It is the honest explanation of "my friend cannot
    /// see what I see" when the session is flagged as PvP - the usual cause in a mission with a side
    /// per player, because a two-player mission with two sides looks like a versus mission to that
    /// mod. And when it *is* on, nudging it is harmless and occasionally useful: its reveal sweep
    /// runs on a timer, so a fresh sweep is the cheapest way to resynchronise after a gap.
    /// </summary>
    internal static class Picture
    {
        /// <summary>True when the multiplayer mod is sharing the picture, false when it is not,
        /// null when this build of it does not say.</summary>
        internal static bool? Shared(out string why)
        {
            Scan();
            why = "";

            if (!present)
            {
                why = "no multiplayer session - there is no second picture to share with";
                return false;
            }

            bool? flag = ReadStaticBool(ContactSyncType(), "CoopSessionActive");

            if (flag.HasValue)
            {
                why = flag.Value
                    ? "the multiplayer mod is sharing contacts (its CoopSessionActive is on)"
                    : "the multiplayer mod is NOT sharing contacts: its co-op flag is off, which "
                      + "means it is treating this session as PvP (its own CfgPvP setting), or it "
                      + "has not finished joining";

                // The flag can be on while the mod's own contact-sync switch is off, and then nothing
                // moves between the machines at all. Worth saying out loud, because the symptom is
                // identical to the switch this feature offers.
                if (flag.Value && ConfigBool("CfgContactSync", "ContactSync") == false)
                {
                    why += " - but its own CfgContactSync switch is off, so it is not carrying "
                           + "contacts between the two machines: that switch is the one to turn on";
                }

                return flag;
            }

            why = "this build of the multiplayer mod publishes no co-op flag the mod can read";
            return null;
        }

        /// <summary>The two switches that decide all of the above, as they are set right now.</summary>
        internal static string Switches()
        {
            Scan();
            if (!present || pluginType == null) return "the multiplayer mod is not loaded";

            return "CfgPvP=" + Describe(ConfigBool("CfgPvP", "PvP"))
                   + ", CfgContactSync=" + Describe(ConfigBool("CfgContactSync", "ContactSync"));
        }

        /// <summary>
        /// Asks the multiplayer mod to re-run its own contact sweeps. Public entry points of its
        /// own, called by name: nothing is written from here, and a missing one is reported rather
        /// than worked around.
        /// </summary>
        internal static string Resync()
        {
            Scan();

            var done = new List<string>();
            string why = "";

            if (!present)
            {
                why = "the multiplayer mod is not loaded";
                return why;
            }

            if (CallStatic(ContactSyncType(), "Tick", "ContactTick")) done.Add("contact sweep");
            if (CallStatic(ContactRevealType(), "Tick")) done.Add("reveal sweep");

            bool host = ReadBoolOf("CfgIsHost");

            if (host)
            {
                if (CallStatic(ContactSyncType(), "HostBroadcast")) done.Add("broadcast of the picture");
            }
            else
            {
                if (CallStatic(ContactRevealType(), "ClientSendReport")) done.Add("contact report to the host");
            }

            if (done.Count == 0)
                return "the multiplayer mod exposes no contact sweep this build can call";

            return "asked for a resync: " + string.Join(", ", done.ToArray());
        }

        private static Type? ContactSyncType() => TypeIn("SeapowerMultiplayer.ContactSyncManager");

        private static Type? ContactRevealType() => TypeIn("SeapowerMultiplayer.ContactRevealManager");

        private static Type? TypeIn(string name)
        {
            if (pluginType == null) return null;

            try { return pluginType.Assembly.GetType(name, false); }
            catch (Exception) { return null; }
        }

        private static bool? ConfigBool(params string[] names)
        {
            if (pluginType == null) return null;

            object? instance = ReadStatic(pluginType, instanceMember);
            if (instance == null) return null;

            foreach (string name in names)
            {
                MemberInfo? member = MemberProbe.Value(pluginType, name);
                if (member == null) continue;

                object? entry = MemberProbe.Read(member, instance);
                if (entry == null) continue;

                object? value = MemberProbe.Read(MemberProbe.Value(entry.GetType(), "Value"), entry);
                if (value is bool flag) return flag;
            }

            return null;
        }

        private static bool ReadBoolOf(string name)
        {
            if (pluginType == null) return false;

            object? instance = ReadStatic(pluginType, instanceMember);
            if (instance == null) return false;

            object? entry = MemberProbe.Read(MemberProbe.Value(pluginType, name), instance);
            if (entry == null) return false;

            return MemberProbe.Read(MemberProbe.Value(entry.GetType(), "Value"), entry) is bool flag && flag;
        }

        private static bool? ReadStaticBool(Type? type, string name)
        {
            if (type == null) return null;

            try
            {
                MemberInfo? member = (MemberInfo?)type.GetProperty(name,
                                           System.Reflection.BindingFlags.Public
                                           | System.Reflection.BindingFlags.NonPublic
                                           | System.Reflection.BindingFlags.Static)
                                       ?? type.GetField(name,
                                           System.Reflection.BindingFlags.Public
                                           | System.Reflection.BindingFlags.NonPublic
                                           | System.Reflection.BindingFlags.Static);

                object? value = ReadStatic(type, member);
                return value is bool flag ? flag : null;
            }
            catch (Exception) { return null; }
        }

        private static bool CallStatic(Type? type, params string[] names)
        {
            if (type == null) return false;

            try
            {
                foreach (string name in names)
                {
                    MethodInfo? method = type.GetMethod(name,
                        System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Static, null, Type.EmptyTypes, null);

                    if (method == null) continue;

                    method.Invoke(null, null);
                    return true;
                }
            }
            catch (Exception) { }

            return false;
        }

        private static string Describe(bool? value) =>
            value.HasValue ? (value.Value ? "on" : "off") : "not readable";
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
