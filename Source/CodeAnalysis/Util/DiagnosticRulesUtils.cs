using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Noxrat.Analyzers.CodeAnalysis;

public static class DiagnosticRulesUtils
{
    public static void ExAddKV<T, V>(this Dictionary<T, V> dict, KeyValuePair<T, V> kv)
    {
        dict.Add(kv.Key, kv.Value);
    }

    public static DiagnosticDescriptor ExRule(this EDiagnosticId id)
    {
        return DiagnosticRules.diagnosticDescriptors[id];
    }

    public static KeyValuePair<EDiagnosticId, DiagnosticDescriptor> MakeUpRule(
        EDiagnosticId id,
        string title,
        string messageFormat,
        DiagnosticSeverity severity = DiagnosticSeverity.Warning
    )
    {
        return new KeyValuePair<EDiagnosticId, DiagnosticDescriptor>(
            id,
            new DiagnosticDescriptor(
                //
                id: $"NoxRaven{((int)id):D4}",
                title: title,
                messageFormat: messageFormat,
                category: "NoxRaven.Sandbox",
                defaultSeverity: severity,
                isEnabledByDefault: true
            )
        );
    }
}
