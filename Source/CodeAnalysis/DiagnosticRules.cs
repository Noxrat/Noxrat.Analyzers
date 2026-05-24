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
        bakedDictionary.ExAddKV(
            DiagnosticRulesUtils.MakeUpRule(
                EDiagnosticId.FIELD_IS_NOT_CONST,
                "BakeStringConstant field is not const",
                "Field '{0}' must be const to be used in BakeStringConstant",
                DiagnosticSeverity.Error
            )
        );
        bakedDictionary.ExAddKV(
            DiagnosticRulesUtils.MakeUpRule(
                EDiagnosticId.FIELD_IS_NOT_A_STRING,
                "BakeStringConstant field has invalid type",
                "Field '{0}' must be of type string or char for BakeStringConstant",
                DiagnosticSeverity.Error
            )
        );
        bakedDictionary.ExAddKV(
            DiagnosticRulesUtils.MakeUpRule(
                EDiagnosticId.BAKE_STRING_CONSTANT_CLASS_MUST_BE_PARTIAL,
                "BakeStringConstant requires partial class",
                "Type '{0}' must be declared partial to use BakeStringConstant",
                DiagnosticSeverity.Error
            )
        );
        bakedDictionary.ExAddKV(
            DiagnosticRulesUtils.MakeUpRule(
                EDiagnosticId.BAKE_STRING_CONSTANT_FIELD_LIST_EMPTY,
                "BakeStringConstant requires at least one field",
                "BakeStringConstant on type '{0}' requires at least one field in listOfFields",
                DiagnosticSeverity.Error
            )
        );
        bakedDictionary.ExAddKV(
            DiagnosticRulesUtils.MakeUpRule(
                EDiagnosticId.BAKE_STRING_CONSTANT_VARIABLE_NAME_INVALID,
                "BakeStringConstant variable name is invalid",
                "Variable name '{0}' is not a valid C# identifier for BakeStringConstant",
                DiagnosticSeverity.Error
            )
        );
        bakedDictionary.ExAddKV(
            DiagnosticRulesUtils.MakeUpRule(
                EDiagnosticId.BAKE_STRING_CONSTANT_VARIABLE_NAME_COLLISION,
                "BakeStringConstant variable name collides",
                "Generated constant name '{0}' collides in type '{1}'",
                DiagnosticSeverity.Error
            )
        );
        bakedDictionary.ExAddKV(
            DiagnosticRulesUtils.MakeUpRule(
                EDiagnosticId.BAKE_STRING_CONSTANT_NESTED_CLASS_NOT_SUPPORTED,
                "BakeStringConstant does not support nested classes",
                "Nested type '{0}' is not supported by BakeStringConstant",
                DiagnosticSeverity.Error
            )
        );
        bakedDictionary.ExAddKV(
            DiagnosticRulesUtils.MakeUpRule(
                EDiagnosticId.BAKE_STRING_CONSTANT_FIELD_REFERENCE_INVALID,
                "BakeStringConstant field reference is invalid",
                "Expression '{0}' must reference a field symbol in listOfFields",
                DiagnosticSeverity.Error
            )
        );
        bakedDictionary.ExAddKV(
            DiagnosticRulesUtils.MakeUpRule(
                EDiagnosticId.BAKE_STRING_CONSTANT_FORMAT_INVALID,
                "Format schema is invalid",
                "Format string '{0}' is invalid: {1}",
                DiagnosticSeverity.Error
            )
        );
        diagnosticDescriptors = bakedDictionary.ToFrozenDictionary();
    }
}
