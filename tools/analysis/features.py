"""
The feature readiness probe: which of the planned features this build can actually support.

Each planned feature is broken into the capabilities it needs, and each capability is one question
that either has an answer in the assembly or does not. That turns "can we build this?" from an
opinion into a checklist:

    READY    every required capability was found
    PARTIAL  some were; the feature is possible but needs a workaround
    BLOCKED  at least one required capability is missing — do not start it yet

`expectation` records the prior before the probe runs, so the result can contradict it honestly
rather than being read as confirmation.

Nothing here decides *how* to build a feature. It answers whether the raw material exists: a
member to switch a radar's emission, a member to read a missile's bearing, and so on.
"""

from __future__ import annotations

import re

from gameapi import Assembly

MAX_EXAMPLES = 4


def need(id, description, *, kind="member", pattern="", scope=None, required=True):
    """One capability. `kind` is member, type or callsite."""
    return {
        "id": id,
        "description": description,
        "kind": kind,
        "pattern": pattern,
        "scope": scope,
        "required": required,
    }


FEATURES = [
    # ---------------------------------------------------------------- sensor & EMCON
    {
        "id": "radar_blink",
        "name": "Radar blink cycling",
        "group": "Sensor & EMCON",
        "expectation": "likely",
        "needs": [
            need("emitters", "ships own sensor/radar objects", kind="type", pattern=r"\bRadar\b|Sensor"),
            need("emitter_list", "a unit exposes the list of its own emitters", pattern=r"^_?(Sensors|Radars|Emitters)$|Sensors\b"),
            need("emission_switch", "an emitter can be switched on and off", pattern=r"set_?(Emission|RadarActive|Active|Enabled|On)$|^Enable$|^Disable$|^Toggle"),
            need("detection_pass", "detection is driven from a tick that can be hooked", kind="callsite",
                 pattern="UpdateDetection", required=False),
        ],
    },
    {
        "id": "auto_arm_shutoff",
        "name": "Auto-ARM shutoff (anti-radiation defence)",
        "group": "Sensor & EMCON",
        "expectation": "maybe",
        "needs": [
            need("arm_ammo", "anti-radiation missiles exist as a recognisable kind",
                 pattern=r"AntiRadiation|Anti_Radiation|ARM[A-Z]|PassiveRadar|HomingAntiRadiation"),
            need("incoming_tracks", "a unit can see weapons inbound to it", pattern=r"^_?Incoming|^_?Threats?$|IncomingWeapons|IncomingMissiles"),
            need("emission_switch", "emitters can be cut and restored", pattern=r"set_?(Emission|RadarActive|Active|Enabled)$"),
            need("missile_bearing", "a weapon's bearing/target is readable", pattern=r"Bearing|Heading|Course|Target$"),
        ],
    },
    {
        "id": "signature_spoofing",
        "name": "Commercial signature spoofing",
        "group": "Sensor & EMCON",
        "expectation": "unlikely",
        "needs": [
            need("rcs", "a unit's radar cross-section is a readable field",
                 pattern=r"\bRCS\b|RadarCrossSection|_?Signature"),
            need("rcs_write", "the cross-section can be changed at runtime",
                 pattern=r"set_?(RCS|Signature|RadarCrossSection)"),
            need("emit_profile", "the emission profile can be swapped", pattern=r"EmissionProfile|_?EmitterType|RadarType"),
            need("speed_profile", "speed can be driven to mimic a merchant", pattern=r"set_?Speed|^Throttle|SetThrottle"),
        ],
    },

    # ---------------------------------------------------------------- tactics
    {
        "id": "salvo_deconfliction",
        "name": "Task-force salvo deconfliction",
        "group": "Tactical",
        "expectation": "maybe",
        "needs": [
            need("fire_from_code", "a mount can fire without the UI's fire command", kind="callsite",
                 pattern="::fire"),
            need("engagement_state", "a mount reports ready/ammo so a shot can be assigned",
                 pattern=r"^_?(Ammo|Remaining|Ready|Loaded)$|IsReady"),
            need("datalink", "ships share a picture through a datalink", pattern=r"Datalink|Link1[16]|CEC|SharedTrack"),
            need("intercept_solution", "time-to-intercept can be computed", pattern=r"Intercept|TimeToImpact|Solution"),
        ],
    },
    {
        "id": "chaff_maneuver",
        "name": "Seduction chaff and hard-rudder manoeuvres",
        "group": "Tactical",
        "expectation": "maybe",
        "needs": [
            need("chaff", "chaff/flares can be released from code", pattern=r"Chaff|Flare|Countermeasure|Decoy"),
            need("terminal_phase", "an incoming missile's phase is visible", pattern=r"Terminal|Seeker|Homing"),
            need("maneuver", "a hard turn can be ordered", pattern=r"set_?Order|SetCourse|set_?Heading"),
        ],
    },
    {
        "id": "degraded_control",
        "name": "Degraded local control",
        "group": "Tactical",
        "expectation": "maybe",
        "needs": [
            need("director_damage", "fire-control directors are damageable subsystems",
                 pattern=r"Director|FireControl|\bCIC\b|CombatInformation"),
            need("mount_health", "a mount can be disabled independently", pattern=r"^_?IsDestroyed$|Disabled|Operable|Damaged"),
            need("gun_control", "guns/CIWS can be told to engage locally", kind="type", pattern=r"\bGun\b|CIWS"),
        ],
    },
    {
        "id": "readiness_states",
        "name": "Cold cruise vs battle stations readiness",
        "group": "Tactical",
        "expectation": "maybe",
        "needs": [
            need("reload_timer", "reload/readiness is a per-mount timer",
                 pattern=r"Reload|Cooldown|ReadyTime|_ready|SpinUp|WarmUp"),
            need("weapon_status", "a unit's weapon status is set by the game", pattern=r"_?weaponStatus|WeaponStatus"),
            need("gyro_seeker", "seekers/gyros have an explicit ready state", pattern=r"Seeker|Gyro|Cool"),
        ],
    },

    # ---------------------------------------------------------------- formation & ASW
    {
        "id": "threat_axis_screen",
        "name": "Threat-axis screen reorientation",
        "group": "Formation & ASW",
        "expectation": "maybe",
        "needs": [
            need("formation", "formations expose their stations", kind="type", pattern=r"\bFormation\b|Station"),
            need("stations", "formation slots are addressable", pattern=r"^_?Stations$|^_?Station$"),
            need("bearing_to", "bearing to a contact can be read", pattern=r"BearingTo|Bearing|CourseTo"),
            need("course_order", "a course can be ordered", pattern=r"set_?Order|SetCourse|set_?Heading"),
        ],
    },
    {
        "id": "sprint_drift",
        "name": "Automated sprint-and-drift ASW",
        "group": "Formation & ASW",
        "expectation": "likely",
        "needs": [
            need("speed_control", "speed can be set from code", pattern=r"set_?Speed|^Throttle|SetThrottle|_speed"),
            need("towed_array", "towed arrays/sonars exist per ship", pattern=r"\bTowed|\bSonar"),
            need("silent_speed", "a quiet/cavitation speed exists", pattern=r"Cavitat|Silent|Quiet|_maxSilent"),
        ],
    },
    {
        "id": "helo_patterns",
        "name": "One-click helicopter search patterns",
        "group": "Formation & ASW",
        "expectation": "likely",
        "needs": [
            need("flight_deck", "a flight deck can launch aircraft from code", kind="type", pattern=r"FlightDeck"),
            need("launch", "launch is callable", pattern=r"^Launch|LaunchUnit"),
            need("waypoints", "waypoint tasks can be pushed onto a unit", pattern=r"setWaypointTask|AddWaypoints|ApplyRouteWaypoints"),
            need("sonobuoy", "sonobuoys can be deployed", pattern=r"Sonobuoy|_?Bouy|Bouy"),
        ],
    },

    # ---------------------------------------------------------------- logistics
    {
        "id": "unrep",
        "name": "Functional underway replenishment",
        "group": "Logistics",
        "expectation": "unlikely",
        "needs": [
            need("fuel", "ships have a fuel quantity", pattern=r"^_?Fuel|Bunker|Endurance"),
            need("fuel_transfer", "fuel can be transferred between ships", pattern=r"Transfer|Replenish|Refuel|UNREP"),
            need("ammo_count", "magazines have a countable inventory", pattern=r"^_?Ammo(Count|State)?$|Magazine"),
        ],
    },
    {
        "id": "fatigue",
        "name": "Battle stations fatigue",
        "group": "Logistics",
        "expectation": "unlikely",
        "needs": [
            need("crew", "crew state exists as a modelled thing", pattern=r"^_?Crew|Fatigue|Stamina|Morale"),
            need("alert_state", "general quarters is a settable state", pattern=r"GeneralQuarters|BattleStations|AlertState|_?Readiness"),
            need("reload_modifier", "reload time can be modified", pattern=r"Reload|_?reloadTime|Cooldown"),
        ],
    },
]


