using System;
using System.IO;

namespace SeaPowerAdvanced;

/// <summary>
/// Where the mod lives on disk. The DLL sits in
/// <c>Sea Power_Data/StreamingAssets/SeaPowerAdvanced/</c>, so its own folder is both the
/// mod package (for the mod-manager checkbox) and the place diagnostic files are written.
/// </summary>
internal static class ModFolders
{
    internal static string Package
    {
        get
        {
            try
            {
                string? folder = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                if (!string.IsNullOrEmpty(folder)) return folder!;
            }
            catch (Exception) { }
            return Path.GetTempPath();
        }
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
