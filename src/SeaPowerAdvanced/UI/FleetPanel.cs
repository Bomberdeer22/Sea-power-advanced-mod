using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.UI;

/// <summary>
/// The fleet sensor panel: one hotkey, the whole task force.
///
/// This is the first slice of the requested "hotkey panel" bundle, and it is built entirely on
/// levers the game already has - nothing here is invented:
///
/// <list type="bullet">
/// <item><b>EMCON profile</b> - radios silent but trading on radar (the game's <c>setEMCON</c> plus
/// its own radar switches), or a full "battle stations" profile with everything radiating.</item>
/// <item><b>Sprint-and-drift</b> - the game's <c>SprintAndDriftDoctrine</c> flag, switched on for
/// every ship at once. The game has the behaviour and a per-unit flag; what it does not have is a
/// way to set it fleet-wide.</item>
/// <item><b>Radar blink</b> - cycling the surface-search radar on a timer, which is what a real
/// ship does to make a passing anti-radiation missile work for its kill.</item>
/// </list>
///
/// Everything is opt-in, logged, and reversible. The panel never touches a unit it was not asked
/// to, and reports how many units responded rather than claiming success.
/// </summary>
public sealed class FleetPanel : MonoBehaviour
{
    private static readonly int WindowId = "SeaPowerAdvanced.FleetPanel".GetHashCode();

    private bool visible;
    private KeyCode toggleKey = KeyCode.F7;
    private Rect window = new(320f, 90f, 360f, 380f);
    private string status = "";

    /// <summary>Units whose radars the blink is cycling, with the state to put back afterwards.</summary>
    private readonly Dictionary<ObjectBase, bool> blinkUnits = new();

    private float nextBlinkAt;
    private bool blinkOnThisCycle;

    private void Awake()
    {
        try { toggleKey = ParseKey(ModSettings.FleetToggleKey, KeyCode.F7); }
        catch (Exception error) { ModLog.Error("Could not read the fleet panel key binding", error); }
    }

    private void Update()
    {
        try { PollHotkey(); }
        catch (Exception error) { ModLog.Error("Fleet panel hotkey failed", error); }

        try { TickBlink(); }
        catch (Exception error) { ModLog.Error("Radar blink failed", error); }
    }

    private void PollHotkey()
    {
        if (ModSettings.FleetShowPanel && Input.GetKeyDown(toggleKey))
        {
            visible = !visible;
            if (visible) status = "";
        }
    }

    private static KeyCode ParseKey(string name, KeyCode fallback)
    {
        try
        {
            return Enum.TryParse(name, true, out KeyCode parsed) ? parsed : fallback;
        }
        catch (Exception) { return fallback; }
    }

    private void OnGUI()
    {
        if (!visible) return;

        try
        {
            GUI.skin.window.fontSize = 12;
            window = GUILayout.Window(WindowId, window, DrawWindow, "Fleet sensors  (" + toggleKey + ")");
        }
        catch (Exception error)
        {
            ModLog.Error("Fleet panel drawing failed", error);
            visible = false;
        }
    }

    private void DrawWindow(int id)
    {
        List<ObjectBase> ships = GameWorld.PlayerShips();
        GUILayout.Label("Your ships: " + ships.Count, GUILayout.Width(320f));

        if (ships.Count == 0)
        {
            GUILayout.Label("No player ships were found. Start or load a mission.", GUILayout.Width(320f));
            GUILayout.TextArea(status, GUILayout.Width(340f));
            GUI.DragWindow();
            return;
        }

        GUILayout.Space(4f);
        GUILayout.Label("EMCON profile", GUILayout.Width(320f));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Silent")) Apply(ships, "silent", SilentProfile);
        if (GUILayout.Button("Passive")) Apply(ships, "passive", PassiveProfile);
        if (GUILayout.Button("Battle stations")) Apply(ships, "battle stations", BattleProfile);
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        GUILayout.Label("Sensors", GUILayout.Width(320f));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Radars on")) Apply(ships, "radars on", u => SensorControl.SetAllRadars(u, true));
        if (GUILayout.Button("Radars off")) Apply(ships, "radars off", u => SensorControl.SetAllRadars(u, false));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Active sonar on")) Apply(ships, "active sonar on", u => SensorControl.SetActiveSonar(u, true));
        if (GUILayout.Button("Active sonar off")) Apply(ships, "active sonar off", u => SensorControl.SetActiveSonar(u, false));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Deploy towed array")) Apply(ships, "towed array deployed", u => SensorControl.SetTowedSonar(u, true));
        if (GUILayout.Button("Retract array")) Apply(ships, "towed array retracted", u => SensorControl.SetTowedSonar(u, false));
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        GUILayout.Label("ASW doctrine", GUILayout.Width(320f));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Sprint and drift on"))
            Apply(ships, "sprint-and-drift on", u => SensorControl.SetSprintDoctrine(u, true));
        if (GUILayout.Button("Off"))
            Apply(ships, "sprint-and-drift off", u => SensorControl.SetSprintDoctrine(u, false));
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        GUILayout.Label("Radar blink", GUILayout.Width(320f));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Start"))
        {
            StartBlink(ships);
        }
        if (GUILayout.Button("Stop"))
        {
            StopBlink("blink stopped");
        }
        GUILayout.EndHorizontal();

        GUILayout.Label(blinkUnits.Count > 0
            ? "Blinking " + blinkUnits.Count + " ship(s). Radars restored when you stop."
            : "Blink off - radars are not being cycled.",
            GUILayout.Width(340f));

        GUILayout.Space(4f);
        GUILayout.TextArea(status, GUILayout.Width(340f));

        GUI.DragWindow();
    }

