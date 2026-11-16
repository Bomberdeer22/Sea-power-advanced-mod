using System;
using System.Text;

namespace SeaPowerAdvanced.Menu;

/// <summary>
/// Writes <c>menu-report.txt</c> the first time the mod adds its entries to a real menu.
///
/// It exists because "the features are not in the menu" is the hardest thing to diagnose remotely:
/// the mod knows whether it ran, where each entry went, and what sections the game actually has, but
/// none of that is visible from the game. One file answers all of it.
///
/// Written from the game's own thread while the menu is being built, so it reflects the menu the
/// player is looking at, not a reconstruction.
/// </summary>
internal static class MenuReport
{
    internal static void Write()
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine("Sea Power Advanced - context menu report");
            text.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            + "   mod " + ModInfo.Version);
            text.AppendLine();
            text.AppendLine("Sections found in this build of the game");
            text.AppendLine("----------------------------------------");
            text.AppendLine("  " + MenuRouting.Describe());
            text.AppendLine();
            text.AppendLine("Where each feature was put");
            text.AppendLine("--------------------------");

            if (MenuRouting.Placements.Count == 0)
            {
                text.AppendLine("  (nothing was placed - the menu was empty when the mod ran)");
            }
            else
            {
                foreach (string placement in MenuRouting.Placements) text.AppendLine("  " + placement);
            }

            text.AppendLine();
            text.AppendLine("If an entry landed somewhere unexpected, the fix is the keyword list in");
            text.AppendLine("MenuRouting.cs - the section labels above are the exact strings to match.");
            text.AppendLine("If every entry says 'top level', the game's sections are named differently");
            text.AppendLine("from anything the mod recognises, and those labels are the missing piece.");

            string path = ModFolders.WriteText("menu-report.txt", text.ToString());
            ModLog.Info("Menu report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Could not write menu-report.txt", error);
        }
    }
}
