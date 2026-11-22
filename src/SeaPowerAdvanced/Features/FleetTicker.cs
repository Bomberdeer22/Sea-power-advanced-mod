using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// Drives everything that has to happen without the player clicking: radar blink cycles, automatic
/// anti-radiation shutoff, automatic defensive jamming, release of a deconflicted salvo, and the
/// readiness/fatigue clocks.
///
/// This is a hidden object with no interface at all. The features themselves live in the game's own
/// right-click menus, in its own style; this only does the timing behind them.
/// </summary>
public sealed class FleetTicker : MonoBehaviour
{
    private static FleetTicker? instance;

    private float nextReadinessTick;

    /// <summary>The multiplayer role last written down, so multiplayer.txt is refreshed when a
    /// session starts, ends, or the player joins as host instead of client.</summary>
    private static string lastMultiplayerRole = "";

    internal static void Install()
    {
        if (instance != null) return;

        try
        {
            GameObject host = new("SeaPowerAdvanced.FleetTicker");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<FleetTicker>();
        }
        catch (Exception error)
        {
            ModLog.Error("Could not start the timer that drives timed features", error);
        }
    }

    private void Update()
    {
        try { Blink.Tick(); }
        catch (Exception error) { ModLog.Error("Radar blink failed", error); }

        try { AutoArm.Tick(); }
        catch (Exception error) { ModLog.Error("Auto-ARM shutoff failed", error); }

        try { DefensiveEcm.Tick(); }
        catch (Exception error) { ModLog.Error("Defensive ECM failed", error); }

        try { OffensiveEcm.Tick(); }
        catch (Exception error) { ModLog.Error("Offensive ECM failed", error); }

        try { Datalink.Tick(); }
        catch (Exception error) { ModLog.Error("Force datalink failed", error); }

        try { Lidar.Tick(); }
        catch (Exception error) { ModLog.Error("LIDAR failed", error); }

        try { SalvoPlan.Tick(); }
        catch (Exception error) { ModLog.Error("Salvo release failed", error); }

        try { HeloPatterns.Tick(); }
        catch (Exception error) { ModLog.Error("Helo pattern follow-up failed", error); }

        if (Time.unscaledTime < nextReadinessTick) return;
        nextReadinessTick = Time.unscaledTime + 2f;

        try { Readiness.Tick(); }
        catch (Exception error) { ModLog.Error("Readiness update failed", error); }

        // A session can come up or go away at any point, and every rule in Multiplayer depends on it.
        if (Multiplayer.Describe() == lastMultiplayerRole) return;

        lastMultiplayerRole = Multiplayer.Describe();
        ModLog.Info("Multiplayer: " + lastMultiplayerRole);
        Multiplayer.WriteReport();
    }

    /// <summary>Every unit the mod is currently acting on, for the timers above.</summary>
    internal static List<ObjectBase> Tracked()
    {
        try { return GameWorld.PlayerShips(); }
        catch (Exception) { return new List<ObjectBase>(); }
    }
}