    // --- profiles ------------------------------------------------------------------------------

    /// <summary>Radios silent, no radar: the ship is a hole in the map. Speed is the only defence.</summary>
    private static bool SilentProfile(ObjectBase unit)
    {
        bool ok = SensorControl.SetAllRadars(unit, false);
        SensorControl.SetEmcon(unit, true);
        return ok;
    }

    /// <summary>Listening only: ESM and passive sonar, no emission at all.</summary>
    private static bool PassiveProfile(ObjectBase unit)
    {
        bool ok = SensorControl.SetAllRadars(unit, false);
        SensorControl.SetActiveSonar(unit, false);
        SensorControl.SetEmcon(unit, true);
        return ok;
    }

    /// <summary>Everything radiating, active sonar on, towed array out: the ship expects a fight.</summary>
    private static bool BattleProfile(ObjectBase unit)
    {
        bool ok = SensorControl.SetAllRadars(unit, true);
        SensorControl.SetActiveSonar(unit, true);
        SensorControl.SetEmcon(unit, false);
        if (SensorControl.HasTowedArray(unit)) SensorControl.SetTowedSonar(unit, true);
        return ok;
    }

    // --- applying ------------------------------------------------------------------------------

    private void Apply(List<ObjectBase> ships, string what, Func<ObjectBase, bool> action)
    {
        int changed = 0;
        int failed = 0;

        foreach (ObjectBase unit in ships)
        {
            bool ok;
            try { ok = action(unit); }
            catch (Exception) { ok = false; }

            if (ok) changed++;
            else failed++;

            ModLog.Info("Fleet panel: " + what + " -> " + Describe(unit) + ": " + (ok ? "applied" : "no effect"));
        }

        status = what + ": " + changed + " ship(s) responded"
                 + (failed > 0 ? ", " + failed + " did not" : "")
                 + ".";
        ModLog.Info("Fleet panel: " + status);
    }

    private static string Describe(ObjectBase unit)
    {
        try { return unit.DisplayNameShort + " (" + unit.Name + ")"; }
        catch (Exception) { return "<unit>"; }
    }

    // --- radar blink ---------------------------------------------------------------------------

    /// <summary>
    /// Cycles the surface-search radar for the given ships. Real "blinking" is a ship switching its
    /// search radar on for a sweep and off again so a passive anti-radiation seeker has less to
    /// home on; the interval here is deliberately visible rather than realistic, so the effect can
    /// be seen in a test mission.
    /// </summary>
    private void StartBlink(List<ObjectBase> ships)
    {
        blinkUnits.Clear();

        foreach (ObjectBase unit in ships)
        {
            // Remember what each ship's surface-search radar was doing, so Stop puts it back as it
            // was rather than switching everything on and calling that a restore.
            bool? wasOn = null;
            try { wasOn = SensorControl.GetSurfaceSearchOn(unit); }
            catch (Exception) { }
            blinkUnits[unit] = wasOn ?? true;
        }

        blinkOnThisCycle = false;
        nextBlinkAt = 0f;
        status = "Blinking " + blinkUnits.Count + " ship(s). Press Stop to restore their radars.";
        ModLog.Info("Fleet panel: radar blink started on " + blinkUnits.Count + " ship(s).");
    }

    private void StopBlink(string why)
    {
        int restored = 0;
        foreach (KeyValuePair<ObjectBase, bool> entry in blinkUnits)
        {
            try
            {
                if (SensorControl.SetSurfaceSearch(entry.Key, entry.Value)) restored++;
            }
            catch (Exception) { }
        }

        blinkUnits.Clear();
        status = why + ": " + restored + " ship(s) had their surface-search radar put back.";
        ModLog.Info("Fleet panel: " + status);
    }

    private void TickBlink()
    {
        if (blinkUnits.Count == 0) return;

        float interval = Mathf.Clamp(ModSettings.FleetBlinkSeconds, 2f, 120f);
        if (Time.unscaledTime < nextBlinkAt) return;

        nextBlinkAt = Time.unscaledTime + interval;
        blinkOnThisCycle = !blinkOnThisCycle;

        int applied = 0;
        foreach (ObjectBase unit in new List<ObjectBase>(blinkUnits.Keys))
        {
            try
            {
                if (unit == null || unit.IsDestroyed)
                {
                    blinkUnits.Remove(unit);
                    continue;
                }
                if (SensorControl.SetSurfaceSearch(unit, blinkOnThisCycle)) applied++;
            }
            catch (Exception) { }
        }

        if (ModSettings.VerboseLog)
            ModLog.Info("Radar blink: surface search " + (blinkOnThisCycle ? "on" : "off")
                        + " for " + applied + " ship(s).");
    }

    /// <summary>Creates the panel once, owned by a scene object that survives scene loads.</summary>
    internal static void Install()
    {
        try
        {
#pragma warning disable CS0618
            if (FindObjectOfType<FleetPanel>() != null) return;
#pragma warning restore CS0618

            GameObject host = new("SeaPowerAdvanced.FleetPanel");
            DontDestroyOnLoad(host);
            host.AddComponent<FleetPanel>();
        }
        catch (Exception error)
        {
            ModLog.Error("Could not create the fleet panel", error);
        }
    }
}
