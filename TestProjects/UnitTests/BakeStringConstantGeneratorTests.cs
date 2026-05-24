using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Noxrat.Analyzers;

namespace Noxrat.Tests;

public class BakeStringConstantGeneratorTests
{
    [Test]
    public void GeneratesFromExplicitFieldSymbolList()
    {
        const string source = """
            using Noxrat.Analyzers;

            namespace Sandbox;

            [BakeStringConstant("{field,separator:','}", listOfFields: [KEY1, KEY2, KEY3, CodeGenStringConstantOther.KEY1])]
            public static partial class CodeGenStringConstant
            {
                public const string KEY1 = "spawn_point";
                public const string KEY2 = "body_strength";
                public const string KEY3 = "body_count";
            }

            public static partial class CodeGenStringConstantOther
            {
                public const string KEY1 = "some_other_key";
            }
            """;

        var result = RunGenerator(source);

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources.Count, Is.EqualTo(1));
        Assert.That(
            result.GeneratedSources[0],
            Does.Contain(
                "[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Noxrat.Analyzers.BakeStringConstantGenerator\", \"2.0.0\")]"
            )
        );
        Assert.That(
            result.GeneratedSources[0],
            Does.Contain(
                "public const string BAKED_STRING = \"spawn_point,body_strength,body_count,some_other_key\";"
            )
        );
    }

    [Test]
    public void GeneratesUsingFieldCountToken()
    {
        const string source = """
            using Noxrat.Analyzers;

            namespace Sandbox;

            [BakeStringConstant("[{field,separator:', '}] has {field_count}", listOfFields: [KEY1, KEY2, KEY3])]
            public static partial class CodeGenStringConstant
            {
                public const string KEY1 = "field1";
                public const string KEY2 = "field2";
                public const string KEY3 = "field3";
            }
            """;

        var result = RunGenerator(source);

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources.Count, Is.EqualTo(1));
        Assert.That(
            result.GeneratedSources[0],
            Does.Contain("public const string BAKED_STRING = \"[field1, field2, field3] has 3\";")
        );
    }

    [Test]
    public void GeneratesUsingPrefixAndSuffixPerField()
    {
        const string source = """
            using Noxrat.Analyzers;

            namespace Sandbox;

            [BakeStringConstant("\\{\"expected\":[{field,separator:', ',prefix:\\\",suffix:\\\"}]\\}", listOfFields: [KEY1, KEY2, KEY3])]
            public static partial class CodeGenStringConstant
            {
                public const string KEY1 = "field1";
                public const string KEY2 = "field2";
                public const string KEY3 = "field3";
            }
            """;

        var result = RunGenerator(source);

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources.Count, Is.EqualTo(1));
        Assert.That(
            result.GeneratedSources[0],
            Does.Contain(
                "public const string BAKED_STRING = \"{\\\"expected\\\":[\\\"field1\\\", \\\"field2\\\", \\\"field3\\\"]}\";"
            )
        );
    }

    [Test]
    public void GeneratesMultipleAttributesWithDistinctVariableNames()
    {
        const string source = """
            using Noxrat.Analyzers;

            namespace Sandbox;

            [BakeStringConstant("PACK_A", "{field,separator:'|'}", listOfFields: [KEY1, KEY2])]
            [BakeStringConstant("PACK_B", "{field,separator:','}", listOfFields: [KEY2, KEY3])]
            public static partial class CodeGenStringConstant
            {
                public const string KEY1 = "one";
                public const string KEY2 = "two";
                public const string KEY3 = "three";
            }
            """;

        var result = RunGenerator(source);

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.GeneratedSources.Count, Is.EqualTo(2));
        Assert.That(
            result.GeneratedSources.Any(src =>
                src.Contains("public const string PACK_A = \"one|two\";")
            ),
            Is.True
        );
        Assert.That(
            result.GeneratedSources.Any(src =>
                src.Contains("public const string PACK_B = \"two,three\";")
            ),
            Is.True
        );
    }

    [Test]
    public void ReportsVariableNameCollisionForDuplicateAttributeNames()
    {
        const string source = """
            using Noxrat.Analyzers;

            namespace Sandbox;

            [BakeStringConstant("DUP", "{field,separator:','}", listOfFields: [KEY1])]
            [BakeStringConstant("DUP", "{field,separator:','}", listOfFields: [KEY2])]
            public static partial class CodeGenStringConstant
            {
                public const string KEY1 = "one";
                public const string KEY2 = "two";
            }
            """;

        var result = RunGenerator(source);
        var collisionId = EDiagnosticId.BAKE_STRING_CONSTANT_VARIABLE_NAME_COLLISION.ExRule().Id;

        Assert.That(result.Diagnostics.Count(d => d.Id == collisionId), Is.EqualTo(1));
        Assert.That(result.GeneratedSources.Count, Is.EqualTo(1));
        Assert.That(
            result.GeneratedSources[0],
            Does.Contain("public const string DUP = \"one\";")
        );
    }

    [Test]
    public void ReportsFieldIsNotConst()
    {
        const string source = """
            using Noxrat.Analyzers;

            namespace Sandbox;

            [BakeStringConstant("{field}", listOfFields: [KEY1])]
            public static partial class CodeGenStringConstant
            {
                public static readonly string KEY1 = "not_const";
            }
            """;

        var result = RunGenerator(source);
        var diagnosticId = EDiagnosticId.FIELD_IS_NOT_CONST.ExRule().Id;

        Assert.That(result.Diagnostics.Any(d => d.Id == diagnosticId), Is.True);
        Assert.That(result.GeneratedSources.Count, Is.EqualTo(0));
    }

    [Test]
    public void ReportsFieldIsNotAString()
    {
        const string source = """
            using Noxrat.Analyzers;

            namespace Sandbox;

            [BakeStringConstant("{field}", listOfFields: [KEY1])]
            public static partial class CodeGenStringConstant
            {
                public const int KEY1 = 5;
            }
            """;

        var result = RunGenerator(source);
        var diagnosticId = EDiagnosticId.FIELD_IS_NOT_A_STRING.ExRule().Id;

        Assert.That(result.Diagnostics.Any(d => d.Id == diagnosticId), Is.True);
        Assert.That(result.GeneratedSources.Count, Is.EqualTo(0));
    }

    [Test]
    public void ReportsEmptyFieldList()
    {
        const string source = """
            using Noxrat.Analyzers;

            namespace Sandbox;

            [BakeStringConstant("{field}", listOfFields: [])]
            public static partial class CodeGenStringConstant
            {
                public const string KEY1 = "value";
            }
            """;

        var result = RunGenerator(source);
        var diagnosticId = EDiagnosticId.BAKE_STRING_CONSTANT_FIELD_LIST_EMPTY.ExRule().Id;

        Assert.That(result.Diagnostics.Any(d => d.Id == diagnosticId), Is.True);
        Assert.That(result.GeneratedSources.Count, Is.EqualTo(0));
    }

    [Test]
    public void ReportsExistingMemberNameCollision()
    {
        const string source = """
            using Noxrat.Analyzers;

            namespace Sandbox;

            [BakeStringConstant("KEY1", "{field}", listOfFields: [KEY2])]
            public static partial class CodeGenStringConstant
            {
                public const string KEY1 = "already_here";
                public const string KEY2 = "other";
            }
            """;

        var result = RunGenerator(source);
        var diagnosticId = EDiagnosticId.BAKE_STRING_CONSTANT_VARIABLE_NAME_COLLISION.ExRule().Id;

        Assert.That(result.Diagnostics.Any(d => d.Id == diagnosticId), Is.True);
        Assert.That(result.GeneratedSources.Count, Is.EqualTo(0));
    }

    [Test]
    public void ReportsInvalidFormatWithNoTokens()
    {
        const string source = """
            using Noxrat.Analyzers;

            namespace Sandbox;

            [BakeStringConstant(",", listOfFields: [KEY1, KEY2])]
            public static partial class CodeGenStringConstant
            {
                public const string KEY1 = "one";
                public const string KEY2 = "two";
            }
            """;

        var result = RunGenerator(source);
        var diagnosticId = EDiagnosticId.BAKE_STRING_CONSTANT_FORMAT_INVALID.ExRule().Id;

        Assert.That(result.Diagnostics.Any(d => d.Id == diagnosticId), Is.True);
        Assert.That(result.GeneratedSources.Count, Is.EqualTo(0));
    }

    private static GeneratorResult RunGenerator(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "BakeStringConstantGeneratorTests",
            new[] { syntaxTree },
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new BakeStringConstantGenerator().AsSourceGenerator() },
            parseOptions: parseOptions
        );
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out _
        );

        var runResult = driver.GetRunResult();
        var generatedSources = runResult
            .Results.SelectMany(r => r.GeneratedSources)
            .Select(g => g.SourceText.ToString())
            .ToList();
        var diagnostics = runResult
            .Results.SelectMany(r => r.Diagnostics)
            .Concat(
                outputCompilation
                    .GetDiagnostics()
                    .Where(d => d.Id.StartsWith("Noxrat", StringComparison.Ordinal))
            )
            .GroupBy(d => $"{d.Id}|{d.Location}|{d.GetMessage()}")
            .Select(g => g.First())
            .ToList();

        return new GeneratorResult(generatedSources, diagnostics);
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(tpa))
        {
            foreach (var path in tpa.Split(Path.PathSeparator))
                refs.Add(path);
        }

        var analyzerAssemblyPath = typeof(BakeStringConstantAttribute).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(analyzerAssemblyPath))
            refs.Add(analyzerAssemblyPath);

        foreach (var path in refs)
            yield return MetadataReference.CreateFromFile(path);
    }

    private sealed class GeneratorResult
    {
        public GeneratorResult(List<string> generatedSources, List<Diagnostic> diagnostics)
        {
            GeneratedSources = generatedSources;
            Diagnostics = diagnostics;
        }

        public List<string> GeneratedSources { get; }
        public List<Diagnostic> Diagnostics { get; }
    }
}
