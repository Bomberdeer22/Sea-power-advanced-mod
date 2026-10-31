using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SeaPower;
using UnityEngine;

namespace SeaPowerAdvanced.Hatches;

/// <summary>
/// Opens and closes individual silo hatches, torpedo tube shutters and launcher covers without
/// firing anything.
///
/// The game never exposed this, so there is no single documented member to call. Instead we try a
/// chain of strategies, most specific first, and stop at the first one that works:
///
/// <list type="number">
/// <item><b>Container member</b> (per cell): an Open/Close style method or an IsOpen style
/// bool on the game's own <c>WeaponContainer</c>.</item>
/// <item><b>Animator</b> (per cell): a Unity Animator on the cell's model whose parameters look
/// like a hatch; the parameter that matches is set to the requested state.</item>
/// <item><b>Code animation</b> (per cell): an entry in <c>_obp._codeAnimations</c> whose name
/// matches the mount and looks like a hatch, driven through setOpenState/setClosedState.</item>
/// <item><b>Mount member</b> (whole system): the same member search and animator search on the
/// weapon system and its visual weapon part, for launchers that open one cover over all cells.</item>
/// </list>
///
/// Whichever step succeeds is logged, so the diagnostic dump and the log together show exactly
/// which member the game uses per ship class.
/// </summary>
internal static class HatchControl
{
    private static readonly Regex HatchTokens =
        new("hatch|cover|lid|silo|door|canopy|vls|tube|cell", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] OpenCellMembers =
    {
        "Open", "OpenHatch", "OpenCover", "OpenLid", "OpenDoor", "OpenSilo", "OpenContainer", "OpenCell",
        "SetOpen", "SetOpened", "SetHatchOpen", "RaiseHatch"
    };

    private static readonly string[] CloseCellMembers =
    {
        "Close", "CloseHatch", "CloseCover", "CloseLid", "CloseDoor", "CloseSilo", "CloseContainer", "CloseCell",
        "SetClosed", "SetHatchClosed", "LowerHatch"
    };

    private static readonly string[] OpenSystemMembers =
    {
        "OpenSystem", "OpenCover", "OpenHatch", "Open", "Deploy", "Raise", "SetOpened", "TryOpenSystem"
    };

    private static readonly string[] CloseSystemMembers =
    {
        "CloseSystem", "CloseCover", "CloseHatch", "Close", "Stow", "Lower", "SetClosed", "TryCloseSystem"
    };

    private static readonly string[] StateMembers =
    {
        "IsOpen", "_isOpen", "_open", "Open", "_opened", "IsOpened", "_hatchOpen", "_coverOpen", "_siloOpen",
        "_systemOpen"
    };

    private static Type? animatorType;
    private static bool animatorTypeResolved;

    /// <summary>Opens or closes one cell, or every cell when <paramref name="cellIndex"/> is -1.</summary>
    internal static bool Apply(ObjectBase unit, HatchGroup group, int cellIndex, bool open, out string report)
    {
        var notes = new List<string>();
        bool handled = false;
        object? blueprint = R.Get(unit, "_obp");

        if (group.HasCells)
        {
            foreach (int index in CellIndexes(group, cellIndex))
            {
                handled |= ApplyCell(group.Cells[index], blueprint, group, index, open, notes);
            }
        }

        // Launchers that open one cover over all cells have nothing to offer per cell.
        if (!handled || !group.HasCells)
        {
            handled |= ApplySystem(group.System, blueprint, group, open, notes);
        }

        report = notes.Count == 0 ? "no hatch handle found" : string.Join("; ", notes);
        return handled;
    }

    internal static void ApplyAll(ObjectBase unit, IEnumerable<HatchGroup> groups, bool open)
    {
        foreach (HatchGroup group in groups)
        {
            Apply(unit, group, -1, open, out string report);
            for (int index = 0; index < group.Cells.Count; index++)
            {
                HatchState.Set(HatchState.CellKey(unit, group, index), open);
            }
            if (ModSettings.VerboseLog) ModLog.Info($"{(open ? "Open" : "Close")} {group.Name}: {report}");
        }
    }

    internal static void ApplyCell(ObjectBase unit, HatchGroup group, int cellIndex, bool open)
    {
        bool handled = Apply(unit, group, cellIndex, open, out string report);
        HatchState.Set(HatchState.CellKey(unit, group, cellIndex), open);
        string label = HatchBay.CellLabel(group, cellIndex);
        string message = $"{(open ? "Open" : "Close")} {group.Name} / {label}: {report}";
        if (!handled)
        {
            ModLog.Warn(message + " (no hatch control found; run the hatch dump for this ship)");
        }
        else if (ModSettings.VerboseLog)
        {
            ModLog.Info(message);
        }
    }

    // ------------------------------------------------------------------ cells

