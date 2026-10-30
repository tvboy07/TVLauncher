using System;
using System.Collections.Generic;
using System.Linq;
using TVLauncher.Models;

namespace TVLauncher.Helpers;

/// <summary>
/// Evalúa las reglas (<c>rules</c>) del formato version.json de Minecraft
/// (sistema operativo, arquitectura y features).
/// </summary>
public static class RuleEvaluator
{
    public static bool IsAllowed(IEnumerable<RuleInfo>? rules, IReadOnlyDictionary<string, bool>? features = null)
    {
        if (rules == null)
            return true;

        var list = rules as IList<RuleInfo> ?? rules.ToList();
        if (list.Count == 0)
            return true;

        bool allowed = false;
        foreach (var rule in list)
        {
            if (!Matches(rule, features))
                continue;

            allowed = !string.Equals(rule.Action, "disallow", StringComparison.OrdinalIgnoreCase);
        }

        return allowed;
    }

    private static bool Matches(RuleInfo rule, IReadOnlyDictionary<string, bool>? features)
    {
        if (rule.Os != null)
        {
            if (!string.IsNullOrEmpty(rule.Os.Name) &&
                !rule.Os.Name.Equals(GetOsName(), StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(rule.Os.Arch) && !ArchMatches(rule.Os.Arch))
                return false;
        }

        if (rule.Features != null)
        {
            foreach (var feature in rule.Features)
            {
                bool current = features != null
                               && features.TryGetValue(feature.Key, out bool value)
                               && value;

                if (feature.Value != current)
                    return false;
            }
        }

        return true;
    }

    public static string GetOsName()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "osx";
        return "linux";
    }

    private static bool ArchMatches(string ruleArch)
    {
        string current = Environment.Is64BitProcess ? "x86_64" : "x86";

        if (ruleArch.Equals(current, StringComparison.OrdinalIgnoreCase))
            return true;

        return ruleArch.ToLowerInvariant() switch
        {
            "x86" => !Environment.Is64BitProcess,
            "amd64" or "x86_64" => Environment.Is64BitProcess,
            "arm64" or "aarch64" => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                                     == System.Runtime.InteropServices.Architecture.Arm64,
            _ => false
        };
    }
}