def _compile(pattern: str):
    return re.compile(pattern, re.IGNORECASE) if pattern else None


class Probe:
    def __init__(self, assembly: Assembly, callsites: bool = True, log=print):
        self.assembly = assembly
        self.callsites = callsites
        self.log = log
        self._names = assembly.member_index()
        self._callsite_cache: dict[str, list] = {}

    def run(self) -> list[dict]:
        results = []
        for feature in FEATURES:
            checks = []
            for item in feature["needs"]:
                checks.append(self._check(item))
            results.append({**feature, "checks": checks, "verdict": _verdict(checks)})
        return results

    def _check(self, item: dict) -> dict:
        found: list[str] = []

        if item["kind"] == "type":
            pattern = _compile(item["pattern"])
            for name in self.assembly.types:
                if pattern.search(name):
                    found.append(name)
                    if len(found) >= MAX_EXAMPLES:
                        break

        elif item["kind"] == "callsite":
            if not self.callsites:
                return {**item, "found": None, "examples": [], "note": "not checked (--fast)"}
            if item["pattern"] not in self._callsite_cache:
                try:
                    self._callsite_cache[item["pattern"]] = self.assembly.find_calls(item["pattern"])
                except Exception as error:
                    self._callsite_cache[item["pattern"]] = []
                    self.log(f"    call-site scan failed for {item['pattern']}: {error}")
            hits = self._callsite_cache[item["pattern"]]
            found = [f"{caller}.{method} -> {target}" for caller, method, target in hits[:MAX_EXAMPLES]]

        else:
            pattern = _compile(item["pattern"])
            scope = _compile(item["scope"]) if item["scope"] else None
            for declaring, kind, name in self._names:
                # Compiler-generated members carry the names of the code around them and produce
                # nothing but noise here.
                if name.startswith("<") or "b__" in name:
                    continue
                if not pattern.search(name):
                    continue
                if scope and not scope.search(declaring):
                    continue
                found.append(f"{declaring}::{name}")
                if len(found) >= MAX_EXAMPLES:
                    break

        return {**item, "found": bool(found), "examples": found, "note": ""}


