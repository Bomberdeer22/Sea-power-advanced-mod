using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SeaPower;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// Offensive ECM, in two modes.
///
/// <b>Standard</b> is what the game already does with the ship's own jammer, and is the default:
/// the mod does not touch it. Nothing here runs, nothing here radiates, and switching back to
/// standard releases any lock immediately.
///
/// <b>Signature lock</b> is the focused mode. The ship's ECM is pointed at one contact and held
/// there, and the mod first *reads* that contact's sensor fit - which radars, which sonar, whether
/// each is up, and what its hull is radiating as. That reading is the lock: without it the jammer
/// does not know what to jam. With it, every sensor family the contact has is switched off and kept
/// off, re-asserted ten times a second, so the target is blind for as long as the lock is held.
///
/// Unlike the defensive feature, this mode does **not** switch the attacking ship's radar on: it runs
/// on EMCON. Nothing here radiates, so the jammer gives away nothing while it works - which is the
/// whole reason to use ECM instead of a radar lock.
///
/// The effect is real and it is entirely the game's own switches: the target's radar, its sonar, its
/// towed array and its own electronic warfare fit are all turned off through the same members this
/// mod uses on friendly ships. The mod is not editing anyone's picture; it is holding another ship's
/// switches down.
///
/// What that means in practice, stated plainly: the target's crew can switch a set back on between
/// assertions, and this re-asserts. It is sustained suppression, not a permanent state change. And
/// if a sensor family cannot be reached on this build of the game, the menu and
/// <c>offensive-ecm.txt</c> name it instead of calling the target blind.
///
/// <b>Two limits on the second half</b>, because holding a stranger's whole sensor fit down for as
/// long as the player likes, on as many contacts as the fleet can reach, was the strongest thing in
/// the mod:
///
/// <list type="number">
/// <item><b>Channels</b> - the task force can keep <c>EcmJamChannels</c> contacts blind at once
///   (two by default). A ship holds one contact, because the game gives a jammer one assignment; the
///   budget is what limits how many *blinded* contacts exist at any moment. Every lock keeps the
///   game's own jam whether or not it has a channel.</item>
/// <item><b>Recovery</b> - a hold lasts <c>EcmJamHoldSeconds</c> (a minute), after which the mod
///   stops holding the sets down and the contact's crew get them back. For the next
///   <c>EcmJamRecoverySeconds</c> (fifteen) the same contact cannot be held down again. Nothing is
///   switched back on for the enemy: the mod stops, and the game's own crew decides.</item>
/// </list>
///
/// Both are settings, both are off at their old values, and both are stated in the menu and in the
/// report rather than left for the player to discover.
/// </summary>
internal static class OffensiveEcm
{
    /// <summary>How the ship's ECM is being used.</summary>
    internal enum Mode
    {
        /// <summary>The game's own behaviour. The mod does nothing at all.</summary>
        Standard = 0,

        /// <summary>Read one contact's signatures, then hold it blind.</summary>
        SignatureLock = 1,
    }

    private sealed class Hold
    {
        internal ObjectBase? Target;
        internal float NextAssertAt;
        internal int Assertions;
        internal string LastReport = "";
        internal bool Reported;

        /// <summary>
        /// True once the player has told it to transmit. Locking only aims the beam: holding the
        /// target's switches down is a separate, deliberate order.
        /// </summary>
        internal bool Jamming;

        /// <summary>When this hold runs out and the contact's crew get their sensors back. Zero
        /// means the window is switched off (<c>EcmJamHoldSeconds</c> 0) and it runs indefinitely,
        /// which is the old behaviour.</summary>
        internal float HeldUntil;

        /// <summary>How many hold windows this lock has already used up, for the report.</summary>
        internal int Windows;
    }

    private static readonly Dictionary<ObjectBase, Hold> Held = new();

    /// <summary>
    /// One contact's recovery window. Kept per contact rather than per lock, so releasing and
    /// re-locking cannot skip it, and so the mod can look at the contact's sensors a few seconds
    /// later and say in the log whether its own side actually brought anything back up.
    /// </summary>
    private sealed class Recovery
    {
        internal float Started;
        internal float Until;
        internal bool Checked;
    }

    /// <summary>When each contact may be held down again, by contact. Unity's == reports true for a
    /// destroyed object, so entries are expired by time as well as by target.</summary>
    private static readonly Dictionary<ObjectBase, Recovery> Recovering = new();

    /// <summary>How long after a hold ends the contact's own sensors are read once, so the log can
    /// say whether the window is buying the target anything on this build.</summary>
    private const float RecoveryCheckSeconds = 5f;
    private static bool pictureReported;
    private static readonly Dictionary<ObjectBase, Mode> Modes = new();

    /// <summary>
    /// How often the suppression is re-asserted. Ten times a second, because the target's own crew
    /// works on the same switches and the point of the hold is that it does not lapse between ticks.
    /// </summary>
    private const float AssertSeconds = 0.1f;

    /// <summary>How far the focused jammer reaches against a ship. Beyond this the lock drops.</summary>
    private static float RangeNm => Mathf.Clamp(ModSettings.JamRangeNm, 5f, 300f);

    /// <summary>How far it reaches against something airborne - see ModSettings.JamAirRangeNm.</summary>
    private static float AirRangeNm => Mathf.Clamp(ModSettings.JamAirRangeNm, 5f, 300f);

    /// <summary>
    /// How far this jammer can hold a particular contact.
    ///
    /// Two figures, because the two are configured separately: <c>JamRangeNm</c> for a ship or a
    /// missile skimming the sea, <c>JamAirRangeNm</c> for anything airborne. Both default to 150 nm.
    /// This is the reach the mod uses to decide what to offer and when to let go of a lock - the
    /// game's own ECM model still decides whether the jam bites at that distance, and a masthead
    /// jamming a ship's radar at 150 nm is well past the radio horizon in the real world.
    /// </summary>
    internal static float Reach(ObjectBase? contact) =>
        UnitFit.IsAir(contact) ? AirRangeNm : RangeNm;

    // --- the two limits --------------------------------------------------------------------------

    /// <summary>How many contacts the task force may keep blind at once.</summary>
    private static int Channels => ModSettings.EcmJamChannels;

    /// <summary>How long one hold lasts, or zero for the unlimited version.</summary>
    private static float HoldSeconds => ModSettings.EcmJamHoldSeconds;

    /// <summary>How long a contact's crew are left alone after a hold ends.</summary>
    private static float RecoverySeconds => ModSettings.EcmJamRecoverySeconds;

    /// <summary>How many locks are holding a contact's sets down right now.</summary>
    private static int ChannelsInUse()
    {
        int used = 0;

        // A client cannot switch a host-simulated ship's sensors off, so a hold ordered from a
        // client occupies nothing: counting it would take a channel from the machine that can
        // actually use it.
        bool here = !Multiplayer.IsClient;

        foreach (KeyValuePair<ObjectBase, Hold> entry in Held)
        {
            if (here && entry.Value.Jamming) used++;
        }

        return used;
    }

