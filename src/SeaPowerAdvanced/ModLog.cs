using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using BepInEx.Logging;

namespace SeaPowerAdvanced;

/// <summary>
/// Two log channels on purpose.
///
/// 1. The BepInEx log (<c>BepInEx\LogOutput.log</c>), like every other mod.
/// 2. A plain file next to the mod DLL (<c>seapoweradvanced.log</c>).
///
/// The second one exists because "is this mod even running?" is the single most expensive question
/// to answer by guesswork: it worked here even when another mod's log lines were the only ones
/// visible, and it will survive a failing BepInEx log writer.
/// </summary>
internal static class ModLog
{
    private static ManualLogSource? source;
    private static readonly object Gate = new();
    private static string? filePath;
    private static bool fileUnavailable;

    internal static void Info(string message) => Write(LogLevel.Info, message);
    internal static void Warn(string message) => Write(LogLevel.Warning, message);
    internal static void Error(string message, Exception error) => Write(LogLevel.Error, message + ": " + error);

    /// <summary>Where the mod's own log file lives (null when it could not be written).</summary>
    internal static string? FilePath => filePath;

    private static void Write(LogLevel level, string message)
    {
        try
        {
            (source ??= Logger.CreateLogSource(ModInfo.Name)).Log(level, ModInfo.Name + ": " + message);
        }
        catch (Exception error)
        {
            try { Trace.TraceError(ModInfo.Name + ": " + message + "; logging failed: " + error); }
            catch (Exception) { }
        }

        WriteFile(level, message);
    }

    private static void WriteFile(LogLevel level, string message)
    {
        if (fileUnavailable) return;

        try
        {
            lock (Gate)
            {
                filePath ??= Path.Combine(ModFolders.Package, "seapoweradvanced.log");
                string tag = level switch
                {
                    LogLevel.Warning => "WARN ",
                    LogLevel.Error => "ERROR",
                    _ => "INFO "
                };
                File.AppendAllText(filePath,
                    DateTime.Now.ToString("HH:mm:ss") + " " + tag + " " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // A read-only game folder is normal; skip the file channel from now on.
            fileUnavailable = true;
        }
    }
}
