using System;
using System.Collections.Generic;
using System.Text;
using SeaPower;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// A datalink between the forces two players command.
///
/// The problem it exists for is one a co-op mission can create by itself. A mission with a side per
/// player - Blue for the host, Cyan for the guest, the two marked friendly, both hostile to Red - has
/// <b>two task forces</b>, and every contact picture in this game belongs to a task force. Each
/// machine detects with its own sensors and writes what it found to its own force's table, so the two
/// players do not automatically share a picture the way they do when they both command <i>one</i>
/// force. The host ends up holding a track the guest has never been given, and the reverse. That is
/// what "I could see him and he could not see it" looks like from the inside.
///
/// What this does about it: on a sweep, every contact the local picture holds is <b>handed to the
/// other force</b> through the game's own intel-sharing call
/// (<c>Utils.RevealContactToObject</c>, see <see cref="DatalinkControl"/>), which is the route a
/// reconnaissance report or a radio link uses. The game builds the track; the mod supplies only the
/// pairing, and it supplies it position-only, so the receiving side learns that something is there
/// and where - never whose it is, what class, or that anybody has identified it.
///
/// Three rules keep it honest:
///
/// <list type="number">
/// <item><b>Only tracks this machine really holds.</b> The source is
///   <see cref="ContactPicture.Hostiles"/> - the player's own plotting table and spotted list, the
///   same picture the offensive jammer is aimed from. Nothing is invented, and a contact nobody can
///   see is never relayed.</item>
/// <item><b>Only to a force that is friendly by evidence.</b> A target force is refused if it is the
///   game's neutral force, the game's enemy force, or a force any of whose units is in our own
///   picture as a <i>contact</i> - in which case we are already treating it as hostile and handing it
///   our picture would be the opposite of a favour. On top of that the whole feature is off unless
///   the multiplayer mod says the session is a co-op one: in a PvP session the other player is an
///   opponent, and that mod deliberately keeps the pictures apart.</item>
/// <item><b>Nothing is claimed that was not seen.</b> Every sweep re-reads the target force's table
///   after revealing, and counts what is actually in it. A build whose reveal writes only to the
///   player's own side would otherwise produce a switch that reports success every time and changes
///   nothing; here that is detected, reported in <c>datalink.txt</c>, and the revealing stops after
///   three sweeps instead of pretending.</item>
/// </list>
///
/// And one limit that is stated rather than worked around: <b>this runs on the machine that calls
/// it.</b> It makes the picture on <i>this</i> machine hold what this machine's sensors hold for the
/// allied force. What the other player's screen shows is decided on the other machine, by its own
/// detection and by the multiplayer mod's contact sync - which this feature reads, reports, and can
/// ask to resynchronise, but cannot replace. <c>datalink.txt</c> says exactly which of those is
/// working on the install it was written from.
/// </summary>
internal static class Datalink
{
    /// <summary>A switch in the menu for this session; null means "follow the setting".</summary>
    private static bool? switched;

    private static float nextSweep;
    private static bool engaged;
    private static int sweeps;

    /// <summary>The last sweep, kept for the menu entry and the report.</summary>
    private static string lastVerdict = "nothing swept yet";
    private static float lastLoggedAt = -60f;
    private static int relayed;
    private static int alreadyHeld;
    private static int refusedForces;

    /// <summary>Forces whose table did not gain anything we handed them, and how many times.</summary>
    private static readonly Dictionary<object, int> Unproven = new();

    internal static bool On => switched ?? ModSettings.CoopDatalink;

    /// <summary>Switches the datalink on or off for this session and says what it found.</summary>
    internal static bool Toggle(out string report)
    {
        switched = !On;

        if (!On)
        {
            engaged = false;
            report = "datalink off - this force's picture is no longer handed to any other force";
            ModLog.Info("Datalink: " + report);
            WriteReport("switched off");
            return true;
        }

        // Sweep immediately, so the click's answer is evidence rather than a promise.
        Sweep(force: true);

        report = lastVerdict;
        ModLog.Info("Datalink: " + report);
        WriteReport("switched on");
        return true;
    }

    /// <summary>One line for the menu label.</summary>
    internal static string Describe()
    {
        if (!On) return "off";

        if (sweeps == 0) return "on, waiting for the first sweep";

        string state = relayed > 0
            ? "sharing " + relayed + " track(s)"
            : "on, nothing to pass on yet";

        if (refusedForces > 0) state += ", " + refusedForces + " force(s) left out";

        return state;
    }

