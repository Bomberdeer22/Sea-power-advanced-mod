using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Interop;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// The smoke screen: every hull in the force fires its own decoy launchers at once, so a salvo
/// arriving from one bearing has a screen of radar-reflecting cloud in front of the formation
/// instead of a row of clean hulls.
///
/// <b>This is chaff, not gun-launched smoke.</b> That distinction is the whole feature and it is not
/// a technicality. Sea Power's smoke rounds are ammunition data - an entry under the game's
/// <c>Ammunition</c> folder, referenced by a magazine's own line - and a BepInEx plugin cannot conjure
/// a shell type the game does not have, nor order a gun to fire at a point on the sea. What the game
/// does expose, and what this uses, is <c>ObjectBase.LaunchChaff(bool)</c>: the decoy launchers a hull
/// carries on the deck.
///
/// So the honest description in the menu, the report and the changelog is: a <b>countermeasure
/// screen</b> against radar and infrared terminal seekers. It is not an illumination round, it does
/// nothing to anybody's optical tracking, and the mod does not claim it degrades a laser rangefinder -
/// there is no such sensor in this game to degrade.
///
/// What the mod adds is the part the game does not do: one order, the whole formation, and the
/// launchers fired in the same second rather than one ship at a time while the missile closes. The
/// chaff itself, and whatever the game's seekers make of it, are the game's own.
///
/// <b>Nothing is invented.</b> A hull whose mounts show no launcher is refused and named in the
/// report rather than reported as having popped a decoy it does not carry; a hull that does have one
/// is confirmed by its launcher count, not by the call returning.
/// </summary>
internal static class SmokeScreen
{
    /// <summary>
    /// Fire one screen from every hull under command.
    ///
    /// Returns the number of hulls whose launchers were fired. Out comes a sentence for the menu and
    /// the log, and on every call <c>smoke-screen.txt</c> is written, so "nothing happened" has an
    /// answer in a file rather than in a guess.
    /// </summary>
    internal static int Fire(out string report)
    {
        int fired = 0;
        int without = 0;
        int refused = 0;

        var lines = new List<string>();
        var names = new List<string>();

        List<ObjectBase> fleet = FleetTicker.Tracked();

        if (fleet.Count == 0)
        {
            report = "no ships under command - there is nothing to screen";
            Write("no hulls under command", lines);
            return 0;
        }

        foreach (ObjectBase hull in fleet)
        {
            try
            {
                if (!UnitFit.IsPlayer(hull)) { refused++; continue; }

                string name = Name(hull);

                // A submarine has no deck launchers and is under water besides: it is not that the
                // mod declines, it is that there is nothing there to fire.
                if (UnitFit.IsSubmarine(hull))
                {
                    lines.Add("  " + name + ": submarine - no deck launchers to fire");
                    continue;
                }

                int launchers = LauncherCount(hull);

                if (launchers == 0)
                {
                    // Positive evidence of absence, read from the hull's own mounts: this is the
                    // difference between "no decoys aboard" and "the mod could not tell".
                    without++;
                    lines.Add("  " + name + ": no decoy launcher in its mounts - nothing fired");
                    continue;
                }

                bool sent = SensorControl.LaunchChaff(hull);

                if (sent)
                {
                    fired++;
                    names.Add(name);
                    lines.Add("  " + name + ": chaff away (" + launchers + " launcher(s) fitted)");
                    ModLog.Info("Smoke screen: " + name + " fired chaff");
                }
                else
                {
                    lines.Add("  " + name + ": launcher fitted but the game refused the call - "
                              + "LaunchChaff(bool) is absent or threw on this build");
                    ModLog.Warn("Smoke screen: " + name + ": LaunchChaff was found but did not take");
                }
            }
            catch (Exception error)
            {
                lines.Add("  " + Name(hull) + ": failed (" + error.GetType().Name + ")");
            }
        }

        report = Summary(fired, without, refused, names);

        Write(report, lines);
        return fired;
    }

    /// <summary>The one line the menu and the log carry, phrased so it cannot be read as more than
    /// it is: a countermeasure screen against terminal seekers, fired by the ships themselves.</summary>
    private static string Summary(int fired, int without, int refused, List<string> names)
    {
        if (fired == 0)
        {
            if (without > 0 && refused == 0)
            {
                return "no decoy launchers in this force - carrier-borne smoke rounds are ammunition "
                       + "data, which a mod cannot add; see smoke-screen.txt";
            }

            return "no hull fired: " + without + " with no launcher, " + refused + " not ours to order";
        }

        return fired + " ship(s) fired chaff: " + string.Join(", ", names) + " - a countermeasure "
               + "screen against terminal radar and infrared seekers, not gun smoke and not "
               + "illumination";
    }

