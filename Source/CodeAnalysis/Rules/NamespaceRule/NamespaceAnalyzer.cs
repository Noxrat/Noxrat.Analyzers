using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using static Noxrat.Analyzers.NamespaceComputer;

namespace Noxrat.Analyzers;

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
            var config = startCtx.Options.AnalyzerConfigOptionsProvider;
            var projectDir = NamespaceComputer.TryGetProjectDir(config);
            var cachedEffectiveRules =
                new ConcurrentDictionary<SyntaxTree, NamespaceComputer.EffectiveNamespaceRule?>();

            startCtx.RegisterSymbolAction(
                symbolCtx => AnalyzeNamedType(symbolCtx, config, projectDir, cachedEffectiveRules),
                SymbolKind.NamedType
            );
        });
    }

    private static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        AnalyzerConfigOptionsProvider config,
        string? projectDir,
        ConcurrentDictionary<
            SyntaxTree,
            NamespaceComputer.EffectiveNamespaceRule?
        > cachedEffectiveRules
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

            var effectiveRule = cachedEffectiveRules.GetOrAdd(
                tree,
                t => NamespaceComputer.TryGetEffectiveNamespaceRule(t, config, projectDir)
            );

            if (effectiveRule is null)
                continue;

            var actual = typeSymbol.ContainingNamespace.ToDisplayString();
            var expected = effectiveRule.Value.expectedNamespace;

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