    /// <summary>Who is holding the channels, so the menu can name what is occupying them.</summary>
    private static string ChannelUsers()
    {
        var names = new List<string>();

        bool here = !Multiplayer.IsClient;

        foreach (KeyValuePair<ObjectBase, Hold> entry in Held)
        {
            if (!here || !entry.Value.Jamming) continue;
            names.Add(Name(entry.Key) + " on " + Name(entry.Value.Target));
        }

        return names.Count > 0 ? string.Join(", ", names) : "nothing";
    }

    /// <summary>One line for the menu: the budget and what is using it.</summary>
    internal static string ChannelLine()
    {
        int used = ChannelsInUse();

        return used + " of " + Channels + " suppression channel(s) in use"
               + (used > 0 ? " - " + ChannelUsers() : "");
    }

    /// <summary>Seconds left on this contact's recovery window, or zero when it is free.</summary>
    private static float RecoveringFor(ObjectBase? target)
    {
        if (target == null) return 0f;
        if (!Recovering.TryGetValue(target, out Recovery recovery)) return 0f;

        float left = recovery.Until - Time.unscaledTime;
        return left > 0f ? left : 0f;
    }

    /// <summary>Seconds left on this ship's hold, or zero when it is not holding anything.</summary>
    internal static int HoldLeft(ObjectBase? ship)
    {
        if (ship == null || !Held.TryGetValue(ship, out Hold hold)) return 0;
        if (!hold.Jamming || hold.HeldUntil <= 0f) return 0;

        float left = hold.HeldUntil - Time.unscaledTime;
        return left > 0f ? (int)Math.Ceiling(left) : 0;
    }

    /// <summary>
    /// Ends a hold: the mod stops switching the contact's sets off, and the contact cannot be held
    /// down again until its recovery window closes. Nothing is switched back on for the enemy - what
    /// it does with its own radars afterwards is the game's own crew, not this mod pretending.
    /// </summary>
    private static void EndHold(ObjectBase ship, Hold hold, string why)
    {
        if (!hold.Jamming) return;

        hold.Jamming = false;
        hold.HeldUntil = 0f;
        hold.Windows++;
        hold.Reported = false;

        ObjectBase? target = hold.Target;
        if (target != null)
        {
            Recovering[target] = new Recovery
            {
                Started = Time.unscaledTime,
                Until = Time.unscaledTime + RecoverySeconds,
            };
        }

        ModLog.Info("Offensive ECM: " + Name(ship) + " stopped holding " + Name(target) + " down - "
                    + why + (hold.Windows > 1 ? " (hold " + hold.Windows + " on this contact)" : "")
                    + "; its crew have their sensors back"
                    + (RecoverySeconds > 0f
                        ? " and it cannot be held down again for " + (int)RecoverySeconds + " s"
                        : "")
                    + ". The game's own jam continues.");
    }

    /// <summary>
    /// Whether this ship's lock could be switched to holding the contact's sensors down right now,
    /// with the reason when it could not. The menu uses this so a refused order is never a click
    /// that appears to do nothing.
    /// </summary>
    internal static bool CanHold(ObjectBase? ship, out string why)
    {
        why = "";

        if (ship == null || !Held.TryGetValue(ship, out Hold hold) || hold.Target == null)
        {
            why = "no lock to hold - lock a contact first";
            return false;
        }

        if (hold.Jamming)
        {
            why = "already holding " + Name(hold.Target) + " down";
            return true;
        }

        ObjectBase target = hold.Target;

        if (ContactPicture.Identified(target) == false)
        {
            why = Name(target) + " is not identified";
            return false;
        }

        if (Multiplayer.IsClient)
        {
            why = "the host simulates the target";
            return false;
        }

        float recovering = RecoveringFor(target);
        if (recovering > 0f)
        {
            why = Name(target) + "'s crew are back up for another "
                  + (int)Math.Ceiling(recovering) + " s";
            return false;
        }

        if (ChannelsInUse() >= Channels)
        {
            why = "all " + Channels + " suppression channel(s) are in use (" + ChannelUsers() + ")";
            return false;
        }

        return true;
    }

    // --- mode ------------------------------------------------------------------------------------

    internal static Mode ModeOf(ObjectBase? unit)
    {
        if (unit == null) return Mode.Standard;
        return Modes.TryGetValue(unit, out Mode mode) ? mode : Mode.Standard;
    }

    internal static bool IsLocked(ObjectBase? unit) =>
        unit != null && Held.TryGetValue(unit, out Hold hold) && hold.Target != null;

    /// <summary>Whether this ship's jammer is locked on this particular contact.</summary>
    internal static bool IsLockedOn(ObjectBase? unit, ObjectBase? contact) =>
        unit != null && contact != null
        && Held.TryGetValue(unit, out Hold hold) && ReferenceEquals(hold.Target, contact);

    /// <summary>Switches between the game's own ECM and the focused, signature-lock mode.</summary>
    internal static bool SetMode(ObjectBase unit, Mode mode, out string report)
    {
        if (unit == null)
        {
            report = "no unit";
            return false;
        }

        // Arming a mode is a command to that ship, so it has to be this machine's ship.
        if (!Multiplayer.Commands(unit, out string notMine))
        {
            report = Name(unit) + " is not this machine's to command: " + notMine;
            ModLog.Warn("Offensive ECM: " + report);
            return false;
        }

        Modes[unit] = mode;

        if (mode == Mode.Standard)
        {
            Release(unit, out string released);
            report = released.Length > 0
                ? "standard ECM: the game's own behaviour, and the mod stands off (" + released + ")"
                : "standard ECM: the game's own behaviour, and the mod stands off";
            return true;
        }

        report = OffensiveJam.Available
            ? "focused mode armed. Runs on EMCON - this ship radiates no radar of its own. Pick a "
              + "contact: the game's own jam order does the jamming, identified or not."
            : "focused mode selected, but this build exposes no jam order the mod can name - see "
              + "offensive-ecm.txt";
        return true;
    }

    // --- contacts --------------------------------------------------------------------------------

