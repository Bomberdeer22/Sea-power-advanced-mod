using System;
using System.Text;
using SeaPowerAdvanced.Menu;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// Writes <c>capabilities.txt</c> next to the mod DLL: what the mod found in this build of the
/// game, what it means, and what to do about anything missing.
///
/// This exists so the next round of questions can be answered from the user's own machine instead
/// of from another offline scan. It is written at startup, every run, because a game patch can
/// change the answers and a stale file would be worse than none.
/// </summary>
internal static class CapabilityReport
{
    internal static void Write()
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine("Sea Power Advanced - capabilities found in this game build");
            text.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            + "   mod " + ModInfo.Version);
            text.AppendLine();
            text.AppendLine("ok   = the member was found and can be called");
            text.AppendLine("--   = not found; the feature that needs it falls back or stays off");
            text.AppendLine();
            text.AppendLine("Every lookup is by name with a signature check, so a member listed here");
            text.AppendLine("was matched on its parameter types as well as its name.");
            text.AppendLine();

            foreach (string line in Capabilities.Describe()) text.AppendLine(line);

            text.AppendLine();
            text.AppendLine("Sensor, doctrine and countermeasure levers");
            text.AppendLine("----------------------------------------");
            foreach (string line in SensorControl.Describe()) text.AppendLine(line);

            text.AppendLine();
            text.AppendLine("Electronic countermeasures");
            text.AppendLine("--------------------------");
            foreach (string line in EcmControl.Describe()) text.AppendLine(line);
            text.AppendLine("  ecm-report.txt lists every member and type the scan found.");
            text.AppendLine();
            text.AppendLine("The jammer as the game keeps it (offensive ECM)");
            text.AppendLine("----------------------------------------------");
            text.AppendLine("  " + OffensiveJam.Summary());
            text.AppendLine("  A jammer is a sensor system in the unit's parameter block, not a member");
            text.AppendLine("  of the unit. offensive-ecm.txt records what was found and what it jammed.");

            text.AppendLine();
            text.AppendLine("Context menu sections found in this build");
            text.AppendLine("-----------------------------------------");
            text.AppendLine("  " + MenuRouting.Describe());
            text.AppendLine("  The advanced features are routed by keyword into these sections. If a");
            text.AppendLine("  feature landed somewhere unexpected, these are the labels to match against.");

            text.AppendLine();
            text.AppendLine("What this means");
            text.AppendLine("---------------");
            text.AppendLine("A missing radar switch does not make radar-related features impossible:");
            text.AppendLine("the mod keeps per-unit state itself and uses whichever of the switches above");
            text.AppendLine("are present. What it cannot do is make the game's own detection maths read a");
            text.AppendLine("value it never stored - that needs a patch on the specific method, and the");
            text.AppendLine("method name is what this file is for.");

            string path = ModFolders.WriteText("capabilities.txt", text.ToString());
            ModLog.Info("Capability report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Could not write capabilities.txt", error);
        }
    }

    /// <summary>Short summary for the startup log.</summary>
    internal static string Summary() =>
        Capabilities.FoundCount + " of " + Capabilities.TotalCount + " game capabilities reachable";
}