    /// <summary>Asks the multiplayer mod to re-run its own contact sweeps, and sweeps here too.</summary>
    internal static bool Resync(out string report)
    {
        Sweep(force: true);

        string theirs = Multiplayer.Picture.Resync();

        report = "this machine: " + lastVerdict + ". Other side: " + theirs + ".";
        ModLog.Info("Datalink: " + report);
        WriteReport("resync asked for");
        return true;
    }

    /// <summary>Called from the ticker.</summary>
    internal static void Tick()
    {
        if (!ModSettings.FeaturesEnabled) return;

        if (!On)
        {
            if (engaged)
            {
                engaged = false;
                ModLog.Info("Datalink: off - no further tracks are being handed to other forces. The "
                            + "tracks already passed age out on the game's own sensor clock, which is "
                            + "what a link being cut looks like.");
            }
            return;
        }

        // Nothing to do outside a session, and this feature is on by default: sweeping (and logging)
        // every three seconds through a single-player game would be pure noise, so the session test
        // comes before the clock.
        if (!Multiplayer.InSession)
        {
            if (engaged)
            {
                engaged = false;
                ModLog.Info("Datalink: session ended - nothing further is being handed over");
            }

            if (sweeps > 0)
            {
                sweeps = 0;
                relayed = 0;
                alreadyHeld = 0;
                lastVerdict = "no multiplayer session";
            }

            return;
        }

        float interval = Mathf.Clamp(ModSettings.CoopDatalinkSeconds, 1f, 30f);
        if (Time.unscaledTime < nextSweep) return;

        nextSweep = Time.unscaledTime + interval;
        Sweep(force: false);
    }

    /// <summary>
    /// One sweep: decide who may be given the picture, hand over what they do not hold, then read
    /// their table back to see whether the game took it.
    /// </summary>
    private static void Sweep(bool force)
    {
        sweeps++;

        relayed = 0;
        alreadyHeld = 0;
        refusedForces = 0;

        object? player = DatalinkControl.PlayerTaskforce();

        if (player == null)
        {
            Set("no player task force yet - the datalink has nothing to share", force);
            return;
        }

        // The session decides whether there is a friend to share with at all. A co-op flag that is
        // off is the single commonest reason two co-op players see different pictures, so it is
        // reported as the answer rather than as a footnote.
        bool? shared = Multiplayer.Picture.Shared(out string sessionWhy);

        if (shared != true)
        {
            engaged = false;
            Set(sessionWhy, force);
            return;
        }

        if (!DatalinkControl.CanReveal)
        {
            engaged = false;
            Set("this build exposes no intel-sharing call (" + DatalinkControl.RevealSource + ")", force);
            return;
        }

        List<ContactPicture.Contact> contacts = ContactPicture.Hostiles();

        if (contacts.Count == 0)
        {
            engaged = false;
            Set("this force holds no contacts to pass on", force);
            return;
        }

        List<DatalinkControl.Force> forces = DatalinkControl.Forces();

        if (forces.Count < 2)
        {
            engaged = false;
            Set("only one force is present on this machine, so there is nobody to share with", force);
            return;
        }

        object? neutral = DatalinkControl.StaticTaskforce("_neutralTaskforce");
        object? enemy = DatalinkControl.StaticTaskforce("_enemyTaskforce");

        var parts = new List<string>();
        engaged = false;

        foreach (DatalinkControl.Force other in forces)
        {
            try
            {
                if (other.Is(player)) continue;

                string refusal = Refusal(other, neutral, enemy, contacts);

                if (refusal.Length > 0)
                {
                    refusedForces++;
                    parts.Add(other.Name + ": not shared with - " + refusal);
                    continue;
                }

                int handed = Share(other, contacts, out int held, out string note);

                relayed += handed;
                alreadyHeld += held;

                engaged |= handed > 0;

                parts.Add(other.Name + ": " + note);
            }
            catch (Exception error)
            {
                parts.Add(other.Name + ": failed (" + error.GetType().Name + ")");
            }
        }

        Set(parts.Count == 0 ? "no other force found in the world" : string.Join("; ", parts.ToArray()), force);
    }

