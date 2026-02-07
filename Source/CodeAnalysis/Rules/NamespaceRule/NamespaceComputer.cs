using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Noxrat.Analyzers.CodeAnalysis;

public static class NamespaceComputer
{
    public const string ROOT_NAMESPACE_FQN = "Noxrat.Analyzers.RootNamespaceAttribute";
    public static string ComputeExpectedNamespace(
        NamespaceRule rule,
        string filePath,
        AnalyzerConfigOptionsProvider config
    )
    {
        var root = rule.rootNamespace.Trim('.');
        if (rule.depth <= 0 || string.IsNullOrWhiteSpace(filePath))
            return root;

        var projectDir = TryGetProjectDir(config);
        var relativeDir = GetRelativeDirectory(projectDir, filePath); // "Foo/Bar" etc.

        if (string.IsNullOrWhiteSpace(relativeDir))
            return root;

        var parts = relativeDir
            .Split(new[] { '/', '\\' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Take(rule.depth)
            .Select(MakeValidNamespaceSegment)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        if (parts.Length == 0)
            return root;

        return root + "." + string.Join(".", parts);
    }

    private static string? TryGetProjectDir(AnalyzerConfigOptionsProvider config)
    {
        var global = config.GlobalOptions;

        if (
            global.TryGetValue("build_property.ProjectDir", out var p1)
            && !string.IsNullOrWhiteSpace(p1)
        )
            return p1;

        if (
            global.TryGetValue("build_property.MSBuildProjectDirectory", out var p2)
            && !string.IsNullOrWhiteSpace(p2)
        )
            return p2 + Path.DirectorySeparatorChar;

        return null;
    }

    private static string GetRelativeDirectory(string? projectDir, string filePath)
    {
        try
        {
            var fileDir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(fileDir))
                return "";

            if (!string.IsNullOrWhiteSpace(projectDir))
            {
                // Normalize a bit
                projectDir = Path.GetFullPath(projectDir);
                fileDir = Path.GetFullPath(fileDir);

                if (fileDir.StartsWith(projectDir, System.StringComparison.OrdinalIgnoreCase))
                {
                    var rel = fileDir.Substring(projectDir.Length);
                    return rel.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
            }

            // fallback: just use the file's directory name (not ideal, but better than nothing)
            return fileDir;
        }
        catch
        {
            return "";
        }
    }

    private static string MakeValidNamespaceSegment(string raw)
    {
        // Simple sanitization: turn invalid chars into '_' and ensure identifier rules
        var chars = raw.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var candidate = new string(chars);

        if (string.IsNullOrWhiteSpace(candidate))
            return "";

        if (!SyntaxFacts.IsIdentifierStartCharacter(candidate[0]))
            candidate = "_" + candidate;

        // If still not a valid identifier, keep patching
        if (!SyntaxFacts.IsValidIdentifier(candidate))
        {
            candidate = new string(
                candidate
                    .Select(ch => SyntaxFacts.IsIdentifierPartCharacter(ch) ? ch : '_')
                    .ToArray()
            );
            if (!SyntaxFacts.IsIdentifierStartCharacter(candidate[0]))
                candidate = "_" + candidate;
        }

        return candidate;
    }

    public readonly record struct NamespaceRule
    {
        public readonly string rootNamespace;
        public readonly int depth;

        public NamespaceRule(string rootNamespace, int depth)
        {
            this.rootNamespace = rootNamespace;
            this.depth = depth;
        }

        public static NamespaceRule? TryCreate(Compilation compilation)
        {
            var attrType = compilation.GetTypeByMetadataName(ROOT_NAMESPACE_FQN);
            if (attrType is null)
                return null;

            foreach (var attr in compilation.Assembly.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attrType))
                    continue;

                // ctor arg: rootNamespace
                var root =
                    attr.ConstructorArguments.Length > 0
                        ? attr.ConstructorArguments[0].Value as string
                        : null;

                if (string.IsNullOrWhiteSpace(root))
                    return null;

                // named arg: Depth property
                var depth = 0;
                foreach (var kv in attr.NamedArguments)
                {
                    if (
                        string.Equals(kv.Key, "Depth", StringComparison.OrdinalIgnoreCase)
                        && kv.Value.Value is int d
                    )
                    {
                        depth = d;
                    }
                }

                if (depth < 0)
                    depth = 0;
                if (depth > 2)
                    depth = 2; // you said 0-2 preferred

                return new NamespaceRule(root!, depth);
            }

            return null;
        }
    }
}
