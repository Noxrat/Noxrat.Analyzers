using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace Noxrat.Tests;

public static class AnalyzerVerifierHelper
{
    public static void AddAnalyzersProjectReference(
        this MetadataReferenceCollection metadataReferences
    )
    {
        // yeah
        var attrAssembly = typeof(Analyzers.RootNamespaceAttribute).Assembly;
        if (string.IsNullOrWhiteSpace(attrAssembly.Location))
            throw new InvalidOperationException(
                $"Broke reference to Noxrat.Analyzers assembly {attrAssembly.FullName}"
            );
        metadataReferences.Add(MetadataReference.CreateFromFile(attrAssembly.Location));
    }
}
