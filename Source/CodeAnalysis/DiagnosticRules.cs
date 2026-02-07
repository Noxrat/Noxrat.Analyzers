using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Noxrat.Analyzers;

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
                "Type {0} has namespace '{1}' but expected '{2}'",
                DiagnosticSeverity.Warning
            )
        );
        bakedDictionary.ExAddKV(
            DiagnosticRulesUtils.MakeUpRule(
                EDiagnosticId.REQUIRE_ATTRIBUTE_DOESNT_CONTAIN_ATTRIBUTE,
                "Requires attribute on the type is not found",
                "Type {0} does not contain required attributes: {1}",
                DiagnosticSeverity.Warning
            )
        );
        diagnosticDescriptors = bakedDictionary.ToFrozenDictionary();
    }
}