    /// <summary>
    /// Hostile contacts the force's picture holds, nearest first. Read from the contact lists - the
    /// same ones the threat-axis order uses - so the mod never invents a target the force cannot
    /// actually see.
    ///
    /// The picture is the <b>task force's</b>, not this one ship's: an aircraft picked up by a
    /// picket thirty miles away is a contact the force holds, and the jammer may be the only set
    /// near enough to work it. Reading only the jammer's own list meant an aircraft circling over
    /// the horizon was invisible to every jammer in the fleet.
    ///
    /// Aircraft and helicopters are included, and reached at their own, much longer range.
    /// </summary>
    internal static List<ObjectBase> Contacts(ObjectBase ship)
    {
        var contacts = new List<ObjectBase>();
        if (ship == null) return contacts;

        GeoPosition own;
        try { own = ship.getGeoPosition(); }
        catch (Exception) { return contacts; }

        var seen = new HashSet<ObjectBase>();

        // The picture first. This is the map: every foreign track the player's side holds, whether
        // or not anybody has identified it. The old list scan below is kept only as a fallback for a
        // build whose plotting table this mod cannot read.
        List<ContactPicture.Contact> picture = Picture();

        foreach (ContactPicture.Contact entry in picture)
        {
            try
            {
                ObjectBase contact = entry.Unit;

                if (contact == null || contact.IsDestroyed) continue;
                if (ReferenceEquals(contact, ship)) continue;
                if (!seen.Add(contact)) continue;

                double range = ThreatAxis.Distance(own, contact.getGeoPosition());
                if (range > Reach(contact)) continue;

                contacts.Add(contact);
            }
            catch (Exception) { }
        }

        if (contacts.Count > 0) { Sort(contacts, own); return contacts; }

        foreach (ObjectBase contact in Candidates(ship))
        {
            try
            {
                if (contact == null || contact.IsDestroyed) continue;
                if (contact.IsPlayerUnit) continue;
                if (ReferenceEquals(contact, ship)) continue;
                if (!seen.Add(contact)) continue;

                double range = ThreatAxis.Distance(own, contact.getGeoPosition());
                if (range > Reach(contact)) continue;

                contacts.Add(contact);
            }
            catch (Exception) { }
        }

        Sort(contacts, own);
        return contacts;
    }

    private static void Sort(List<ObjectBase> contacts, GeoPosition own)
    {
        contacts.Sort((a, b) => SafeRange(own, a).CompareTo(SafeRange(own, b)));
    }

    /// <summary>
    /// The player's contact picture, with what it says about identification. Read once per menu
    /// build and shared, so a list of eight entries does not walk the plotting table eight times.
    /// </summary>
    private static List<ContactPicture.Contact> Picture()
    {
        try
        {
            List<ContactPicture.Contact> picture = ContactPicture.Hostiles();

            // Once per session, record what the picture actually was - a mission with no jammable
            // contact can then be told apart from a mission whose picture this build would not give
            // up, which is the difference between "nothing in range" and "the mod cannot see".
            if (!pictureReported && picture.Count > 0)
            {
                pictureReported = true;
                ContactPicture.WriteReport(picture);
            }

            return picture;
        }
        catch (Exception error)
        {
            ModLog.Warn("Offensive ECM: the contact picture could not be read - "
                        + error.GetType().Name);
            return new List<ContactPicture.Contact>();
        }
    }

    private static IEnumerable<ObjectBase> Candidates(ObjectBase ship)
    {
        foreach (ObjectBase unit in ListsOf(ship)) yield return unit;

        // Then the rest of the force's contacts. A picket's radar plot is the task force's plot:
        // the jammer is not required to be the ship that detected the aircraft it is about to jam.
        foreach (ObjectBase other in FleetTicker.Tracked())
        {
            if (other == null || ReferenceEquals(other, ship)) continue;

            foreach (ObjectBase unit in ListsOf(other)) yield return unit;
        }
    }

    /// <summary>
    /// One unit's contact lists, as the game holds them.
    ///
    /// The three known names are not the whole picture. A ship's air plot is not necessarily
    /// <c>_spottedObjects</c> - this build may keep aircraft in a list of its own - and an aircraft
    /// the game can see but this mod cannot find is exactly the "it will not lock onto planes"
    /// report. So the lists are <b>looked for</b>: any enumerable member whose name says it holds
    /// contacts, threats, tracks or detections, on this unit and on the rest of the force. Nothing is
    /// invented - only lists the game itself already filled are read.
    ///
    /// The names that were actually used are in <c>offensive-ecm.txt</c>, so the next build can be
    /// exact about which one the air picture lives in.
    /// </summary>
    private static IEnumerable<ObjectBase> ListsOf(ObjectBase unit)
    {
        var found = new List<ObjectBase>();

        foreach (MemberInfo member in ContactMembers(unit.GetType()))
        {
            object? value;
            try { value = MemberProbe.Read(member, unit); }
            catch (Exception) { continue; }

            if (value is not IEnumerable sequence || value is string) continue;

            try
            {
                foreach (object? item in sequence)
                {
                    if (item is ObjectBase contact) found.Add(contact);
                }
            }
            catch (Exception) { }
        }

        foreach (ObjectBase contact in found) yield return contact;
    }

    /// <summary>Cache of the contact-bearing members per unit type - this is read when a menu is
    /// built, and walking every member of ObjectBase is not free.</summary>
    private static readonly Dictionary<Type, List<MemberInfo>> ContactMembersByType = new();

    private static List<MemberInfo> ContactMembers(Type type)
    {
        if (ContactMembersByType.TryGetValue(type, out List<MemberInfo>? cached)) return cached;

        var members = new List<MemberInfo>();

        try
        {
            foreach (MemberInfo member in MemberProbe.All(type))
            {
                if (member is MethodInfo) continue;
                if (member is PropertyInfo property && !property.CanRead) continue;

                Type listType = member is PropertyInfo pi ? pi.PropertyType : ((FieldInfo)member).FieldType;
                if (!typeof(IEnumerable).IsAssignableFrom(listType)) continue;
                if (listType == typeof(string)) continue;

                string name = member.Name.ToLowerInvariant();

                bool contactish = name.Contains("spott") || name.Contains("threat")
                                  || name.Contains("contact") || name.Contains("track")
                                  || name.Contains("detect") || name.Contains("known")
                                  || name.Contains("dangerous") || name.Contains("hostile")
                                  || name.Contains("picture");

                if (contactish) members.Add(member);
            }
        }
        catch (Exception) { }

        ContactMembersByType[type] = members;
        return members;
    }

    /// <summary>The contact sources this build actually uses, for the report.</summary>
    internal static string Sources(ObjectBase? ship)
    {
        string picture = ContactPicture.Sources();
        if (ship is null) return picture;

        return picture + " | unit lists: " + UnitSources(ship);
    }

    /// <summary>The unit-level lists, kept because a build that holds contacts on the unit rather
    /// than on the task force would still be read by the fallback path.</summary>
    private static string UnitSources(ObjectBase ship)
    {
        // A local, so the compiler can see the reference is real: Unity's own == overload is not a
        // null test as far as flow analysis is concerned.
        Type type = ship.GetType();
        var names = new List<string>();

        foreach (MemberInfo member in ContactMembers(type))
        {
            if (!names.Contains(member.Name)) names.Add(member.Name);
        }

        return names.Count > 0 ? string.Join(", ", names) : "none found on this build";
    }

