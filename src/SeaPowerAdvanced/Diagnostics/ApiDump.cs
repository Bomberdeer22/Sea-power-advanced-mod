using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SeaPower;

namespace SeaPowerAdvanced.Diagnostics;

/// <summary>
/// Writes <c>hatch-api-dump.txt</c> next to the mod DLL once per launch.
///
/// The game does not document how a ship opens a silo hatch, and no public mod touches it, so the
/// mod reads the assembly at runtime and reports the member names that could be the handle. The
/// interesting section is "members with hatch/cover/lid/silo/door in the name": that is where the
/// real per-cell hatch member shows up, if it exists as a named member at all.
///
/// The file is small, contains no player data, and can be deleted at any time.
/// </summary>
internal static class ApiDump
{
    private static readonly Regex Hatchish =
        new("hatch|silo|cover|lid|door|canopy|cell", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TypeOfInterest =
        new("hatch|silo|cover|lid|door|container|launcher|weapon|codeanimation|objectblueprint|animat",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static void Write(string reason)
    {
        try
        {
            var text = new StringBuilder();
            Assembly game = typeof(ObjectBase).Assembly;

            text.AppendLine("Sea Power Advanced " + ModInfo.Version + " - game API dump (" + reason + ")");
            text.AppendLine("Written:       " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("Assembly:      " + game.GetName().Name + " " + game.GetName().Version);
            text.AppendLine("Animator type: " + (ResolveAnimator()?.FullName ?? "NOT FOUND"));
            text.AppendLine();

            Type[] types = SafeTypes(game);
            text.AppendLine("Types scanned: " + types.Length);
            text.AppendLine();

            text.AppendLine("== 1. Every member whose name looks like a hatch (all types) ==");
            text.AppendLine("   This is the section that identifies the real hatch handle.");
            foreach (Type type in types) AppendHatchishMembers(text, type);
            text.AppendLine();

            text.AppendLine("== 2. Full member list for container / launcher / animation types ==");
            foreach (Type type in types.Where(t => TypeOfInterest.IsMatch(t.Name)).OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                AppendType(text, type);
            }

            string path = ModFolders.WriteText("hatch-api-dump.txt", text.ToString());
            ModLog.Info("API dump written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("API dump failed", error);
        }
    }

    private static void AppendHatchishMembers(StringBuilder text, Type type)
    {
        try
        {
            foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!Hatchish.IsMatch(member.Name)) continue;
                text.AppendLine("  " + type.FullName + "." + Describe(member));
            }
        }
        catch (Exception) { }
    }

    private static void AppendType(StringBuilder text, Type type)
    {
        try
        {
            text.AppendLine();
            text.AppendLine("### " + type.FullName + (type.BaseType != null ? "  :  " + type.BaseType.Name : string.Empty));

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                text.AppendLine("  field    " + field.FieldType.Name + " " + field.Name);
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                text.AppendLine("  property " + property.PropertyType.Name + " " + property.Name);
            }

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                text.AppendLine("  method   " + Describe(method));
            }
        }
        catch (Exception) { }
    }

    private static string Describe(MemberInfo member)
    {
        try
        {
            return member switch
            {
                MethodInfo method =>
                    method.Name + "(" + string.Join(", ", method.GetParameters()
                        .Select(p => p.ParameterType.Name + " " + p.Name)) + ") : " + method.ReturnType.Name,
                PropertyInfo property => property.Name + " : " + property.PropertyType.Name,
                FieldInfo field => field.Name + " : " + field.FieldType.Name,
                _ => member.Name
            };
        }
        catch (Exception)
        {
            return member.Name;
        }
    }

    private static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException error) { return error.Types.Where(t => t != null).Cast<Type>().ToArray(); }
        catch (Exception) { return Array.Empty<Type>(); }
    }

    private static Type? ResolveAnimator()
    {
        foreach (string assemblyName in new[] { "UnityEngine.AnimationModule", "UnityEngine.CoreModule" })
        {
            Type? type = Type.GetType("UnityEngine.Animator, " + assemblyName, false);
            if (type != null) return type;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType("UnityEngine.Animator", false);
            if (type != null) return type;
        }

        return null;
    }
}