    /// <summary>Why a force may not be given the picture, or an empty string when it may.</summary>
    private static string Refusal(DatalinkControl.Force other, object? neutral, object? enemy,
                                  List<ContactPicture.Contact> contacts)
    {
        if (other.Taskforce == null) return "it has no task force";

        if (neutral != null && other.Is(neutral)) return "it is the game's neutral force";

        if (enemy != null && other.Is(enemy)) return "it is the game's enemy force";

        // The decisive test, and it is the game's own picture doing the deciding: a unit we are
        // currently plotting as a contact is a unit we are already treating as hostile.
        foreach (ObjectBase unit in other.Units)
        {
            foreach (ContactPicture.Contact contact in contacts)
            {
                if (ReferenceEquals(contact.Unit, unit))
                    return "we are plotting " + SafeName(unit) + " as a contact, so that force is hostile";
            }
        }

        if (other.Detector == null) return "it has no unit that could hold a track";

        if (DatalinkControl.TableOf(other.Taskforce) == null)
            return "its plotting table could not be read on this build";

        if (Unproven.TryGetValue(other.Taskforce, out int strikes) && strikes >= 3)
            return "the game did not put the tracks into its picture (see datalink.txt)";

        return "";
    }

    /// <summary>
    /// Hands every contact the force does not already hold to one of its units, then counts what the
    /// force's table gained.
    /// </summary>
    private static int Share(DatalinkControl.Force other, List<ContactPicture.Contact> contacts,
                             out int held, out string note)
    {
        held = 0;
        int failed = 0;

        // The force's picture is read once, before, and once again after the handing - and the
        // "does it already hold this" question is asked of that first read rather than of the table,
        // so one sweep walks the other force's table once, not once per contact.
        DatalinkControl.Snapshot before = DatalinkControl.Read(DatalinkControl.TableOf(other.Taskforce));
        int handed = 0;
        string refusal = "";

        foreach (ContactPicture.Contact contact in contacts)
        {
            try
            {
                ObjectBase unit = contact.Unit;
                if (unit == null || unit.IsDestroyed) continue;

                // A force that detects the track with its own sensors keeps its own fix - errors,
                // age and all - and is not refreshed from here. A track it holds only because it
                // was handed one is refreshed every sweep, which is what stops it ageing out: an
                // air track lives about ten seconds without an update.
                if (before.Holds(unit) && before.DetectedHere(unit) != false)
                {
                    held++;
                    continue;
                }

                if (!DatalinkControl.Reveal(other.Detector, unit, out string why))
                {
                    failed++;
                    if (refusal.Length == 0) refusal = why;
                    continue;
                }

                handed++;
            }
            catch (Exception) { failed++; }
        }

        // Verification. The table is read again from the game, not from the count above: what matters
        // is whether the force's picture gained the track, not whether the call returned.
        DatalinkControl.Snapshot after = DatalinkControl.Read(DatalinkControl.TableOf(other.Taskforce));
        int inPicture = 0;

        foreach (ContactPicture.Contact contact in contacts)
        {
            if (after.Holds(contact.Unit)) inPicture++;
        }

        if (handed > 0 && inPicture == 0)
        {
            int strikes = Unproven.TryGetValue(other.Taskforce!, out int had) ? had + 1 : 1;
            Unproven[other.Taskforce!] = strikes;

            if (strikes == 3)
            {
                ModLog.Warn("Datalink: " + other.Name + " did not gain any of the " + handed
                            + " track(s) handed to it, so the mod stops handing it more. The game's "
                            + "reveal call on this build may write only to the player's own force - "
                            + "datalink.txt records it.");
            }

            note = "handed " + handed + ", but its picture did not gain them"
                   + (strikes < 3 ? " (" + strikes + " of 3)" : " - stopped");
            return handed;
        }

        note = handed > 0
            ? "handed " + handed + " new track(s), now holding " + inPicture
              + (held > 0 ? "; " + held + " it already had" : "")
            : "its own sensors already hold " + held + " of the contacts this force can see";

        if (failed > 0) note += "; " + failed + " refused (" + refusal + ")";

        return handed;
    }

    /// <summary>
    /// Records the sweep's verdict, and logs it when the player asked for it (a click) or when it has
    /// genuinely changed - at most once every twenty seconds, because the verdict carries track counts
    /// and those move most sweeps. A feature that writes a line every three seconds is a feature whose
    /// log nobody reads.
    /// </summary>
    private static void Set(string verdict, bool log)
    {
        bool changed = !string.Equals(verdict, lastVerdict, StringComparison.Ordinal);
        lastVerdict = verdict;

        if (!log && !changed) return;
        if (!log && Time.unscaledTime - lastLoggedAt < 20f) return;

        lastLoggedAt = Time.unscaledTime;
        ModLog.Info("Datalink: " + verdict);
    }