    private static double SafeRange(GeoPosition own, ObjectBase? unit)
    {
        try { return unit == null ? double.MaxValue : ThreatAxis.Distance(own, unit.getGeoPosition()); }
        catch (Exception) { return double.MaxValue; }
    }

    // --- the lock --------------------------------------------------------------------------------

    /// <summary>
    /// Locks a contact and points the ship's jammer at it through the game's own jam order.
    ///
    /// The lock is the game's jam, not a flag of the mod's: the order goes in, the game selects the
    /// set, and its radar model works on the target from that moment. The sensor read that follows
    /// is information and, later, the second half of the feature - holding the target's individual
    /// sets down as well - but it is <b>not</b> a condition. A contact nobody has identified is
    /// jammed on its bearing, and refusing to lock it because its fit could not be read was the bug
    /// that made unidentified contacts unusable.
    /// </summary>
    internal static bool Lock(ObjectBase ship, ObjectBase target, out string report)
    {
        if (ship == null || target == null)
        {
            report = "no target";
            return false;
        }

        if (!OffensiveJam.Available)
        {
            report = "this build exposes no jam order the mod can name - offensive-ecm.txt lists "
                     + "what was found";
            return false;
        }

        if (!OffensiveJam.HasEcm(ship))
        {
            report = Name(ship) + " carries no ECM, so it cannot jam anything";
            return false;
        }

        // Only a ship this machine commands. In a co-op session the fleet list holds the other
        // player's ships too, and a jam ordered for one of those is a jam that player did not ask
        // for and cannot see the reason for.
        if (!Multiplayer.Commands(ship, out string notMine))
        {
            report = Name(ship) + " is not this machine's to command: " + notMine;
            ModLog.Warn("Offensive ECM: " + report);
            return false;
        }

        double range = SafeRange(Position(ship), target);
        double reach = Reach(target);

        if (range > reach)
        {
            report = Name(target) + " is " + (int)range + " nm away - beyond the " + (int)reach
                     + " nm the focused jammer reaches against "
                     + (UnitFit.IsAir(target) ? "an aircraft" : "a ship");
            return false;
        }

        // 1. The read. It is information, not a gate: a contact nobody has identified can still be
        //    jammed, and refusing the lock because its fit cannot be read was exactly what made
        //    unidentified contacts unusable. What the read adds is the second half - holding its
        //    individual sets down as well as jamming it.
        List<SensorSet.Reading> signatures = SensorSet.Read(target);
        string signature = SensorSet.SignatureOf(target);
        bool? identified = ContactPicture.Identified(target);

        // 2. The lock itself: hold the track and point the jammer.
        Modes[ship] = Mode.SignatureLock;
        // A second lock on the same ship must not orphan the first: the old one is released the way
        // the menu releases it, so its hold ends under the recovery rule and the game's own teardown
        // takes its jam down instead of leaving it transmitting at nothing.
        if (Held.ContainsKey(ship)) Release(ship, out _);

        Held[ship] = new Hold
        {
            Target = target,
            NextAssertAt = 0f,
            Assertions = 0,
            Reported = false,
            Jamming = false,
            HeldUntil = 0f,
        };

        report = "locked on " + Name(target)
                 + (UnitFit.IsAir(target) ? " (airborne)" : "")
                 + " (" + (int)range + " nm, hull " + signature + "). "
                 + (identified == false
                        ? "Not identified - a bearing jam reaches it all the same. "
                        : "")
                 + (signatures.Count > 0
                        ? "Read " + SensorSet.Describe(signatures) + ". "
                        : "Its sensor fit could not be read, so the jam works on the game's own "
                          + "radar model alone. ")
                 + "The game's jam is live; 'hold its sensors down' adds the second half.";

        if (UnitFit.IsAir(target))
        {
            report += " An aircraft has one radar and its own jammer; with the radar off it is blind,"
                      + " and its crew can put it back up between assertions.";
        }

        if (Multiplayer.IsClient)
        {
            report += " In a session the jam goes to the host as the game's own jam order, so the"
                      + " game's ECM decides the effect - the mod cannot hold a host unit's switches"
                      + " down from here.";
        }

        // Two limits, named at the moment the lock is taken rather than discovered on the click that
        // does not work: how many contacts the force may keep blind, and whether this one is still
        // recovering from the last hold.
        if (ChannelsInUse() >= Channels)
        {
            report += " All " + Channels + " suppression channel(s) are in use, so the game's jam is "
                      + "all this contact gets until one is freed.";
        }

        float recovering = RecoveringFor(target);
        if (recovering > 0f)
        {
            report += " Its crew are back up for another " + (int)Math.Ceiling(recovering)
                      + " s, so it cannot be held down yet.";
        }

        // Point the jammer now. The lock's whole purpose is that the game's own ECM is working on
        // this contact: leaving that to a second click is how the feature looked broken.
        Hold created = Held[ship];
        bool aimed = AimTheJammer(ship, created, out string aimedNote);

        report += aimed
            ? " The jammer is pointed: " + aimedNote + "."
            : " The jammer would not take the order: " + aimedNote + ".";

        ModLog.Info("Offensive ECM: " + Name(ship) + " " + report);

        WriteReport(ship, target, signature, signatures,
                    new List<SensorSet.Reading>(),
                    aimed ? "lock - game jam order issued" : "lock only - the order was refused");
        return true;
    }

    /// <summary>
    /// Locks a contact from whichever of our own ships can actually do it: the nearest one with ECM,
    /// inside jam range. This is what the enemy contact's menu calls, so the player never has to
    /// first work out which of their ships has a jammer.
    /// </summary>
    internal static bool LockOnContact(ObjectBase contact, out string report)
    {
        report = "";

        if (contact == null)
        {
            report = "no contact";
            return false;
        }

        ObjectBase? jammer = BestJammerFor(contact);

        if (jammer == null)
        {
            // The reach belongs to the contact, not to this method: an aircraft is engaged out to
            // its own horizon. Named here because the sentence has to carry the right number.
            int reach = (int)Reach(contact);

            report = "no ship of ours with ECM is within " + reach + " nm of "
                     + Name(contact) + " - nothing to jam it with";
            ModLog.Warn("Offensive ECM: " + report);
            return false;
        }

        bool ok = Lock(jammer, contact, out string inner);
        report = ok ? Name(jammer) + ": " + inner : inner;
        return ok;
    }

