using System;
using System.Collections;
using System.IO;
using System.Reflection;

namespace SeaPowerAdvanced;

/// <summary>
/// Anchor Chain loads every DLL in every mod folder, ticked or not, so each mod has to switch
/// itself off when the mod-manager checkbox is clear. This reads the game's own file manager for
/// that answer (the same approach used by other Anchor Chain mods).
/// </summary>
internal static class ModActivation
{
    internal static bool IsEnabled(Type fileManagerType, string packagePath)
    {
        object? manager = fileManagerType
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            ?.GetValue(null);
        if (manager == null) throw new InvalidOperationException("Sea Power's file manager is not ready.");

        var entries = fileManagerType.GetProperty("Directories", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(manager) as IEnumerable
            ?? throw new InvalidOperationException("Sea Power's mod directories are unavailable.");

        foreach (object? entry in entries)
        {
            if (entry == null) continue;
            Type type = entry.GetType();
            if (type.GetProperty("DirectoryInfo", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entry) is not DirectoryInfo directory)
                continue;
            if (!string.Equals(Normalize(directory.FullName), Normalize(packagePath), StringComparison.OrdinalIgnoreCase))
                continue;

            var checkedProperty = type.GetProperty("IsChecked", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("The mod entry has no IsChecked property.");
            return (bool)(checkedProperty.GetValue(entry) ?? false);
        }

        // Not registered: treat as enabled so a dev build in a loose folder still runs.
        ModLog.Warn("Mod folder is not registered with the mod manager: " + packagePath);
        return true;
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
