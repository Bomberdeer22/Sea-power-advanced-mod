using System;
using System.IO;

namespace SeaPowerAdvanced;

/// <summary>
/// Where the mod lives on disk. Whichever route loaded it - <c>BepInEx\plugins\SeaPowerAdvanced\</c>
/// or <c>Sea Power_Data\StreamingAssets\SeaPowerAdvanced\</c> - that folder is both the mod package
/// (for the mod-manager checkbox) and where diagnostic files are written.
/// </summary>
internal static class ModFolders
{
    internal static string Package
    {
        get
        {
            try
            {
                // Any type in this assembly would do; Boot is used because it is compiled
                // unconditionally, unlike Plugin (which only exists on the Anchor Chain route).
                string? folder = Path.GetDirectoryName(typeof(Boot).Assembly.Location);
                if (!string.IsNullOrEmpty(folder)) return folder!;
            }
            catch (Exception) { }
            return Path.GetTempPath();
        }
    }

    /// <summary>
    /// Appends to a text file in the mod folder, creating it if needed. Used for records that
    /// accumulate - the offensive ECM log grows one entry per lock rather than one per run.
    /// </summary>
    internal static string AppendText(string fileName, string contents)
    {
        foreach (string folder in new[] { Package, Path.GetTempPath() })
        {
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, fileName);
                File.AppendAllText(path, contents);
                return path;
            }
            catch (Exception) { }
        }
        return "<could not write " + fileName + ">";
    }

    /// <summary>Writes a text file into the mod folder (falls back to the temp folder).</summary>
    internal static string WriteText(string fileName, string contents)
    {
        foreach (string folder in new[] { Package, Path.GetTempPath() })
        {
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, fileName);
                File.WriteAllText(path, contents);
                return path;
            }
            catch (Exception) { }
        }
        return "<could not write " + fileName + ">";
    }
}
