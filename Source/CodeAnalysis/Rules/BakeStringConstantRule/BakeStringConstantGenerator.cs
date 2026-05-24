using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Noxrat.Analyzers;

[Generator(LanguageNames.CSharp)]
public sealed class BakeStringConstantGenerator : IIncrementalGenerator
{
    private const string BAKE_STRING_CONSTANT_ATTRIBUTE_FQN =
        "Noxrat.Analyzers.BakeStringConstantAttribute";
    private const string GENERATED_CODE_TOOL = "Noxrat.Analyzers.BakeStringConstantGenerator";
    private const string GENERATED_CODE_VERSION = "2.0.0";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classTargets = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                BAKE_STRING_CONSTANT_ATTRIBUTE_FQN,
                static (node, _) => node is ClassDeclarationSyntax,
                static (generatorCtx, cancellationToken) =>
                    BuildClassTarget(generatorCtx, cancellationToken)
            )
            .Collect()
            .SelectMany(static (targets, _) => DeduplicateClassTargets(targets));

        context.RegisterSourceOutput(
            classTargets,
            static (sourceCtx, target) => ExecuteGeneration(sourceCtx, target)
        );
    }

    private static ClassTarget BuildClassTarget(
        GeneratorAttributeSyntaxContext generatorCtx,
        CancellationToken cancellationToken
    )
    {
        var classSymbol = (INamedTypeSymbol)generatorCtx.TargetSymbol;
        var classSyntax = (ClassDeclarationSyntax)generatorCtx.TargetNode;
        var attributes = ImmutableArray.CreateBuilder<AttributeInvocation>();

        foreach (var attributeData in generatorCtx.Attributes)
        {
            if (
                attributeData.ApplicationSyntaxReference?.GetSyntax(cancellationToken)
                is not AttributeSyntax attributeSyntax
            )
                continue;

            var listOfFieldExpressions = GetListOfFieldExpressions(attributeData, attributeSyntax);
            var fieldRefs = ImmutableArray.CreateBuilder<FieldReference>();

            foreach (var expression in listOfFieldExpressions)
            {
                var symbolInfo = generatorCtx.SemanticModel.GetSymbolInfo(expression, cancellationToken);
                var fieldSymbol =
                    symbolInfo.Symbol as IFieldSymbol
                    ?? (
                        symbolInfo.CandidateSymbols.Length == 1
                        ? symbolInfo.CandidateSymbols[0] as IFieldSymbol
                        : null
                    );

                fieldRefs.Add(
                    new FieldReference(
                        expression,
                        expression.GetLocation(),
                        expression.ToString(),
                        fieldSymbol
                    )
                );
            }

            var variableName = "BAKED_STRING";
            var format = string.Empty;
            ResolveAttributeStrings(attributeData, ref variableName, ref format);

            attributes.Add(
                new AttributeInvocation(
                    attributeSyntax.GetLocation(),
                    variableName,
                    format,
                    fieldRefs.ToImmutable()
                )
            );
        }

        return new ClassTarget(classSymbol, classSyntax, attributes.ToImmutable());
    }

    private static IEnumerable<ClassTarget> DeduplicateClassTargets(
        ImmutableArray<ClassTarget> targets
    )
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var target in targets)
        {
            if (seen.Add(target.ClassSymbol))
                yield return target;
        }
    }

    private static void ResolveAttributeStrings(
        AttributeData attributeData,
        ref string variableName,
        ref string format
    )
    {
        var ctor = attributeData.AttributeConstructor;
        if (ctor is null || ctor.Parameters.Length == 0 || attributeData.ConstructorArguments.Length == 0)
            return;

        var firstParamName = ctor.Parameters[0].Name;
        if (string.Equals(firstParamName, "format", StringComparison.Ordinal))
        {
            format = GetStringArgument(attributeData.ConstructorArguments[0], string.Empty);
            return;
        }

        if (string.Equals(firstParamName, "variableName", StringComparison.Ordinal))
        {
            variableName = GetStringArgument(attributeData.ConstructorArguments[0], "BAKED_STRING");
            if (attributeData.ConstructorArguments.Length > 1)
                format = GetStringArgument(attributeData.ConstructorArguments[1], string.Empty);
        }
    }

    private static string GetStringArgument(TypedConstant constant, string fallback)
    {
        return constant.Kind == TypedConstantKind.Primitive && constant.Value is string value
            ? value
            : fallback;
    }

    private static ImmutableArray<ExpressionSyntax> GetListOfFieldExpressions(
        AttributeData attributeData,
        AttributeSyntax attributeSyntax
    )
    {
        if (attributeSyntax.ArgumentList is null || attributeSyntax.ArgumentList.Arguments.Count == 0)
            return ImmutableArray<ExpressionSyntax>.Empty;

        var args = attributeSyntax.ArgumentList.Arguments;
        foreach (var arg in args)
        {
            var name =
                arg.NameColon?.Name.Identifier.ValueText ?? arg.NameEquals?.Name.Identifier.ValueText;
            if (!string.Equals(name, "listOfFields", StringComparison.Ordinal))
                continue;

            return ExpandFieldExpressions(arg.Expression);
        }

        var parameterIndex = GetListOfFieldsParameterIndex(attributeData);
        if (parameterIndex < 0)
            return ImmutableArray<ExpressionSyntax>.Empty;

        var positionalArgs = new List<AttributeArgumentSyntax>();
        foreach (var arg in args)
        {
            if (arg.NameColon is not null || arg.NameEquals is not null)
                continue;
            positionalArgs.Add(arg);
        }

        if (positionalArgs.Count <= parameterIndex)
            return ImmutableArray<ExpressionSyntax>.Empty;

        var result = ImmutableArray.CreateBuilder<ExpressionSyntax>();

        if (positionalArgs.Count == parameterIndex + 1)
        {
            result.AddRange(ExpandFieldExpressions(positionalArgs[parameterIndex].Expression));
            return result.ToImmutable();
        }

        for (var i = parameterIndex; i < positionalArgs.Count; i++)
            result.Add(positionalArgs[i].Expression);

        return result.ToImmutable();
    }

    private static int GetListOfFieldsParameterIndex(AttributeData attributeData)
    {
        var ctor = attributeData.AttributeConstructor;
        if (ctor is null)
            return -1;

        for (var i = 0; i < ctor.Parameters.Length; i++)
        {
            if (string.Equals(ctor.Parameters[i].Name, "listOfFields", StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static ImmutableArray<ExpressionSyntax> ExpandFieldExpressions(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case CollectionExpressionSyntax collectionExpression:
            {
                var result = ImmutableArray.CreateBuilder<ExpressionSyntax>();
                foreach (var element in collectionExpression.Elements)
                {
                    if (element is ExpressionElementSyntax expressionElement)
                        result.Add(expressionElement.Expression);
                }
                return result.ToImmutable();
            }
            case ArrayCreationExpressionSyntax arrayCreation
                when arrayCreation.Initializer is not null:
            {
                var result = ImmutableArray.CreateBuilder<ExpressionSyntax>();
                foreach (var entry in arrayCreation.Initializer.Expressions)
                    result.Add(entry);
                return result.ToImmutable();
            }
            case ImplicitArrayCreationExpressionSyntax implicitArrayCreation:
            {
                var result = ImmutableArray.CreateBuilder<ExpressionSyntax>();
                foreach (var entry in implicitArrayCreation.Initializer.Expressions)
                    result.Add(entry);
                return result.ToImmutable();
            }
            default:
                return ImmutableArray.Create(expression);
        }
    }

    private static void ExecuteGeneration(SourceProductionContext sourceCtx, ClassTarget target)
    {
        if (target.ClassSymbol.ContainingType is not null)
        {
            sourceCtx.ReportDiagnostic(
                Diagnostic.Create(
                    EDiagnosticId.BAKE_STRING_CONSTANT_NESTED_CLASS_NOT_SUPPORTED.ExRule(),
                    target.ClassSyntax.Identifier.GetLocation(),
                    target.ClassSymbol.ToDisplayString()
                )
            );
            return;
        }

        if (!IsPartial(target.ClassSyntax))
        {
            sourceCtx.ReportDiagnostic(
                Diagnostic.Create(
                    EDiagnosticId.BAKE_STRING_CONSTANT_CLASS_MUST_BE_PARTIAL.ExRule(),
                    target.ClassSyntax.Identifier.GetLocation(),
                    target.ClassSymbol.Name
                )
            );
            return;
        }

        var usedMemberNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in target.ClassSymbol.GetMembers())
            usedMemberNames.Add(member.Name);

        var generatedCount = 0;

        foreach (var attr in target.Attributes)
        {
            var location = attr.Location ?? target.ClassSyntax.Identifier.GetLocation();

            if (!IsValidIdentifier(attr.VariableName))
            {
                sourceCtx.ReportDiagnostic(
                    Diagnostic.Create(
                        EDiagnosticId.BAKE_STRING_CONSTANT_VARIABLE_NAME_INVALID.ExRule(),
                        location,
                        attr.VariableName
                    )
                );
                continue;
            }

            if (usedMemberNames.Contains(attr.VariableName))
            {
                sourceCtx.ReportDiagnostic(
                    Diagnostic.Create(
                        EDiagnosticId.BAKE_STRING_CONSTANT_VARIABLE_NAME_COLLISION.ExRule(),
                        location,
                        attr.VariableName,
                        target.ClassSymbol.ToDisplayString()
                    )
                );
                continue;
            }

            if (attr.FieldReferences.Length == 0)
            {
                sourceCtx.ReportDiagnostic(
                    Diagnostic.Create(
                        EDiagnosticId.BAKE_STRING_CONSTANT_FIELD_LIST_EMPTY.ExRule(),
                        location,
                        target.ClassSymbol.ToDisplayString()
                    )
                );
                continue;
            }

            var fieldValues = new List<string>(attr.FieldReferences.Length);
            var hasErrors = false;
            foreach (var fieldRef in attr.FieldReferences)
            {
                if (fieldRef.FieldSymbol is null)
                {
                    sourceCtx.ReportDiagnostic(
                        Diagnostic.Create(
                            EDiagnosticId.BAKE_STRING_CONSTANT_FIELD_REFERENCE_INVALID.ExRule(),
                            fieldRef.Location ?? location,
                            fieldRef.ExpressionText
                        )
                    );
                    hasErrors = true;
                    continue;
                }

                if (!fieldRef.FieldSymbol.HasConstantValue)
                {
                    sourceCtx.ReportDiagnostic(
                        Diagnostic.Create(
                            EDiagnosticId.FIELD_IS_NOT_CONST.ExRule(),
                            fieldRef.Location ?? location,
                            fieldRef.FieldSymbol.ToDisplayString()
                        )
                    );
                    hasErrors = true;
                    continue;
                }

                var type = fieldRef.FieldSymbol.Type;
                var isString = type.SpecialType == SpecialType.System_String;
                var isChar = type.SpecialType == SpecialType.System_Char;
                if (!isString && !isChar)
                {
                    sourceCtx.ReportDiagnostic(
                        Diagnostic.Create(
                            EDiagnosticId.FIELD_IS_NOT_A_STRING.ExRule(),
                            fieldRef.Location ?? location,
                            fieldRef.FieldSymbol.ToDisplayString()
                        )
                    );
                    hasErrors = true;
                    continue;
                }

                var rawValue = fieldRef.FieldSymbol.ConstantValue;
                if (rawValue is string stringValue)
                    fieldValues.Add(stringValue);
                else if (rawValue is char charValue)
                    fieldValues.Add(charValue.ToString());
                else
                    fieldValues.Add(string.Empty);
            }

            if (hasErrors)
                continue;

            if (fieldValues.Count == 0)
                continue;

            if (
                !CodegenFormatTemplate.TryParse(
                    attr.Format,
                    out var parsedTemplate,
                    out var formatError
                )
                || parsedTemplate is null
            )
            {
                sourceCtx.ReportDiagnostic(
                    Diagnostic.Create(
                        EDiagnosticId.BAKE_STRING_CONSTANT_FORMAT_INVALID.ExRule(),
                        location,
                        attr.Format,
                        formatError
                    )
                );
                continue;
            }

            var bakedValue = parsedTemplate.Render(fieldValues);
            var generatedSource = GenerateClassFragment(target.ClassSymbol, attr.VariableName, bakedValue);
            var hintName =
                $"{SanitizeFileToken(target.ClassSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))}.{attr.VariableName}.{generatedCount}.g.cs";
            sourceCtx.AddSource(hintName, generatedSource);
            usedMemberNames.Add(attr.VariableName);
            generatedCount++;
        }
    }

    private static bool IsPartial(ClassDeclarationSyntax classDeclaration)
    {
        foreach (var modifier in classDeclaration.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.PartialKeyword))
                return true;
        }
        return false;
    }

    private static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (!SyntaxFacts.IsValidIdentifier(value))
            return false;
        if (SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None)
            return false;
        if (SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None)
            return false;
        return true;
    }

    private static string GenerateClassFragment(
        INamedTypeSymbol classSymbol,
        string variableName,
        string bakedValue
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");

        if (!classSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            sb.Append("namespace ").Append(classSymbol.ContainingNamespace.ToDisplayString()).AppendLine(";");
            sb.AppendLine();
        }

        var accessibility = GetAccessibilityToken(classSymbol.DeclaredAccessibility);
        if (!string.IsNullOrEmpty(accessibility))
            sb.Append(accessibility).Append(' ');

        if (classSymbol.IsStatic)
            sb.Append("static ");
        else
        {
            if (classSymbol.IsAbstract)
                sb.Append("abstract ");
            if (classSymbol.IsSealed)
                sb.Append("sealed ");
        }

        sb.Append("partial class ").Append(classSymbol.Name);
        if (classSymbol.TypeParameters.Length > 0)
        {
            sb.Append('<');
            for (var i = 0; i < classSymbol.TypeParameters.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(classSymbol.TypeParameters[i].Name);
            }
            sb.Append('>');
        }

        sb.AppendLine();
        sb.AppendLine("{");
        sb.Append("    [global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"");
        sb.Append(GENERATED_CODE_TOOL);
        sb.Append("\", \"");
        sb.Append(GENERATED_CODE_VERSION);
        sb.AppendLine("\")]");
        sb.Append("    public const string ").Append(variableName).Append(" = \"");
        sb.Append(EscapeStringLiteral(bakedValue));
        sb.AppendLine("\";");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string EscapeStringLiteral(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string GetAccessibilityToken(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => string.Empty,
        };
    }

    private static string SanitizeFileToken(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "generated";

        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '.')
                chars[i] = '_';
        }
        return new string(chars);
    }

    private sealed class ClassTarget
    {
        public ClassTarget(
            INamedTypeSymbol classSymbol,
            ClassDeclarationSyntax classSyntax,
            ImmutableArray<AttributeInvocation> attributes
        )
        {
            ClassSymbol = classSymbol;
            ClassSyntax = classSyntax;
            Attributes = attributes;
        }

        public INamedTypeSymbol ClassSymbol { get; }
        public ClassDeclarationSyntax ClassSyntax { get; }
        public ImmutableArray<AttributeInvocation> Attributes { get; }
    }

    private readonly struct AttributeInvocation
    {
        public AttributeInvocation(
            Location? location,
            string variableName,
            string format,
            ImmutableArray<FieldReference> fieldReferences
        )
        {
            Location = location;
            VariableName = variableName;
            Format = format;
            FieldReferences = fieldReferences;
        }

        public Location? Location { get; }
        public string VariableName { get; }
        public string Format { get; }
        public ImmutableArray<FieldReference> FieldReferences { get; }
    }

    private readonly struct FieldReference
    {
        public FieldReference(
            ExpressionSyntax expressionSyntax,
            Location? location,
            string expressionText,
            IFieldSymbol? fieldSymbol
        )
        {
            ExpressionSyntax = expressionSyntax;
            Location = location;
            ExpressionText = expressionText;
            FieldSymbol = fieldSymbol;
        }

        public ExpressionSyntax ExpressionSyntax { get; }
        public Location? Location { get; }
        public string ExpressionText { get; }
        public IFieldSymbol? FieldSymbol { get; }
    }
}
