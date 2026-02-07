using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using static Noxrat.Analyzers.CodeAnalysis.NamespaceComputer;

namespace Noxrat.Analyzers.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NamespaceRuleAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            [
                //
                EDiagnosticId.NAMESPACE_DOES_NOT_MATCH_RULE.ExRule(),
            ]
        );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(startCtx =>
        {
            var rule = NamespaceRule.TryCreate(startCtx.Compilation);
            if (rule is null)
                return;

            startCtx.RegisterSymbolAction(
                symbolCtx => AnalyzeNamedType(symbolCtx, rule.Value, startCtx.Options),
                SymbolKind.NamedType
            );
        });
    }

    private static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        NamespaceRule rule,
        AnalyzerOptions options
    )
    {
        if (context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        // Only enforce for top-level types (namespace is determined here).
        if (typeSymbol.ContainingType is not null)
            return;

        if (typeSymbol.IsImplicitlyDeclared)
            return;

        // CA1050 will already yell about global namespace. We skip it.
        if (
            typeSymbol.ContainingNamespace is null
            || typeSymbol.ContainingNamespace.IsGlobalNamespace
        )
            return;

        // If partial across multiple files, validate each declaration independently.
        foreach (var location in typeSymbol.Locations)
        {
            if (!location.IsInSource)
                continue;

            var tree = location.SourceTree;
            if (tree is null)
                continue;

            var expected = NamespaceComputer.ComputeExpectedNamespace(
                rule,
                tree.FilePath,
                options.AnalyzerConfigOptionsProvider
            );

            var actual = typeSymbol.ContainingNamespace.ToDisplayString();

            if (StringComparer.Ordinal.Equals(actual, expected))
                continue;

            // Report on identifier in THIS tree (for partial types).
            var decl = typeSymbol
                .DeclaringSyntaxReferences.Select(r => r.GetSyntax(context.CancellationToken))
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(d => d.SyntaxTree == tree);

            var reportLocation = decl?.Identifier.GetLocation() ?? location;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    EDiagnosticId.NAMESPACE_DOES_NOT_MATCH_RULE.ExRule(),
                    reportLocation,
                    typeSymbol.Name,
                    actual,
                    expected
                )
            );
        }
    }
}