    /// <summary>The nearest of our ships with ECM that is inside jam range of this contact.</summary>
    internal static ObjectBase? BestJammerFor(ObjectBase? contact)
    {
        if (contact == null) return null;

        ObjectBase? best = null;
        double bestRange = double.MaxValue;
        double reach = Reach(contact);

        foreach (ObjectBase ship in FleetTicker.Tracked())
        {
            try
            {
                if (!OffensiveJam.HasEcm(ship)) continue;

                // Somebody else's ship is not a jammer this client can task, however close it is.
                if (!Multiplayer.Commands(ship, out _)) continue;

                double range = SafeRange(Position(ship), contact);
                if (range > reach || range >= bestRange) continue;

                bestRange = range;
                best = ship;
            }
            catch (Exception) { }
        }

        return best;
    }

    /// <summary>
    /// The second half of the lock: the target's own sensors are switched off and held down from
    /// here, on top of the jam the lock already started.
    ///
    /// It needs the contact to be identified. There is nothing to hold down on a track nobody has
    /// named - the mod will not name a stranger's sensor fit for them - so an unidentified contact
    /// gets the game's own jam and this order declines, in words, rather than pretending.
    ///
    /// Two limits gate it, and both are stated in the refusal rather than hidden: a contact still
    /// inside its recovery window cannot be held down again, and the task force only has
    /// <c>EcmJamChannels</c> holds to give out at once. The lock's own jam is unaffected by either.
    /// </summary>
    internal static bool JamNow(ObjectBase ship, out string report)
    {
        report = "";

        if (ship == null || !Held.TryGetValue(ship, out Hold hold) || hold.Target == null)
        {
            report = "no lock to jam - lock a contact first";
            return false;
        }

        if (!Multiplayer.Commands(ship, out string notMineJam))
        {
            report = Name(ship) + " is not this machine's to command: " + notMineJam;
            return false;
        }

        if (hold.Jamming)
        {
            int left = HoldLeft(ship);
            report = "already holding " + Name(hold.Target) + " down"
                     + (left > 0 ? " - " + left + " s left in this window" : "");
            return true;
        }

        if (ContactPicture.Identified(hold.Target) == false)
        {
            report = Name(hold.Target) + " is not identified, so there is nothing to hold down - "
                     + "the game's jam on it is already working, and that does not need a name";
            return false;
        }

        // On a client the target is simulated by the host: writing its switches here would be
        // overwritten on the next frame, and the lock's game jam is the whole effect there.
        if (Multiplayer.IsClient)
        {
            report = "the target is simulated by the host, so the mod cannot switch its sensors "
                     + "off from this machine - the game's own ECM is what this lock gets";
            return false;
        }

        // The crew's own recovery window, which a re-order does not skip: without it the hold would
        // simply be renewed every time it lapsed and the target would never see anything again.
        float recovering = RecoveringFor(hold.Target);
        if (recovering > 0f)
        {
            report = Name(hold.Target) + "'s crew are back up for another "
                     + (int)Math.Ceiling(recovering) + " s - the mod will not hold the same contact "
                     + "down again until that window closes. The game's jam on it is still running";
            return false;
        }

        // The channel budget: the expensive half is what is rationed, not the jam.
        if (ChannelsInUse() >= Channels)
        {
            report = "no suppression channel free - all " + Channels + " are in use ("
                     + ChannelUsers() + "). The game's jam on " + Name(hold.Target)
                     + " is already running; release one of those to hold this one's sensors down";
            ModLog.Warn("Offensive ECM: " + Name(ship) + " " + report);
            return false;
        }

        hold.Jamming = true;
        hold.NextAssertAt = Time.unscaledTime + AssertSeconds;
        hold.Reported = false;
        hold.HeldUntil = HoldSeconds > 0f ? Time.unscaledTime + HoldSeconds : 0f;

        // The jam itself: the game's own order, so the game's own ECM model decides the effect -
        // range, the cone, the degraded radar picture, and in a session the host that runs it.
        // A contact nobody has identified is jammed on its bearing, which is the form the game
        // offers for exactly that case.
        bool aimed = AimTheJammer(ship, hold, out string order);

        if (!aimed)
        {
            hold.Jamming = false;
            hold.HeldUntil = 0f;
            report = "the jammer would not take the order on " + Name(hold.Target) + ": " + order;
            ModLog.Warn("Offensive ECM: " + report);
            return false;
        }

        // The second half, and only where it is real: the target's own sets are held down from
        // here. A client was refused above, so this machine simulates the target.
        List<SensorSet.Reading> readings = SensorSet.Silence(hold.Target);
        EcmControl.Engage(ship, hold.Target, false, out string legacy);

        string extra = readings.Count > 0
            ? (SensorSet.FullyBlind(readings)
                ? "every sensor off"
                : "NOT all sensors off - " + Stubborn(readings))
            : "no sensor fit readable, so the game's radar model alone";

        if (legacy.Length > 0) extra += " | " + legacy;

        report = "jamming " + Name(hold.Target) + " on EMCON: " + order + "; " + extra
                 + ". The hold lasts "
                 + (HoldSeconds > 0f
                        ? (int)HoldSeconds + " s, then its crew get their sensors back for "
                          + (int)RecoverySeconds + " s"
                        : "until it is stopped (no window)")
                 + "; " + ChannelLine() + ".";

        ModLog.Info("Offensive ECM: " + Name(ship) + " " + report);
        return true;
    }

    /// <summary>
    /// Points the ship's jammer at the held contact through the game's own order.
    ///
    /// One form is preferred over the other by what the picture knows: a contact the game has
    /// identified is jammed as a unit, because then the game can follow it as it moves; anything
    /// else is jammed on its bearing, which is the honest order for a track nobody has named. Both
    /// are the game's own and both are forwarded by the multiplayer mod.
    /// </summary>
    private static bool AimTheJammer(ObjectBase ship, Hold hold, out string note)
    {
        note = "";

        ObjectBase? target = hold.Target;
        if (target == null)
        {
            note = "the track is empty";
            return false;
        }

        bool? identified = ContactPicture.Identified(target);

        if (identified == false)
        {
            // Unidentified: the bearing form. The position is the plotted one, which is what the
            // player's side actually has - not the target's true position.
            GeoPosition bearing = Position(target);

            if (OffensiveJam.PointAt(ship, bearing, out string bearingNote))
            {
                note = "bearing jam at " + (int)SafeRange(Position(ship), target) + " nm ("
                       + bearingNote + ")";
                return true;
            }

            // No bearing form on this build: fall back to the unit form rather than not jamming.
            note = bearingNote;
        }

        if (OffensiveJam.PointAt(ship, target, out string unitNote))
        {
            note = (identified == false ? "unit jam (no bearing form): " : "") + unitNote;
            return true;
        }

        note = note.Length > 0 ? note + " / " + unitNote : unitNote;
        return false;
    }

