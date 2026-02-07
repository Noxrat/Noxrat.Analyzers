using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Noxrat.Analyzers.CodeAnalysis;

namespace Noxrat.Analyzers.CodeAnalysis;

public static partial class DiagnosticRules
{
    public static readonly FrozenDictionary<
        EDiagnosticId,
        DiagnosticDescriptor
    > diagnosticDescriptors;

    static DiagnosticRules()
    {
        var bakedDictionary = new Dictionary<EDiagnosticId, DiagnosticDescriptor>();
        bakedDictionary.ExAddKV(
            DiagnosticRulesUtils.MakeUpRule(
                EDiagnosticId.NAMESPACE_DOES_NOT_MATCH_RULE,
                "Namespace does not match rule",
                "File {0} does not match the root namespace rule",
                DiagnosticSeverity.Error
            )
        );
        diagnosticDescriptors = bakedDictionary.ToFrozenDictionary();
    }
}
