using System;
using SeaPower;
using SeaPowerAdvanced.Interop;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// Commercial signature spoofing: look like a merchant on someone else's plot.
///
/// The readiness report called this PARTIAL, correctly. What the game has:
///
/// <code>
/// SensorData.RCS (string)                       the signature a unit presents
/// SimpleObjectParameters.set_Signature(...)     the signature type
/// ObjectBase.EnableAllRadars/DisableAllRadars   what the unit emits
/// ObjectBase.setTelegraph(int)                  how fast it moves
/// </code>
///
/// What it does not have is a hook that makes an *enemy's* detection routine consult a value we set.
/// So this feature is deliberately three-quarters honest and the last quarter is stated plainly in
/// the log: the ship stops radiating like a warship, throttles to a merchant's speed, and presents
/// the signature string of a civilian hull. Whether the opposing AI is fooled depends on code this
/// mod cannot reach, so the mod reports what it changed and never claims the deception succeeded.
/// </summary>
internal static class MerchantDisguise
{
    /// <summary>A merchant's economical speed, in knots.</summary>
    private const int MerchantTelegraph = 2;

    internal static bool IsDisguised(ObjectBase unit) => unit != null && UnitState.Of(unit).Disguised;

    internal static bool Toggle(ObjectBase unit, out string report)
    {
        if (unit == null)
        {
            report = "no unit";
            return false;
        }

        UnitRecord record = UnitState.Of(unit);
        return record.Disguised ? Drop(unit, record, out report) : Assume(unit, record, out report);
    }

    private static bool Assume(ObjectBase unit, UnitRecord record, out string report)
    {
        var steps = new System.Collections.Generic.List<string>();

        bool? radars = null;
        try { radars = SensorControl.GetSurfaceSearchOn(unit); }
        catch (Exception) { }

        record.DisguiseRadarsWereOn = radars ?? true;
        record.Disguised = true;
        record.DisguiseSetAt = UnityEngine.Time.unscaledTime;

        if (SensorControl.SetAllRadars(unit, false)) steps.Add("dark");
        if (SensorControl.SetEmcon(unit, true)) steps.Add("EMCON on");

        // Merchant's signature string, if this build exposes one on the unit's parameters.
        try
        {
            object? parameters = Capabilities.ParametersOf(unit);
            if (parameters != null)
            {
                System.Reflection.MemberInfo? signature = MemberProbe.Value(parameters.GetType(),
                    "signature", "Signature", "rcs", "RCS");
                if (signature != null)
                {
                    object? current = MemberProbe.Read(signature, parameters);
                    string? text = current as string;
                    record.OriginalSignature = text;

                    string? merchant = MerchantSignature(text);
                    if (merchant != null && MemberProbe.Write(signature, parameters, merchant))
                        steps.Add("signature -> " + merchant);
                }
            }
        }
        catch (Exception) { }

        try
        {
            unit.setTelegraph(MerchantTelegraph);
            steps.Add("telegraph " + MerchantTelegraph);
        }
        catch (Exception) { }

        report = steps.Count > 0
            ? "disguise on: " + string.Join(", ", steps)
              + ". The mod cannot make an enemy's radar read our signature, so treat this as a "
              + "reduced emission profile, not proven deception."
            : "disguise on, but no lever responded in this build";

        return steps.Count > 0;
    }

    /// <summary>
    /// A civilian-looking signature derived from the hull's own. The point is that the string a
    /// contact presents stops reading like a warship, which is what the game's own signature column
    /// shows; nothing here claims the opposing AI re-evaluates it.
    /// </summary>
    private static string? MerchantSignature(string? original)
    {
        if (string.IsNullOrWhiteSpace(original)) return null;

        // "DDG-51" -> "MV"; anything already civilian-looking is left alone.
        string trimmed = original!.Trim();
        if (trimmed.StartsWith("MV", StringComparison.OrdinalIgnoreCase)) return trimmed;
        return "MV " + trimmed;
    }

    private static bool Drop(ObjectBase unit, UnitRecord record, out string report)
    {
        // How long it was up, which is the only thing that makes the timestamp worth keeping.
        float held = UnityEngine.Time.unscaledTime - record.DisguiseSetAt;
        ModLog.Info("Disguise lifted after " + UnityEngine.Mathf.RoundToInt(held) + "s");

        record.Disguised = false;

        if (record.OriginalSignature != null)
        {
            try
            {
                object? parameters = Capabilities.ParametersOf(unit);
                if (parameters != null)
                {
                    System.Reflection.MemberInfo? signature = MemberProbe.Value(parameters.GetType(),
                        "signature", "Signature", "rcs", "RCS");
                    MemberProbe.Write(signature, parameters, record.OriginalSignature);
                    record.OriginalSignature = null;
                }
            }
            catch (Exception) { }
        }

        bool restored = SensorControl.SetAllRadars(unit, record.DisguiseRadarsWereOn);
        SensorControl.SetEmcon(unit, false);

        try { unit.setTelegraph(3); }
        catch (Exception) { }

        report = restored
            ? "disguise off: radars back as they were"
            : "disguise off, but the radars did not respond";
        return restored;
    }
}