def _verdict(checks: list[dict]) -> str:
    required = [c for c in checks if c["required"] and c["found"] is not None]
    if not required:
        return "UNKNOWN"
    missing = [c for c in required if not c["found"]]
    if not missing:
        return "READY"
    if len(missing) == len(required):
        return "BLOCKED"
    return "PARTIAL"


def format_report(assembly: Assembly, results: list[dict]) -> str:
    info = assembly.summary()
    lines = []
    add = lines.append

    add("=" * 100)
    add("FEATURE READINESS")
    add("=" * 100)
    add(f"assembly   {info['assembly']}  {info['version']}")
    add(f"file       {info['path']}")
    add(f"types      {info['types']}   methods {info['methods']}")
    add("")
    add("READY    every required capability found")
    add("PARTIAL  some found: possible with a workaround")
    add("BLOCKED  a required capability is missing")
    add("")
    add("'expectation' is my guess before the check, so a surprise is visible as a surprise.")
    add("")

    counts = {"READY": 0, "PARTIAL": 0, "BLOCKED": 0, "UNKNOWN": 0}
    for result in results:
        counts[result["verdict"]] = counts.get(result["verdict"], 0) + 1

    add(f"summary    READY {counts['READY']}   PARTIAL {counts['PARTIAL']}   "
        f"BLOCKED {counts['BLOCKED']}")
    add("")

    for group in dict.fromkeys(feature["group"] for feature in results):
        add("")
        add(f"## {group}")
        for result in (feature for feature in results if feature["group"] == group):
            add("")
            add(f"  [{result['verdict']:<7}] {result['name']}")
            add(f"            prior guess: {result['expectation']}")
            for check in result["checks"]:
                if check["found"] is None:
                    mark = "  --  "
                elif check["found"]:
                    mark = "  ok  "
                else:
                    mark = " MISS " if check["required"] else " opt  "
                add(f"            {mark} {check['description']}")
                for example in check["examples"]:
                    add(f"                   {example}")

    add("")
    add("What next: send this file back. A PARTIAL verdict lists exactly which capability was missing,")
    add("which is usually the question that decides the implementation.")
    return "\n".join(lines)
