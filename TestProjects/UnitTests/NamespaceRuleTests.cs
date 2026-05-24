using System.IO;
using Microsoft;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Noxrat.Analyzers;
using Noxrat.Tests.Helpers;

namespace Noxrat.Tests;

public class NamespaceRuleTests
{
    [Test]
    public async Task ReportsDiagnosticWhenTypeNamespaceIsIncorrect()
    {
        var markedSource = """
            namespace Demo;

            public class {|#0:TestType|}
            {
            }
            """;

        var editorConfig = """
            root = true

            [*.cs]
            noxrat_namespace_root = Test.Namespace
            noxrat_namespace_folder_traversal_depth = 0
            """;

        var expected = new DiagnosticResult(
            EDiagnosticId.NAMESPACE_DOES_NOT_MATCH_RULE.ExRule().Id,
            DiagnosticSeverity.Warning
        ).WithLocation(0);

        await AnalyzerVerifier<NamespaceRuleAnalyzer>.VerifyAnalyzerAsync(
            editorConfig,
            [("TestType.cs", markedSource)],
            expected
        );
    }

    [Test]
    public async Task OffersCodeFixWhenTypeNamespaceIsIncorrect()
    {
        var markedSource = """
            namespace Demo;

            public class {|#0:TestType|}
            {
            }
            """;

        var fixedSource = """
            namespace Test.Namespace;

            public class TestType
            {
            }
            """;

        var editorConfig = """
            root = true

            [*.cs]
            noxrat_namespace_root = Test.Namespace
            noxrat_namespace_folder_traversal_depth = 0
            """;

        var expected = new DiagnosticResult(
            EDiagnosticId.NAMESPACE_DOES_NOT_MATCH_RULE.ExRule().Id,
            DiagnosticSeverity.Warning
        ).WithLocation(0);

        await CodeFixVerifier<
            NamespaceRuleAnalyzer,
            NamespaceRuleCodeFixProvider
        >.VerifyCodeFixAsync(
            editorConfig,
            [("TestType.cs", markedSource)],
            [("TestType.cs", fixedSource)],
            expected
        );
    }

    [Test]
    public async Task ScopedFolderConfigUsesScopeRelativeDepth()
    {
        var markedSource = """
            namespace SomeAddon.Bad;

            public class {|#0:FeatureType|}
            {
            }
            """;

        var editorConfig = """
            root = true

            [addons/SomeAddon/**/*.cs]
            noxrat_namespace_root = SomeAddon
            noxrat_namespace_scope_dir = addons/SomeAddon
            noxrat_namespace_folder_traversal_depth = 1
            """;

        var expected = new DiagnosticResult(
            EDiagnosticId.NAMESPACE_DOES_NOT_MATCH_RULE.ExRule().Id,
            DiagnosticSeverity.Warning
        ).WithLocation(0);

        await AnalyzerVerifier<NamespaceRuleAnalyzer>.VerifyAnalyzerAsync(
            editorConfig,
            [("addons/SomeAddon/Utils/FeatureType.cs", markedSource)],
            expected
        );
    }

    [Test]
    public async Task MultipleScopedConfigsAreAppliedPerFile()
    {
        var addonSource = """
            namespace Bad.Addons.Namespace;

            public class {|#0:AddonFeature|}
            {
            }
            """;

        var scriptSource = """
            namespace Bad.Scripts.Namespace;

            public class {|#1:ServiceDto|}
            {
            }
            """;

        var editorConfig = """
            root = true

            [addons/SomeAddon/**/*.cs]
            noxrat_namespace_root = SomeAddon
            noxrat_namespace_scope_dir = addons/SomeAddon
            noxrat_namespace_folder_traversal_depth = 1

            [Scripts/**/*.cs]
            noxrat_namespace_root = Project1.Core
            noxrat_namespace_scope_dir = Scripts
            noxrat_namespace_folder_traversal_depth = 1
            """;

        var expectedAddon = new DiagnosticResult(
            EDiagnosticId.NAMESPACE_DOES_NOT_MATCH_RULE.ExRule().Id,
            DiagnosticSeverity.Warning
        ).WithLocation(0);
        var expectedScript = new DiagnosticResult(
            EDiagnosticId.NAMESPACE_DOES_NOT_MATCH_RULE.ExRule().Id,
            DiagnosticSeverity.Warning
        ).WithLocation(1);

        await AnalyzerVerifier<NamespaceRuleAnalyzer>.VerifyAnalyzerAsync(
            editorConfig,
            [
                ("addons/SomeAddon/Utils/AddonFeature.cs", addonSource),
                ("Scripts/DTOs/ServiceDto.cs", scriptSource),
            ],
            expectedAddon,
            expectedScript
        );
    }

