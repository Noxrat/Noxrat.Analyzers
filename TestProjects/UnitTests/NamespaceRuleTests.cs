using Microsoft;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Noxrat.Analyzers;
using Noxrat.Tests.Helpers;

namespace Noxrat.Tests;

public class NamespaceRuleTests
{
    [Test]
    public async Task Test1()
    {
        var markedSource = """
            using Noxrat.Analyzers;

                [assembly: RootNamespace("Test.Namespace", folderTraversalDepth = 0)]
                namespace Demo;

                public class {|#0:TestType|}
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
            var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
            {
                TestCode = source,

                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            };

            test.TestState.AdditionalReferences.AddAnalyzersProjectReference();

            test.ExpectedDiagnostics.AddRange(expected);
            await test.RunAsync();
        }
    }
}