    /// <summary>Stops transmitting but keeps the lock and the beam, so it can be resumed.</summary>
    internal static bool StopJam(ObjectBase ship, out string report)
    {
        report = "";

        if (ship == null || !Held.TryGetValue(ship, out Hold hold) || !hold.Jamming)
        {
            report = "not jamming";
            return false;
        }

        if (!Multiplayer.Commands(ship, out string notMineStop))
        {
            report = Name(ship) + " is not this machine's to command: " + notMineStop;
            return false;
        }

        // Only the second half is stopped here. The game's own jam belongs to the lock and keeps
        // running - "release" is the order that takes it down.
        //
        // Stopping by hand starts the same recovery window as a window running out, otherwise the
        // limit would be free to skip: stop and re-order would renew the hold for ever.
        EndHold(ship, hold, "stopped by hand");
        EcmControl.StandDown(ship, out _);

        report = Multiplayer.IsClient
            ? "stopped holding " + Name(hold.Target) + " down here; the lock and the game's jam "
              + "stay, and the host's sim is the one that ran them"
            : "stopped holding " + Name(hold.Target) + " down; the game's jam and the lock stay"
              + (RecoverySeconds > 0f
                    ? ", and its crew have " + (int)RecoverySeconds + " s with their sensors back"
                    : "");
        return true;
    }

    /// <summary>
    /// Whether the second half is on: the target's own sets are being held down. Distinct from
    /// <see cref="IsJamming"/>, which is the game's own jam order and runs from the moment the lock
    /// is taken.
    /// </summary>
    internal static bool IsHolding(ObjectBase? ship) =>
        ship != null && Held.TryGetValue(ship, out Hold hold) && hold.Jamming;

    /// <summary>
    /// Whether this ship is transmitting right now. The game's own jammer state is the answer when
    /// there is one - the mod's own flag only records whether the suppression half was ordered.
    /// </summary>
    internal static bool IsJamming(ObjectBase? ship)
    {
        if (ship == null) return false;
        if (OffensiveJam.IsTransmitting(ship)) return true;

        return Held.TryGetValue(ship, out Hold hold) && hold.Jamming;
    }

    /// <summary>The beams the map overlay draws for the offensive set.</summary>
    internal static List<EcmBeam> Beams()
    {
        var beams = new List<EcmBeam>();

        foreach (KeyValuePair<ObjectBase, Hold> entry in Held)
        {
            if (entry.Key == null || entry.Value.Target == null) continue;

            // The beam is live when the game's own jam is: that is what the radar calculator and
            // every ESM set in range actually sees.
            bool live = entry.Value.Jamming || OffensiveJam.IsTransmitting(entry.Key);
            beams.Add(new EcmBeam(entry.Key, entry.Value.Target, live, true));
        }

        return beams;
    }

    /// <summary>Drops the lock. The mod stops holding the target's switches down.</summary>
    internal static bool Release(ObjectBase ship, out string report)
    {
        report = "";

        if (ship == null || !Held.TryGetValue(ship, out Hold hold)) return false;

        if (!Multiplayer.Commands(ship, out string notMineRelease))
        {
            // The local record still goes: holding a lock for a ship this machine may not command
            // would leave the mod counting a suppression channel for a jam it can never stop. The
            // game's own jam task is the other client's to release.
            Held.Remove(ship);
            ModLog.Warn("Offensive ECM: lock dropped here without touching the ship - " + notMineRelease);
            report = "dropped this machine's lock without touching the ship: " + notMineRelease;
            return true;
        }

        string target = Name(hold.Target);

        // If a hold was running, releasing ends it the same way a window running out does: the
        // contact's crew get the recovery window, so releasing and re-locking cannot be used to
        // start a fresh hold on the same ship a moment later.
        EndHold(ship, hold, "the lock was released");
        Held.Remove(ship);

        // Nothing to restore on the radar front: this mode never switched it on. Offensive jamming
        // runs on EMCON - silent, radiating nothing - which is the point of using ECM over radar.
        EcmControl.StandDown(ship, out _);
        OffensiveJam.Cease(ship, out _);

        report = Multiplayer.IsClient
            ? "released " + target + " here; the host's jam task is its own, so it ends there"
            : "released " + target + " after " + hold.Assertions
              + " assertion(s); its own crew can bring its sensors back up";
        ModLog.Info("Offensive ECM: " + Name(ship) + " " + report);
        return true;
    }

    /// <summary>Releases every ship's lock - used when the feature is switched off wholesale.</summary>
    internal static int ReleaseAll(out string report)
    {
        int released = 0;

        foreach (ObjectBase ship in new List<ObjectBase>(Held.Keys))
        {
            if (Release(ship, out _)) released++;
        }

        report = released > 0 ? "released " + released + " lock(s)" : "nothing was locked";
        return released;
    }

    /// <summary>
    /// Drops recovery windows that have closed, and forgets contacts that are gone. Written as its
    /// own pass because the dictionary is keyed by the contact, not by the lock: a window that
    /// outlives the lock that created it is the point.
    /// </summary>
    private static void ExpireRecovery()
    {
        if (Recovering.Count == 0) return;

        float now = Time.unscaledTime;

        foreach (KeyValuePair<ObjectBase, Recovery> entry in
                 new List<KeyValuePair<ObjectBase, Recovery>>(Recovering))
        {
            try
            {
                // Unity's == is true for a destroyed object, and the key that is in the map is the
                // one to remove - the same rule the lock loop follows.
                if (entry.Key == null || entry.Key.IsDestroyed)
                {
                    // Remove through a local that the compiler can see is not null: `entry.Key` on
                    // its own is a possible-null argument (CS8604), because the null test is on a
                    // compound condition and the flow analysis does not carry it into the call.
                    ObjectBase? gone = entry.Key;
                    if (gone != null) Recovering.Remove(gone);
                    continue;
                }

                // Once, a few seconds after the hold ended: was anything switched back on? The mod
                // switched nothing on for the enemy, so this is the target's own side doing it -
                // and if nothing came back, the window is not buying it anything on this build and
                // the log says so instead of leaving the limit looking like it works.
                Recovery recovery = entry.Value;

                if (!recovery.Checked && now >= recovery.Started + RecoveryCheckSeconds)
                {
                    recovery.Checked = true;
                    NoteRecovery(entry.Key);
                }

                if (recovery.Checked && now >= recovery.Until) Recovering.Remove(entry.Key);
            }
            catch (Exception)
            {
                // Not the bare key: it is a possible-null argument to Remove (CS8604), and the whole
                // reason this line runs is that something already went wrong - a second throw while
                // cleaning up after the first would lose the real error.
                ObjectBase? key = entry.Key;
                if (key != null) Recovering.Remove(key);
            }
        }
    }