    private static bool ApplyCell(object cell, object? blueprint, HatchGroup group, int index, bool open,
        List<string> notes)
    {
        if (TryMember(cell, open ? OpenCellMembers : CloseCellMembers, open, out string how))
        {
            notes.Add($"cell {index + 1} member {how}");
            return true;
        }

        if (TryAnimator(cell, open, out how))
        {
            notes.Add($"cell {index + 1} {how}");
            return true;
        }

        if (TryCodeAnimation(blueprint, group, index, open, out how))
        {
            notes.Add($"cell {index + 1} {how}");
            return true;
        }

        return false;
    }

    // ---------------------------------------------------------------- systems

    private static bool ApplySystem(object system, object? blueprint, HatchGroup group, bool open, List<string> notes)
    {
        if (TryMember(system, open ? OpenSystemMembers : CloseSystemMembers, open, out string how))
        {
            notes.Add("mount member " + how);
            return true;
        }

        object? mount = R.Get(system, "_vwp");

        if (mount != null && TryMember(mount, open ? OpenSystemMembers : CloseSystemMembers, open, out how))
        {
            notes.Add("mount part member " + how);
            return true;
        }

        if (TryAnimator(system, open, out how) || (mount != null && TryAnimator(mount, open, out how)))
        {
            notes.Add("mount " + how);
            return true;
        }

        if (TryCodeAnimation(blueprint, group, -1, open, out how))
        {
            notes.Add("mount " + how);
            return true;
        }

        notes.Add("mount has no hatch handle");
        return false;
    }

    /// <summary>Looks for a method or a bool member with a plausible name on the target object.</summary>
    private static bool TryMember(object? target, string[] names, bool open, out string how)
    {
        how = string.Empty;
        if (target == null) return false;

        foreach (string name in names)
        {
            if (R.TryCall(target, name))
            {
                how = name + "()";
                return true;
            }

            if (R.TryCall(target, name, open))
            {
                how = name + "(bool)";
                return true;
            }
        }

        foreach (string name in StateMembers)
        {
            if (R.TrySet(target, name, open))
            {
                how = name + " (state member)";
                return true;
            }
        }

        return false;
    }

    // --------------------------------------------------------------- animators

    /// <summary>
    /// Finds Unity Animators on the object and on whatever model it points at. The game's
    /// containers are usually components, so the transform hierarchy is where the hatch animation
    /// lives.
    /// </summary>
    internal static List<object> FindAnimators(object host)
    {
        var found = new List<object>();
        Type? type = AnimatorType();
        if (type == null) return found;

        var seen = new HashSet<int>();
        foreach (GameObject model in CandidateGameObjects(host))
        {
            try
            {
                foreach (Component component in model.GetComponentsInChildren(type, true))
                {
                    if (component != null && seen.Add(component.GetInstanceID())) found.Add(component);
                }
            }
            catch (Exception) { }
        }
        return found;
    }

    private static bool TryAnimator(object? host, bool open, out string how)
    {
        how = string.Empty;
        if (host == null) return false;

        foreach (object animator in FindAnimators(host))
        {
            bool matched = false;
            List<KeyValuePair<string, string>> parameters = AnimatorParameters(animator);

            foreach (KeyValuePair<string, string> parameter in parameters)
            {
                bool isBool = parameter.Value.IndexOf("Bool", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isTrigger = parameter.Value.IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) >= 0;
                if ((!isBool && !isTrigger) || !HatchTokens.IsMatch(parameter.Key)) continue;

                if (isBool)
                {
                    matched |= R.TryCall(animator, "SetBool", parameter.Key, open);
                }
                else if (open)
                {
                    matched |= R.TryCall(animator, "SetTrigger", parameter.Key);
                }
                else
                {
                    matched |= R.TryCall(animator, "ResetTrigger", parameter.Key);
                }
            }

            if (matched)
            {
                how = "animator " + Name(animator) + " (named parameters)";
                return true;
            }

            if (parameters.Count == 0)
            {
                // The animator does not expose its parameter list; try the conventional names.
                foreach (string name in ModSettings.AnimatorBoolNames)
                {
                    if (!R.TryCall(animator, "SetBool", name, open)) continue;
                    how = "animator " + Name(animator) + " (convention " + name + ", unverified)";
                    return true;
                }
            }
        }

        return false;
    }

    private static List<KeyValuePair<string, string>> AnimatorParameters(object animator)
    {
        var parameters = new List<KeyValuePair<string, string>>();
        object? raw = R.Get(animator, "parameters");
        foreach (object parameter in R.Items(raw))
        {
            if (R.Get(parameter, "name") is not string name) continue;
            parameters.Add(new KeyValuePair<string, string>(name, R.Get(parameter, "type")?.ToString() ?? string.Empty));
        }
        return parameters;
    }

    /// <summary>Parameter names of every animator under the host, for the diagnostic dump.</summary>
    internal static string DescribeAnimators(object host)
    {
        var lines = new List<string>();
        foreach (object animator in FindAnimators(host))
        {
            var names = AnimatorParameters(animator)
                .Select(p => p.Key + ":" + p.Value)
                .ToList();
            lines.Add(Name(animator) + " [" + (names.Count == 0 ? "no parameter list" : string.Join(", ", names)) + "]");
        }
        return lines.Count == 0 ? "none" : string.Join(" | ", lines);
    }

