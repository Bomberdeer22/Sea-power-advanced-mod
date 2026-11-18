using System;
using System.IO;
using SeaPower;

namespace SeaPowerAdvanced;

/// <summary>
/// Anchor Chain loads every DLL in every mod folder, ticked or not, so a mod has to switch itself
/// off when the mod-manager checkbox is clear. The answer lives in the game's own file manager.
/// </summary>
internal static class ModActivation
{
    internal static bool IsEnabled(string packagePath)
    {
        try
        {
            TrulyObservableCollection<SearchDirectory>? directories = Singleton<FileManager>.Instance?.Directories;
            if (directories == null) return true;

            string wanted = Normalize(packagePath);
            foreach (SearchDirectory entry in directories)
            {
                if (entry?.DirectoryInfo == null) continue;
                if (!string.Equals(Normalize(entry.DirectoryInfo.FullName), wanted, StringComparison.OrdinalIgnoreCase))
                    continue;
                return entry.IsChecked;
            }

            // Not registered with the mod manager (a loose dev build): let it run.
            ModLog.Warn("Mod folder is not registered with the mod manager: " + packagePath);
            return true;
        }
        catch (Exception error)
        {
            ModLog.Error("Could not read the mod-manager state; assuming enabled", error);
            return true;
        }
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