    /// <summary>
    /// Reads the contact's own sensors a few seconds after a hold ended and writes the answer down.
    /// The mod switches nothing back on for the enemy, so this is the only honest way to know
    /// whether the recovery window is doing anything: if its crew brought the sets back up, the
    /// limit is real; if they did not, the log says so and the limit needs revisiting.
    /// </summary>
    private static void NoteRecovery(ObjectBase target)
    {
        List<SensorSet.Reading> readings = SensorSet.Read(target);

        if (readings.Count == 0)
        {
            ModLog.Info("Offensive ECM: " + (int)RecoveryCheckSeconds + " s after the hold on "
                        + Name(target) + " ended its sensor fit could not be read, so whether its "
                        + "crew brought anything back is not visible to the mod");
            return;
        }

        int up = 0;
        int unknown = 0;

        foreach (SensorSet.Reading reading in readings)
        {
            if (reading.Radiating == true) up++;
            else if (reading.Radiating == null) unknown++;
        }

        // Three answers, not two. "Nothing came back" is only a finding when the game actually said
        // so for every family: a build that does not expose a state must not be reported as a
        // crew that never recovered.
        string verdict;

        if (up > 0)
        {
            verdict = ". Its crew have their sensors back, which is what the window is for";
        }
        else if (unknown > 0)
        {
            verdict = ". Nothing read as radiating, but " + unknown + " famil(ies) do not expose "
                      + "their state on this build, so a crew that did not recover cannot be told "
                      + "apart from a state the mod cannot read - send this line";
        }
        else
        {
            verdict = ". Nothing came back: the mod switched nothing on for it, so the window is "
                      + "not giving this target anything on this build - send this line";
        }

        ModLog.Info("Offensive ECM: " + (int)RecoveryCheckSeconds + " s after the hold on "
                    + Name(target) + " ended: " + up + " of " + readings.Count
                    + " sensor famil(ies) radiating - " + SensorSet.Describe(readings) + verdict);
    }

    // --- the tick --------------------------------------------------------------------------------

    /// <summary>
    /// Keeps every lock alive. Called from the ticker: this is where a lock is re-asserted, where a
    /// target that has been sunk or has run out of range is let go, and where the log records the
    /// moment a target went blind.
    /// </summary>
    internal static void Tick()
    {
        // The recovery clocks run whether or not anything is locked, so a contact that has just been
        // let go keeps its window even after the lock that created it has gone.
        ExpireRecovery();

        if (Held.Count == 0) return;
        if (!ModSettings.FeaturesEnabled)
        {
            ReleaseAll(out _);
            return;
        }

        float now = Time.unscaledTime;

        foreach (KeyValuePair<ObjectBase, Hold> entry in new List<KeyValuePair<ObjectBase, Hold>>(Held))
        {
            ObjectBase ship = entry.Key;
            Hold hold = entry.Value;

            try
            {
                // Unity's == reports true for a destroyed object even though the reference is
                // real, so the entry has to be removed by the key that is actually in the map.
                // Through a local, tested: entry.Key is a possible-null argument to Remove
                // (CS8604), and Unity's == reports a destroyed object as equal to null.
                ObjectBase? gone = entry.Key;
                if (ship == null)
                {
                    if (gone != null) Held.Remove(gone);
                    continue;
                }
                if (ModeOf(ship) != Mode.SignatureLock) { Release(ship, out _); continue; }

                ObjectBase? target = hold.Target;

                if (target == null || target.IsDestroyed)
                {
                    ModLog.Info("Offensive ECM: " + Name(ship) + " released "
                                + (target == null ? "an empty track" : Name(target) + " - sunk or gone"));
                    Release(ship, out _);
                    continue;
                }

                double range = SafeRange(Position(ship), target);
                if (range > Reach(target))
                {
                    ModLog.Info("Offensive ECM: " + Name(ship) + " lost the lock on " + Name(target)
                                + " at " + (int)range + " nm - out of range");
                    Release(ship, out _);
                    continue;
                }

                // The window runs out on its own. This is the limit that makes the hold a burst of
                // suppression rather than a permanent state: the mod stops holding the contact's
                // sets down, and switches nothing back on for it - that is the game's own crew.
                if (hold.Jamming && hold.HeldUntil > 0f && now >= hold.HeldUntil)
                {
                    EndHold(ship, hold, (int)HoldSeconds + " s window used up");
                    EcmControl.StandDown(ship, out _);
                }

                if (now < hold.NextAssertAt) continue;
                hold.NextAssertAt = now + AssertSeconds;
                hold.Assertions++;

                // The jam has to survive being replaced under it: the target's crew, its AI, or a
                // later order can all clear the assignment. So the game's own state is read back
                // and the order is only re-issued when the assignment has actually gone - not ten
                // times a second into the multiplayer mod's order forwarding.
                bool identified = ContactPicture.Identified(target) != false;
                bool stillAimed = identified
                    ? OffensiveJam.IsJammingAt(ship, target)
                    : OffensiveJam.IsTransmitting(ship);

                if (!stillAimed)
                {
                    AimTheJammer(ship, hold, out string reissued);

                    if (!hold.Reported || hold.LastReport != "re-issued")
                    {
                        hold.Reported = true;
                        hold.LastReport = "re-issued";
                        ModLog.Info("Offensive ECM: " + Name(ship) + " re-issued the jam on "
                                    + Name(target) + " (" + (int)range + " nm) - " + reissued);
                    }
                }

                // The beam is kept pointed whether or not the suppression half has been ordered:
                // the game's own jam is what makes the ECM real, and it is the lock's whole job.
                // Locked but silent still means the sets are not being held down - that is the
                // second, deliberate order.
                if (!hold.Jamming) continue;

                // A client holds no switches on the host's ships: the jam travels as an order, and
                // writing a replica's sensors would be overwritten on the next frame.
                if (Multiplayer.IsClient) continue;

                // The second half: hold the target's own sets down, which the order does not do.
                // Only for a contact the picture has identified - a track nobody has named gets the
                // game's own jam and nothing more.
                if (!identified) continue;

                EcmControl.Engage(ship, target, false, out _);
                List<SensorSet.Reading> readings = SensorSet.Silence(target);

                bool blind = SensorSet.FullyBlind(readings);
                string summary = readings.Count > 0
                    ? SensorSet.Describe(readings)
                    : "no sensor fit readable";

                if (!hold.Reported || summary != hold.LastReport)
                {
                    hold.Reported = true;
                    hold.LastReport = summary;

                    ModLog.Info("Offensive ECM: " + Name(ship) + " holding " + Name(target) + " ("
                                + (int)range + " nm) - "
                                + (blind ? "every sensor off" : "some sensors still up")
                                + ": " + summary);
                }
            }
            catch (Exception error)
            {
                ModLog.Error("Offensive ECM failed for a ship", error);

                ObjectBase? lost = entry.Key;
                if (lost != null) Held.Remove(lost);
            }
        }
    }

    // --- describing ------------------------------------------------------------------------------