    private static IEnumerable<GameObject> CandidateGameObjects(object host)
    {
        var seen = new HashSet<int>();
        object? mount = R.Get(host, "_vwp");

        foreach (object? candidate in new object?[]
                 {
                     host,
                     R.Get(host, "gameObject"),
                     R.Get(host, "_gameObject"),
                     R.Get(host, "_object"),
                     R.Get(host, "_model"),
                     R.Get(host, "_subModel"),
                     R.Get(host, "transform"),
                     R.Get(host, "_transform"),
                     R.Get(mount, "gameObject"),
                     R.Get(mount, "_gameObject"),
                     R.Get(mount, "transform"),
                 })
        {
            GameObject? model = candidate switch
            {
                GameObject direct => direct,
                Component component => component.gameObject,
                _ => null
            };
            if (model != null && seen.Add(model.GetInstanceID())) yield return model;
        }
    }

    private static Type? AnimatorType()
    {
        if (animatorTypeResolved) return animatorType;
        animatorTypeResolved = true;

        // Resolved by name so the mod does not need a reference to UnityEngine.AnimationModule.
        foreach (string assemblyName in new[] { "UnityEngine.AnimationModule", "UnityEngine.CoreModule" })
        {
            animatorType = Type.GetType("UnityEngine.Animator, " + assemblyName, false);
            if (animatorType != null) return animatorType;
        }

        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            animatorType = assembly.GetType("UnityEngine.Animator", false);
            if (animatorType != null) return animatorType;
        }

        return null;
    }

    // --------------------------------------------------------- code animations

    /// <summary>
    /// The blueprint's code animations include things like gear, masts and covers. We only touch
    /// entries that look like a hatch and belong to this mount; an ambiguous match is skipped on
    /// purpose rather than opening a radar mast.
    /// </summary>
    private static bool TryCodeAnimation(object? blueprint, HatchGroup group, int cellIndex, bool open, out string how)
    {
        how = string.Empty;
        if (blueprint == null) return false;

        List<KeyValuePair<string, object?>> animations = R.DictionaryEntries(R.Get(blueprint, "_codeAnimations"));
        if (animations.Count == 0) return false;

        List<KeyValuePair<string, object?>> hatchish = animations.Where(a => HatchTokens.IsMatch(a.Key)).ToList();
        if (hatchish.Count == 0) return false;

        string token = R.Token(group.Name);
        List<KeyValuePair<string, object?>> named = hatchish
            .Where(a => (token.Length > 0 && R.Token(a.Key).Contains(token))
                        || (cellIndex >= 0 && MatchesCell(a.Key, cellIndex)))
            .ToList();

        // One clearly named group, or exactly one hatch-like animation on the ship: use it.
        // Anything else is ambiguous and is left alone.
        List<KeyValuePair<string, object?>> candidates =
            named.Count > 0 ? named : hatchish.Count == 1 ? hatchish : new List<KeyValuePair<string, object?>>();

        if (candidates.Count == 0) return false;

        bool any = false;
        foreach (KeyValuePair<string, object?> candidate in candidates)
        {
            any |= SetAnimationState(candidate.Value, open);
        }

        if (any) how = "code animation " + string.Join(", ", candidates.Select(c => c.Key));
        return any;
    }

    /// <summary>
    /// True when an animation name looks like it belongs to one cell, e.g. "Cell7_Open" or
    /// "Hatch_7". Only ever applied to names that already look like a hatch.
    /// </summary>
    private static bool MatchesCell(string animationName, int cellIndex)
    {
        string token = R.Token(animationName);
        string number = (cellIndex + 1).ToString(CultureInfo.InvariantCulture);
        return token.Contains("cell" + number) || token.EndsWith(number, StringComparison.Ordinal);
    }

    private static bool SetAnimationState(object? animation, bool open)
    {
        if (animation == null) return false;

        if (open ? R.TryCall(animation, "setOpenState") : R.TryCall(animation, "setClosedState")) return true;
        if (R.TryCall(animation, open ? "Open" : "Close")) return true;
        if (R.TryCall(animation, "SetOpen", open)) return true;
        if (R.TryCall(animation, "SetState", open)) return true;
        if (R.TrySet(animation, "_open", open)) return true;
        if (R.TrySet(animation, "open", open)) return true;
        if (R.TrySet(animation, "IsOpen", open)) return true;
        return false;
    }

    // ------------------------------------------------------------------ misc

    private static IEnumerable<int> CellIndexes(HatchGroup group, int cellIndex)
    {
        if (cellIndex >= 0)
        {
            if (cellIndex < group.Cells.Count) yield return cellIndex;
            yield break;
        }

        for (int index = 0; index < group.Cells.Count; index++) yield return index;
    }

    private static string Name(object instance) =>
        R.Get(instance, "name") as string ?? instance.GetType().Name;
}