    // --- the file --------------------------------------------------------------------------------

    /// <summary>
    /// Writes <c>datalink.txt</c>: what this machine holds, which force each track was handed to,
    /// what the receiving force's own table says, and what the multiplayer mod's contact sync is
    /// doing - because that, and not this feature, is what decides the other player's screen.
    /// </summary>
    internal static void WriteReport(string why)
    {
        try
        {
            var text = new StringBuilder();

            text.AppendLine();
            text.AppendLine("Sea Power Advanced - the force datalink");
            text.AppendLine("======================================");
            text.AppendLine("written: " + why);
            text.AppendLine();

            text.AppendLine("Session");
            text.AppendLine("-------");
            text.AppendLine("  host or client:   " + Multiplayer.Describe());
            text.AppendLine("  co-op picture:    " + Multiplayer.Picture.Shared(out string sessionWhy));
            text.AppendLine("  why:              " + sessionWhy);
            text.AppendLine("  its switches:     " + Multiplayer.Picture.Switches());
            text.AppendLine("  sharing call:     " + DatalinkControl.RevealSource);
            text.AppendLine("  switch here:      " + (On ? "on" : "off")
                            + " (setting CoopDatalink, and the menu entry)");
            text.AppendLine();

            text.AppendLine("The last sweep");
            text.AppendLine("--------------");
            text.AppendLine("  sweeps run:       " + sweeps);
            text.AppendLine("  verdict:          " + lastVerdict);
            text.AppendLine("  tracks handed:    " + relayed);
            text.AppendLine("  already held:     " + alreadyHeld);
            text.AppendLine();

            object? player = DatalinkControl.PlayerTaskforce();

            text.AppendLine("Forces on this machine");
            text.AppendLine("----------------------");

            int shown = 0;

            foreach (DatalinkControl.Force force in DatalinkControl.Forces())
            {
                shown++;

                bool ours = force.Is(player);
                object? table = DatalinkControl.TableOf(force.Taskforce);
                DatalinkControl.Snapshot picture = DatalinkControl.Read(table);

                text.AppendLine("  " + force.Name + (ours ? "   [this machine's player force]" : ""));
                text.AppendLine("    units:          " + force.Units.Count);
                text.AppendLine("    plotting table: " + (table == null ? "unreadable" : "read"));
                text.AppendLine("    tracks held:    "
                                + (table == null ? "?" : picture.Tracks.Count.ToString()));

                if (table != null)
                {
                    int detected = 0;
                    foreach (ObjectBase track in picture.Tracks)
                    {
                        if (picture.DetectedHere(track) == true) detected++;
                    }

                    text.AppendLine("    detected here:  " + detected + " of " + picture.Tracks.Count
                                    + " (the rest are tracks this force was given)");
                }
            }

            if (shown == 0)
            {
                text.AppendLine("  no forces found - the game's unit list was empty or unreadable");
            }

            text.AppendLine();
            text.AppendLine("What this feature can and cannot do");
            text.AppendLine("-----------------------------------");
            text.AppendLine("  It hands the picture this machine holds to the other force through the game's");
            text.AppendLine("  own intel-sharing call, one track at a time, position only. Identity, class");
            text.AppendLine("  and identification are never passed: the receiving force gets a bearing and a");
            text.AppendLine("  position, which is what a datalink gives a watch team.");
            text.AppendLine();
            text.AppendLine("  It runs on THIS machine. What the other player's screen shows is decided on");
            text.AppendLine("  their machine, by their own sensors, and by the multiplayer mod's contact");
            text.AppendLine("  sync - which is the only thing that carries a contact from one computer to");
            text.AppendLine("  another. That mod shares only when it considers the session co-op:");
            text.AppendLine("  in a mission with a side per player it may decide this is PvP, and then the");
            text.AppendLine("  two pictures are meant to differ and nothing here can change that.");
            text.AppendLine();
            text.AppendLine("  If the co-op picture above reads 'off', or the switches read CfgPvP=on,");
            text.AppendLine("  that is the answer to two players seeing different contacts: turn PvP off");
            text.AppendLine("  in the multiplayer mod's own settings before loading the mission.");
            text.AppendLine("  This mod cannot set that switch for it - it is read, not written.");

            string path = ModFolders.WriteText("datalink.txt", text.ToString());
            ModLog.Info("Datalink: report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Datalink: could not write datalink.txt", error);
        }
    }

    private static string SafeName(ObjectBase? unit)
    {
        try { return unit == null ? "<unit>" : unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }
}
