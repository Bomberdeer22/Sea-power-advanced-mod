using System;
using System.Diagnostics;
using BepInEx.Logging;

namespace SeaPowerAdvanced;

/// <summary>
/// Thin wrapper over a BepInEx log source. Never throws: a failing logger must not take the
/// game down, and every code path in this mod reports through here.
/// </summary>
internal static class ModLog
{
    private static ManualLogSource? source;

    internal static void Info(string message) => Write(LogLevel.Info, message);
    internal static void Warn(string message) => Write(LogLevel.Warning, message);

    internal static void Error(string message, Exception error) => Write(LogLevel.Error, message + ": " + error);

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
    }
}
