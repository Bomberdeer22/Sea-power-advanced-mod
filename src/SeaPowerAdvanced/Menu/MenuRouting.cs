using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using SeaPower;

namespace SeaPowerAdvanced.Menu;

/// <summary>
/// Puts each feature into the section of the game's own right-click menu where a player would look
/// for it, instead of piling everything into one submenu under Orders.
///
/// The game's root menu labels are not in the API dump, and hard-coding "Sensors" would be the same
/// mistake the offline scanner made with member names - a guess that silently finds nothing. So the
/// roots are discovered at runtime and matched on keyword fragments. Whatever is found is logged
/// once and written into capabilities.txt and menu-report.txt.
///
/// Routing rules, in order:
///
/// 1. the first section whose label matches one of the feature's keywords;
/// 2. the next section in the feature's fallback chain;
/// 3. the Orders section;
/// 4. the top level of the menu.
///
/// A feature never disappears because its section is missing; it just lands one level up, and the
/// log says so.
/// </summary>
internal static class MenuRouting
{
    internal static readonly string[] Sensors =
        { "sensor", "emcon", "radar", "electronic", "esm", "ecm", "signature", "detection" };

    // Deliberately narrow. An earlier version included "fire", which pulled unrelated entries in.
    internal static readonly string[] Weapons =
        { "weapon", "engage", "attack", "strike", "missile", "gun", "armament", "combat",
          "ordnance", "fire control" };

    internal static readonly string[] Rules =
        { "rules of engagement", "roe", "weapons status", "weapons control", "weapons tight",
          "weapons free", "readiness", "engagement rule" };

    internal static readonly string[] Formation =
        { "formation", "task force", "taskforce", "group", "tactic", "manoeuvre", "maneuver", "doctrine" };

    internal static readonly string[] Air =
        { "air operation", "air operations", "flight", "aircraft", "helo", "helicopter", "aviation" };

    internal static readonly string[] Damage =
        { "damage", "repair", "casualty" };

    internal static readonly string[] Logistics =
        { "supply", "logistic", "replenish", "fuel", "cargo" };

    internal static readonly string[] Orders = { "order", "command" };

    /// <summary>
    /// Menus this mod has already added entries to.
    ///
    /// This is a table of collection objects, not a scan of labels. An earlier version tried to
    /// recognise its own entries by name - and the game has its own "Local control" entry, so the
    /// check fired on the first menu and every feature was skipped. The rule now: if this exact
    /// collection has been handled, skip it; otherwise handle it, whatever the labels say.
    /// </summary>
    private static readonly ConditionalWeakTable<object, object> Handled = new();

    private static bool loggedRoots;
    private static List<string> rootLabels = new();

    /// <summary>Where each feature was placed in the last menu, for menu-report.txt.</summary>
    internal static readonly List<string> Placements = new();

    /// <summary>Every root label seen in the last menu the mod touched.</summary>
    internal static IReadOnlyList<string> RootLabels => rootLabels;

    /// <summary>True when this collection has already been augmented by us.</summary>
    internal static bool AlreadyHandled(object collection) => Handled.TryGetValue(collection, out _);

    internal static void MarkHandled(object collection)
    {
        try { Handled.Add(collection, collection); }
        catch (Exception) { }
    }

    /// <summary>Walks the top level of the menu the game just built, and logs the labels once.</summary>
    internal static List<ContextMenuItem> Roots(IEnumerable<ContextMenuItem> items)
    {
        var roots = new List<ContextMenuItem>();
        var labels = new List<string>();

        foreach (ContextMenuItem item in items)
        {
            if (item == null) continue;
            roots.Add(item);

            string? label = item.Label as string;
            if (!string.IsNullOrEmpty(label)) labels.Add(label!);
        }

        rootLabels = labels;

        if (!loggedRoots && labels.Count > 0)
        {
            loggedRoots = true;
            ModLog.Info("Context menu sections: " + string.Join(" | ", labels));
        }

        return roots;
    }

    /// <summary>
    /// Inserts an entry into the best section for it.
    ///
    /// <paramref name="live"/> is the game's own collection: anything that has to go at the top
    /// level must be added to that, not to the list snapshot taken by <see cref="Roots"/>.
    /// </summary>
    internal static string Place(List<ContextMenuItem> roots, object live, ContextMenuItem item,
                                 params string[][] routes)
    {
        if (routes.Length == 0 || routes.All(route => route.Length == 0))
        {
            AddTopLevel(live, item);
            return "top level";
        }

        foreach (string[] keywords in routes)
        {
            if (keywords.Length == 0) continue;

            ContextMenuItem? section = FindSection(roots, keywords);
            if (section?.Items == null) continue;

            section.Items.Add(item);
            return section.Label as string ?? "<unnamed>";
        }

        ContextMenuItem? orders = FindSection(roots, Orders);
        if (orders?.Items != null)
        {
            orders.Items.Add(item);
            return orders.Label as string ?? "Orders";
        }

        AddTopLevel(live, item);
        return "top level";
    }

    private static void AddTopLevel(object live, ContextMenuItem item)
    {
        switch (live)
        {
            case TrulyObservableCollection<ContextMenuItem> collection:
                collection.Add(item);
                return;
            case List<ContextMenuItem> list:
                list.Add(item);
                return;
        }
    }

    /// <summary>
    /// Finds the best section for a set of keywords.
    ///
    /// Roots are checked before their children, and exact label matches before substrings. One level
    /// of nesting is also searched, because this game puts things like "Engage" inside "Orders" -
    /// which is the right home for a strike order even though it is not a root.
    /// </summary>
    private static ContextMenuItem? FindSection(List<ContextMenuItem> roots, string[] keywords)
    {
        List<ContextMenuItem> children = ChildrenOf(roots);

        // Order matters and it is not arbitrary: an exact label match anywhere beats a loose
        // substring match anywhere. Without that, "Engage" nested inside Orders lost to
        // "Rules of engagement", because "engagement" happens to contain "engage".
        return Match(roots, keywords, exactOnly: true)
               ?? Match(children, keywords, exactOnly: true)
               ?? Match(roots, keywords, exactOnly: false)
               ?? Match(children, keywords, exactOnly: false);
    }

    /// <summary>Every child of every root that is itself a submenu.</summary>
    private static List<ContextMenuItem> ChildrenOf(List<ContextMenuItem> roots)
    {
        var children = new List<ContextMenuItem>();
        foreach (ContextMenuItem root in roots)
        {
            if (root?.Items == null) continue;
            foreach (ContextMenuItem child in root.Items)
                if (child != null) children.Add(child);
        }
        return children;
    }

    private static ContextMenuItem? Match(List<ContextMenuItem> candidates, string[] keywords, bool exactOnly)
    {
        foreach (ContextMenuItem candidate in candidates)
        {
            string? label = candidate?.Label as string;
            if (label == null || candidate!.Items == null) continue;

            string lower = label.ToLowerInvariant();
            foreach (string keyword in keywords)
            {
                if (exactOnly ? lower == keyword : lower.Contains(keyword)) return candidate;
            }
        }
        return null;
    }

    /// <summary>Section labels for capabilities.txt and menu-report.txt.</summary>
    internal static string Describe()
    {
        if (rootLabels.Count == 0) return "(no menu has been built yet this session)";
        var text = new StringBuilder();
        text.Append(string.Join(" | ", rootLabels));
        return text.ToString();
    }
}
