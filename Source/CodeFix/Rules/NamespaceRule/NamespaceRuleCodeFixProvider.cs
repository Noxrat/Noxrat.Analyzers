using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Noxrat.Analyzers.NamespaceComputer;

namespace Noxrat.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NamespaceRuleCodeFixProvider)), Shared]
public sealed class NamespaceRuleCodeFixProvider : CodeFixProvider
{
    private static readonly string NamespaceRuleId = EDiagnosticId
        .NAMESPACE_DOES_NOT_MATCH_RULE.ExRule()
        .Id;

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(NamespaceRuleId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.FirstOrDefault(d => d.Id == NamespaceRuleId);
        if (diagnostic is null)
            return;

        var root = await context
            .Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null)
            return;

        var namespaceDeclaration = TryGetEnclosingNamespace(root, context.Span);
        if (namespaceDeclaration is null)
            return;

        var semanticModel = await context
            .Document.GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (semanticModel is null)
            return;

        var rule = NamespaceRule.TryCreate(semanticModel.Compilation);
        if (rule is null)
            return;

        var expectedNamespace = ComputeExpectedNamespace(
            rule.Value,
            context.Document.FilePath ?? string.Empty,
            context.Document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider
        );

        if (string.IsNullOrWhiteSpace(expectedNamespace))
            return;

        if (
            string.Equals(
                namespaceDeclaration.Name.ToString(),
                expectedNamespace,
                StringComparison.Ordinal
            )
        )
            return;

        var title = $"Change namespace to '{expectedNamespace}'";
        context.RegisterCodeFix(
            CodeAction.Create(
                title: title,
                createChangedDocument: ct =>
                    ApplyNamespaceFixAsync(
                        context.Document,
                        namespaceDeclaration,
                        expectedNamespace,
                        ct
                    ),
                equivalenceKey: $"{nameof(NamespaceRuleCodeFixProvider)}:{expectedNamespace}"
            ),
            diagnostic
        );
    }

    private static BaseNamespaceDeclarationSyntax? TryGetEnclosingNamespace(
        SyntaxNode root,
        TextSpan span
    )
    {
        var node = root.FindToken(span.Start).Parent;
        if (node is null)
            return null;

        var typeDeclaration = node.FirstAncestorOrSelf<BaseTypeDeclarationSyntax>();
        if (typeDeclaration is not null)
            return typeDeclaration.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();

        var delegateDeclaration = node.FirstAncestorOrSelf<DelegateDeclarationSyntax>();
        if (delegateDeclaration is not null)
            return delegateDeclaration.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();

        return node.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();
    }

    private static async Task<Document> ApplyNamespaceFixAsync(
        Document document,
        BaseNamespaceDeclarationSyntax namespaceDeclaration,
        string targetNamespace,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var replacement = namespaceDeclaration.WithName(
            SyntaxFactory.ParseName(targetNamespace).WithTriviaFrom(namespaceDeclaration.Name)
        );

        var newRoot = root.ReplaceNode(namespaceDeclaration, replacement);
        return document.WithSyntaxRoot(newRoot);
    }
}