    [Test]
    public async Task UnmatchedFileWithoutRootConfigIsSkipped()
    {
        var source = """
            namespace Any.Namespace;

            public class TestType
            {
            }
            """;

        var editorConfig = """
            root = true

            [addons/**/*.cs]
            noxrat_namespace_root = SomeAddon
            noxrat_namespace_scope_dir = addons
            noxrat_namespace_folder_traversal_depth = 1
            """;

        await AnalyzerVerifier<NamespaceRuleAnalyzer>.VerifyAnalyzerAsync(
            editorConfig,
            [("Scripts/Program.cs", source)]
        );
    }

    [Test]
    public async Task CodeFixUsesScopedEditorConfigNamespace()
    {
        var markedSource = """
            namespace Wrong.Namespace;

            public class {|#0:ServiceDto|}
            {
            }
            """;

        var fixedSource = """
            namespace Project1.Core.DTOs;

            public class ServiceDto
            {
            }
            """;

        var editorConfig = """
            root = true

            [Scripts/**/*.cs]
            noxrat_namespace_root = Project1.Core
            noxrat_namespace_scope_dir = Scripts
            noxrat_namespace_folder_traversal_depth = 1
            """;

        var expected = new DiagnosticResult(
            EDiagnosticId.NAMESPACE_DOES_NOT_MATCH_RULE.ExRule().Id,
            DiagnosticSeverity.Warning
        ).WithLocation(0);

        await CodeFixVerifier<
            NamespaceRuleAnalyzer,
            NamespaceRuleCodeFixProvider
        >.VerifyCodeFixAsync(
            editorConfig,
            [("Scripts/DTOs/ServiceDto.cs", markedSource)],
            [("Scripts/DTOs/ServiceDto.cs", fixedSource)],
            expected
        );
    }

    [Test]
    public async Task ScopeDirSupportsBackslashAndSlashSeparators()
    {
        var source = """
            namespace Wrong.Namespace;

            public class {|#0:FeatureType|}
            {
            }
            """;

        var editorConfig = """
            root = true

            [addons/SomeAddon/**/*.cs]
            noxrat_namespace_root = SomeAddon
            noxrat_namespace_scope_dir = addons\SomeAddon
            noxrat_namespace_folder_traversal_depth = 1
            """;

        var expected = new DiagnosticResult(
            EDiagnosticId.NAMESPACE_DOES_NOT_MATCH_RULE.ExRule().Id,
            DiagnosticSeverity.Warning
        ).WithLocation(0);

        await AnalyzerVerifier<NamespaceRuleAnalyzer>.VerifyAnalyzerAsync(
            editorConfig,
            [("addons/SomeAddon/Utils/FeatureType.cs", source)],
            expected
        );
    }

    internal static class AnalyzerVerifier<TAnalyzer>
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        public static async Task VerifyAnalyzerAsync(
            string editorConfig,
            IEnumerable<(string path, string content)> sources,
            params DiagnosticResult[] expected
        )
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "NoxratAnalyzerTests");
            var configPath = Path.Combine(testRoot, ".editorconfig");

            var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
            {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            };

            foreach (var (path, content) in sources)
            {
                var fullPath = Path.Combine(
                    testRoot,
                    path.Replace('/', Path.DirectorySeparatorChar)
                );
                test.TestState.Sources.Add((fullPath, content));
            }

            test.TestState.AnalyzerConfigFiles.Add((configPath, editorConfig));
            test.TestState.AdditionalReferences.AddAnalyzersProjectReference();

            test.ExpectedDiagnostics.AddRange(expected);
            await test.RunAsync();
        }
    }

    internal static class CodeFixVerifier<TAnalyzer, TCodeFix>
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        public static async Task VerifyCodeFixAsync(
            string editorConfig,
            IEnumerable<(string path, string content)> sources,
            IEnumerable<(string path, string content)> fixedSources,
            params DiagnosticResult[] expected
        )
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "NoxratAnalyzerTests");
            var configPath = Path.Combine(testRoot, ".editorconfig");

            var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
            {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            };

            foreach (var (path, content) in sources)
            {
                var fullPath = Path.Combine(
                    testRoot,
                    path.Replace('/', Path.DirectorySeparatorChar)
                );
                test.TestState.Sources.Add((fullPath, content));
            }

            foreach (var (path, content) in fixedSources)
            {
                var fullPath = Path.Combine(
                    testRoot,
                    path.Replace('/', Path.DirectorySeparatorChar)
                );
                test.FixedState.Sources.Add((fullPath, content));
            }

            test.TestState.AnalyzerConfigFiles.Add((configPath, editorConfig));
            test.FixedState.AnalyzerConfigFiles.Add((configPath, editorConfig));
            test.TestState.AdditionalReferences.AddAnalyzersProjectReference();
            test.FixedState.AdditionalReferences.AddAnalyzersProjectReference();

            test.ExpectedDiagnostics.AddRange(expected);
            await test.RunAsync();
        }
    }
}
