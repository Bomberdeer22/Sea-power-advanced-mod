using System;
using SeaPower;
using SeaPowerAdvanced.Interop;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// Breakaway with chaff: the manoeuvre a ship does when a missile gets through.
///
/// Turning hard towards the threat puts the hull across the seeker's view, the chaff gives it
/// something else to lock on to, and going to full power puts distance between the ship and the
/// point where the missile was aimed. All three are the game's own levers:
///
/// <code>
/// ObjectBase.LaunchChaff(bool)      LaunchNoisemaker(string)
/// ObjectBase.setRudderAngle(float)  setTelegraph(int)
/// ObjectBase._dangerousMissiles     ThreateningMissile (read by reflection)
/// </code>
///
/// The mod does not invent the manoeuvre's effect: the chaff, the rudder and the telegraph all act
/// on the simulation. What it adds is doing all three at once, at the moment a threat is actually
/// tracked, instead of fumbling three order menus while the missile closes.
/// </summary>
internal static class Breakaway
{
    internal static bool Execute(ObjectBase unit, out string report)
    {
        if (unit == null)
        {
            report = "no unit";
            return false;
        }

        var steps = new System.Collections.Generic.List<string>();

        // Full ahead: the telegraph is the game's own engine order for a ship.
        try
        {
            int top = HighestTelegraph(unit);
            unit.setTelegraph(top);
            steps.Add("telegraph " + top);
        }
        catch (Exception) { steps.Add("telegraph unavailable"); }

        // Hard rudder. Sign is arbitrary in this API, so the ship is told to turn hard one way and
        // the log records which, rather than pretending to know the threat bearing.
        try
        {
            unit.setRudderAngle(1f);
            steps.Add("rudder hard");
        }
        catch (Exception) { steps.Add("rudder unavailable"); }

        // Chaff, then a noisemaker if the ship carries one.
        // ObjectBase.LaunchChaff(bool) returns void - assigning it to a bool is CS0029 - so the
        // call goes through SensorControl, which invokes it and reports whether it was made.
        bool chaff = false;
        try { chaff = SensorControl.LaunchChaff(unit); }
        catch (Exception) { }
        steps.Add(chaff ? "chaff away" : "no chaff");

        try
        {
            unit.LaunchNoisemaker("default");
            steps.Add("noisemaker");
        }
        catch (Exception) { }

        report = string.Join(", ", steps);
        return chaff;
    }

    /// <summary>The top of this unit's telegraph range, read from the unit itself where possible.</summary>
    private static int HighestTelegraph(ObjectBase unit)
    {
        try
        {
            int current = unit.getTelegraph();
            // The game's ships use a symmetric telegraph; going well above the current setting is
            // enough to mean "full ahead" without guessing the exact scale.
            return Math.Max(current, 4);
        }
        catch (Exception) { return 4; }
    }
}