    /// <summary>One line for the menu label: mode, and what the lock is doing.</summary>
    internal static string Describe(ObjectBase? unit)
    {
        if (unit == null) return "off";

        if (ModeOf(unit) == Mode.Standard) return "standard (the game's own)";

        if (Held.TryGetValue(unit, out Hold hold) && hold.Target != null)
        {
            double range = SafeRange(Position(unit), hold.Target);

            string what = Name(hold.Target) + " (" + (int)range + " nm, game: "
                          + OffensiveJam.Status(unit) + ")";

            if (hold.Jamming)
            {
                int left = HoldLeft(unit);
                return "holding " + what + (left > 0 ? ", " + left + " s left" : "");
            }

            float recovering = RecoveringFor(hold.Target);
            if (recovering > 0f)
            {
                return "locked on " + what + " - its crew are up for "
                       + (int)Math.Ceiling(recovering) + " s";
            }

            if (ContactPicture.Identified(hold.Target) == false)
            {
                return "locked on " + what + " - not identified, so the jam is on its bearing";
            }

            if (Multiplayer.IsClient)
            {
                return "locked on " + what + " - the host simulates it, so only the game's ECM";
            }

            if (ChannelsInUse() >= Channels)
            {
                return "locked on " + what + " - no suppression channel free";
            }

            return "locked on " + what + " - its sensors are up, click to hold them down";
        }

        return "signature lock, no lock taken yet";
    }

    /// <summary>
    /// How many contacts are within reach, how many of those are in the air, and how many have not
    /// been identified - the last one because the feature works on them and the menu should say so
    /// rather than leave the player looking for a name that is not there.
    /// </summary>
    internal static string ContactSummary(ObjectBase ship)
    {
        List<ObjectBase> contacts = Contacts(ship);

        int air = 0;
        int unnamed = 0;

        foreach (ObjectBase contact in contacts)
        {
            if (UnitFit.IsAir(contact)) air++;
            if (ContactPicture.Identified(contact) == false) unnamed++;
        }

        return contacts.Count + " contact(s) in range"
               + (air > 0 ? ", " + air + " in the air" : "")
               + (unnamed > 0 ? ", " + unnamed + " unidentified" : "");
    }

    /// <summary>
    /// Names the sensor families this build would not let the mod switch, so the report says which
    /// one is still working instead of a vague "partially".
    /// </summary>
    private static string Stubborn(List<SensorSet.Reading> readings)
    {
        var names = new List<string>();

        foreach (SensorSet.Reading reading in readings)
        {
            if (reading.Stubborn) names.Add(reading.Name);
        }

        return names.Count > 0
            ? string.Join(", ", names) + " would not switch off on this build"
            : "one or more sensors could not be confirmed off on this build";
    }

    private static GeoPosition Position(ObjectBase? unit)
    {
        try { return unit == null ? new GeoPosition(0, 0, 0) : unit.getGeoPosition(); }
        catch (Exception) { return new GeoPosition(0, 0, 0); }
    }

    private static string Name(ObjectBase? unit)
    {
        if (unit == null) return "<none>";

        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }

    /// <summary>
    /// Writes <c>offensive-ecm.txt</c> for one lock: what was read off the target, what was silenced,
    /// and what was not. Written per lock rather than per tick, and appended rather than overwritten,
    /// so a mission's whole jamming history is in one file.
    /// </summary>
    private static void WriteReport(ObjectBase ship, ObjectBase target, string signature,
                                    List<SensorSet.Reading> signatures,
                                    List<SensorSet.Reading> applied, string jammer)
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine();
            text.AppendLine("Sea Power Advanced - offensive ECM lock");
            text.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            + "   mod " + ModInfo.Version);
            text.AppendLine("jammer  " + Name(ship));
            text.AppendLine("target  " + Name(target) + "   (" + UnitFit.Kind(target) + ")   hull "
                            + "signature as read: " + signature);
            text.AppendLine("contact picture read from: " + Sources(ship));
            text.AppendLine("jammer path: " + jammer);
            text.AppendLine();
            text.AppendLine("Signatures read off the target before jamming");
            text.AppendLine("-------------------------------------------");
            if (signatures.Count == 0) text.AppendLine("  (none readable)");
            foreach (SensorSet.Reading reading in signatures) text.AppendLine("  " + Line(reading));
            text.AppendLine();
            text.AppendLine("What the mod then did, per sensor family");
            text.AppendLine("----------------------------------------");
            if (applied.Count == 0) text.AppendLine("  (nothing to switch)");
            foreach (SensorSet.Reading reading in applied) text.AppendLine("  " + Line(reading));
            text.AppendLine();
            if (UnitFit.IsAir(target))
            {
                text.AppendLine("An aircraft carries one radar and one receiver set, not a ship's");
                text.AppendLine("families. Its radar off means blind; its receiver cannot be jammed");
                text.AppendLine("off at all, because a passive set has nothing to switch down.");
                text.AppendLine();
            }

            text.AppendLine("A sensor marked STILL is one this build of the game will not let the mod");
            text.AppendLine("switch off; the menu says 'NOT all sensors off' and names it rather than");
            text.AppendLine("claiming otherwise.");
            text.AppendLine("The hold is re-asserted ten times a second - it is sustained suppression, not");
            text.AppendLine("a permanent state change, and it stops when the lock is released or when the");
            text.AppendLine("hold window below runs out.");
            text.AppendLine();
            text.AppendLine("The two limits on holding a contact down");
            text.AppendLine("---------------------------------------");
            text.AppendLine("  suppression channels : " + ChannelsInUse() + " of " + Channels + " in use ("
                            + ChannelUsers() + ")");
            text.AppendLine("  hold window          : " + (HoldSeconds > 0f
                            ? (int)HoldSeconds + " s, then its crew get their sensors back"
                            : "off - the hold runs until it is stopped"));
            text.AppendLine("  recovery window      : " + (RecoverySeconds > 0f
                            ? (int)RecoverySeconds + " s before the same contact can be held down again"
                            : "off - the hold can be renewed immediately"));
            text.AppendLine();
            text.AppendLine("Neither limit switches anything back on for the enemy. When a hold ends the");
            text.AppendLine("mod simply stops holding the sets down, and the contact's own crew decide");
            text.AppendLine("what to radiate next - which is why a jam buys time rather than silence.");

            string path = ModFolders.AppendText("offensive-ecm.txt", text.ToString());
            ModLog.Info("Offensive ECM report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Could not write offensive-ecm.txt", error);
        }
    }

    private static string Line(SensorSet.Reading reading)
    {
        string state = reading.Verdict.Length > 0
            ? reading.Verdict
            : reading.Radiating == true ? "radiating"
            : reading.Radiating == false ? "quiet"
            : "state not exposed";

        return reading.Name.PadRight(28) + state;
    }
}
