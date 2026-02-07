using Microsoft;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Noxrat.Analyzers;
using Noxrat.Analyzers.CodeAnalysis;

namespace UnitTests;

public class Tests
{
    [Test]
    public async Task Test1()
    {
        var source = """
            using Noxrat.Analyzers;

                [assembly: RootNamespace("Test.Namespace", folderTraversalDepth = 0)]
                namespace Demo;

                public class C
                {
                }
            """;

        // The markup {|#0:token|} tags a location we can refer to.
        // Put it on the first token you expect the diagnostic to be reported on.
        var markedSource = """
            using Noxrat.Analyzers;

                [assembly: RootNamespace("Test.Namespace", folderTraversalDepth = 0)]
                {|#0:namespace|} Demo;

                public class C
                {
                }
            """;

        var expected = new DiagnosticResult(
            EDiagnosticId.NAMESPACE_DOES_NOT_MATCH_RULE.ExRule().Id,
            DiagnosticSeverity.Warning
        ).WithLocation(0);

        await AnalyzerVerifier<NamespaceRuleAnalyzer>.VerifyAnalyzerAsync(markedSource, expected);
    }

    internal static class AnalyzerVerifier<TAnalyzer>
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        public static async Task VerifyAnalyzerAsync(
            string source,
            params DiagnosticResult[] expected
        )
        {
            var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> { TestCode = source };

            test.ExpectedDiagnostics.AddRange(expected);
            await test.RunAsync();
        }
    }
}