    /// <summary>
    /// How many decoy launchers this hull's own mounts show.
    ///
    /// Read from the hull's parameter block, which is the game's own description of the ship. The
    /// check is on the mount's class name rather than a type the project would have to be able to
    /// name at compile time - a hull whose data has no launcher gets zero here, and is refused.
    /// </summary>
    private static int LauncherCount(ObjectBase hull)
    {
        try
        {
            ObjectBaseParameters? blueprint = hull._obp;
            List<WeaponSystem>? systems = blueprint?._weaponSystems;
            if (systems == null) return 0;

            int count = 0;

            foreach (WeaponSystem? system in systems)
            {
                if (system == null) continue;

                // The class name, lowercased: a build that renames the class still says "chaff" or
                // "decoy" or "noisemaker" in the name if that is what the mount is.
                string kind = system.GetType().Name.ToLowerInvariant();

                if (kind.Contains("chaff") || kind.Contains("decoy") || kind.Contains("noisemaker")
                    || kind.Contains("countermeasure"))
                {
                    count++;
                }
            }

            return count;
        }
        catch (Exception) { return 0; }
    }

    /// <summary>The hull's name for the menu and the report: the short display name first, then
    /// <c>getName()</c>, then the type - the same order every other feature uses.</summary>
    private static string Name(ObjectBase? unit)
    {
        if (unit == null) return "a ship";

        try
        {
            if (!string.IsNullOrEmpty(unit.DisplayNameShort)) return unit.DisplayNameShort;
        }
        catch (Exception) { }

        try
        {
            string? plain = unit.getName();
            if (!string.IsNullOrWhiteSpace(plain)) return plain!.Trim();
        }
        catch (Exception) { }

        try { return unit.GetType().Name; }
        catch (Exception) { return "a ship"; }
    }

    /// <summary>Writes <c>smoke-screen.txt</c>: what was fired, by which hull, and what was not and
    /// why. Written on every order, including the orders that fire nothing.</summary>
    private static void Write(string verdict, List<string> lines)
    {
        try
        {
            var text = new System.Text.StringBuilder();

            text.AppendLine("Sea Power Advanced - the smoke screen");
            text.AppendLine("written " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            + "   mod " + ModInfo.Version);
            text.AppendLine("outcome: " + verdict);
            text.AppendLine();

            text.AppendLine("What this order does, and what it does not");
            text.AppendLine("------------------------------------------");
            text.AppendLine("  It fires every hull's own decoy launchers at once, through the game's");
            text.AppendLine("  ObjectBase.LaunchChaff(bool). What the chaff then does to a seeker is the");
            text.AppendLine("  game's business and the game's maths, not the mod's.");
            text.AppendLine();
            text.AppendLine("  It is a COUNTERMEASURE screen, not gun smoke and not illumination:");
            text.AppendLine("    - it degrades nothing optical. This game has no laser sensor to degrade,");
            text.AppendLine("      and the mod will not pretend to blind one;");
            text.AppendLine("    - it is not a star shell. Rounds are ammunition DATA in this game - a");
            text.AppendLine("      file under Ammunition/ referenced by a magazine - and a plugin cannot");
            text.AppendLine("      conjure a shell type the install does not have, nor order a gun to");
            text.AppendLine("      fire at a point on the sea. Both would need a lever this mod does not");
            text.AppendLine("      have, and inventing the appearance of one is how a feature becomes a");
            text.AppendLine("      decoration.");
            text.AppendLine();

            text.AppendLine("The force");
            text.AppendLine("---------");

            if (lines.Count == 0)
            {
                text.AppendLine("  no hulls to report");
            }
            else
            {
                foreach (string line in lines) text.AppendLine(line);
            }

            text.AppendLine();
            text.AppendLine("What to send back if this does nothing");
            text.AppendLine("-------------------------------------");
            text.AppendLine("  Send this file. It separates the three cases that look identical from the");
            text.AppendLine("  game: no launcher in the hull's mounts (its data, not the mod), a launcher");
            text.AppendLine("  present but the call refused (a build whose LaunchChaff differs), and the");
            text.AppendLine("  call made successfully - after which the chaff is the game's to use.");
            text.AppendLine("  capabilities.txt already says whether LaunchChaff(bool) exists at all.");

            string path = ModFolders.WriteText("smoke-screen.txt", text.ToString());
            ModLog.Info("Smoke screen: report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Smoke screen: could not write smoke-screen.txt", error);
        }
    }
}
