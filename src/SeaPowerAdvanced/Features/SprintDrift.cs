using System;
using SeaPower;
using SeaPowerAdvanced.Interop;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// Sprint-and-drift ASW: go fast to close the datum, go quiet to listen, repeat.
///
/// The member dump changed this feature from "write a doctrine" to "use the one that exists":
///
/// <code>
/// ReactiveProperty&lt;bool&gt; SprintAndDriftDoctrine     the doctrine flag, per unit
/// virtual float CavitationOnsetKts()                    the game's own quiet-speed threshold
/// ObjectBase.TowedSonarsSetActive(bool)                 the array that does the listening
/// ReactiveProperty&lt;bool&gt; CavitationForbidden          the game's own cavitation gate
/// </code>
///
/// So the mod sets the doctrine, deploys the array, and - where the player asks for it - applies the
/// doctrine to the whole task force at once, which is the staff action the game's UI does not offer.
/// The quiet speed is reported from the game's own cavitation threshold rather than guessed, so what
/// the logs say is what the simulation actually uses.
/// </summary>
internal static class SprintDrift
{
    internal static bool Toggle(ObjectBase unit, out string report)
    {
        if (unit == null)
        {
            report = "no unit";
            return false;
        }

        bool? current = SensorControl.GetSprintDoctrine(unit);
        bool target = !(current ?? false);

        if (!SensorControl.SetSprintDoctrine(unit, target))
        {
            report = "this build did not accept the sprint-and-drift doctrine flag";
            return false;
        }

        if (target)
        {
            // The doctrine is only useful with ears in the water.
            if (SensorControl.HasTowedArray(unit)) SensorControl.SetTowedSonar(unit, true);
        }

        float? quiet = SensorControl.CavitationOnsetKnots(unit);
        report = (target ? "sprint and drift on" : "sprint and drift off")
                 + (quiet.HasValue
                     ? "; the game cavitates this hull above " + quiet.Value.ToString("0.0") + " kts"
                     : "; cavitation threshold not readable on this hull");

        return true;
    }

    /// <summary>Sets the doctrine for every ship under command in one action.</summary>
    internal static int SetFleet(bool on, out string report)
    {
        int changed = 0;
        int total = 0;

        foreach (ObjectBase ship in FleetTicker.Tracked())
        {
            total++;
            try
            {
                if (!SensorControl.SetSprintDoctrine(ship, on)) continue;
                if (on && SensorControl.HasTowedArray(ship)) SensorControl.SetTowedSonar(ship, true);
                changed++;
            }
            catch (Exception error)
            {
                ModLog.Error("Sprint-and-drift could not be set on a ship", error);
            }
        }

        report = (on ? "sprint and drift on" : "sprint and drift off") + " for "
                 + changed + "/" + total + " ship(s)";
        return changed;
    }
}
