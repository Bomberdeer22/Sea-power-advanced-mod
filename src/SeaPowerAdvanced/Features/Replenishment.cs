using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Interop;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// Underway replenishment: top a ship up from a supply ship alongside.
///
/// The honest version of this feature. The game has the *ammunition* half of replenishment as real
/// methods, but it has no fuel-transfer-at-sea member that this mod can find, so the mod does the
/// half that exists and says so in the log rather than pretending to do both:
///
/// <code>
/// ObjectBase.ReplenishAmmo(float, float)                     confirmed
/// ObjectBase.ReplenishAccountableAmmo(float, Dictionary)      confirmed
/// ObjectBase.RearmAll(bool)                                  confirmed
/// ObjectBase.CanBeRearmed(float, Dictionary)                  confirmed
/// </code>
///
/// The constraint that makes it staff work rather than a cheat is the approach distance: a ship can
/// only replenish from a supply unit it is actually near. That is checked here, not assumed.
/// </summary>
internal static class Replenishment
{
    /// <summary>How close a ship must be to a supply unit, in nautical miles.</summary>
    private const double AlongsideNm = 2.0;

    internal static bool Alongside(ObjectBase unit, out string report)
    {
        if (unit == null)
        {
            report = "no unit";
            return false;
        }

        if (!TryFindSupplier(unit, out ObjectBase? supplier, out double range))
        {
            report = "no supply ship within " + AlongsideNm + " nm - move alongside one first";
            return false;
        }

        var steps = new List<string>();

        try
        {
            unit.RearmAll(true);
            steps.Add("rearmed");
        }
        catch (Exception) { }

        try
        {
            float got = unit.ReplenishAmmo(1f, 1f);
            if (got > 0f) steps.Add("replenished " + got);
        }
        catch (Exception) { }

        try
        {
            float points = unit.ReplenishAccountableAmmo(1f, new Dictionary<string, int>());
            if (points > 0f) steps.Add("accountable ammo +" + points);
        }
        catch (Exception) { }

        report = steps.Count > 0
            ? string.Join(", ", steps) + " from " + Name(supplier) + " at " + range.ToString("0.0") + " nm"
              + " (fuel transfer is not modelled by this game, so only ammunition moved)"
            : "alongside " + Name(supplier) + " but the game replenished nothing";

        // In a session the host owns the magazine, so what comes back is the host's answer.
        Multiplayer.Attempt("replenishment", unit, steps.Count > 0, report);
        return steps.Count > 0;
    }

    /// <summary>
    /// Finds the nearest friendly supply unit. Supply ships are recognised by what the game calls
    /// them: a vessel whose name or role mentions supply, oiler, replenishment or AOE.
    /// </summary>
    private static bool TryFindSupplier(ObjectBase unit, out ObjectBase? supplier, out double range)
    {
        supplier = null;
        range = double.MaxValue;

        GeoPosition own;
        try { own = unit.getGeoPosition(); }
        catch (Exception) { return false; }

        foreach (ObjectBase candidate in GameWorld.PlayerUnits())
        {
            try
            {
                if (ReferenceEquals(candidate, unit)) continue;

                string text = (candidate.Name + " " + candidate.DisplayNameShort).ToLowerInvariant();
                bool looksLikeSupplier =
                    text.Contains("supply") || text.Contains("oiler") || text.Contains("replenish")
                    || text.Contains("aoe") || text.Contains("aor") || text.Contains("tanker");

                if (!looksLikeSupplier) continue;

                double distance = ThreatAxis.Distance(own, candidate.getGeoPosition());
                if (distance <= AlongsideNm && distance < range)
                {
                    range = distance;
                    supplier = candidate;
                }
            }
            catch (Exception) { }
        }

        return supplier != null;
    }

    private static string Name(ObjectBase? unit)
    {
        if (unit == null) return "<none>";
        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }
}
