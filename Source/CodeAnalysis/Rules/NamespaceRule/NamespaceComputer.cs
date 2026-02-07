using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Noxrat.Analyzers;

public static class NamespaceComputer
{
    public const string ROOT_NAMESPACE_FQN = "Noxrat.Analyzers.RootNamespaceAttribute";

    public static string ComputeExpectedNamespace(
        NamespaceRule rule,
        string filePath,
        AnalyzerConfigOptionsProvider config
    )
    {
        var root = (rule.rootNamespace ?? "").Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(root))
            return "";

        if (rule.depth <= 0 || string.IsNullOrWhiteSpace(filePath))
            return root;

        var projectDir = TryGetProjectDir(config);
        var relativeDir = GetRelativeDirectory(projectDir, filePath); // "Foo/Bar" etc.

        if (string.IsNullOrWhiteSpace(relativeDir))
            return root;

        var parts = relativeDir
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
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

        string? projectDir = null;

        if (
            global.TryGetValue("build_property.ProjectDir", out var p1)
            && !string.IsNullOrWhiteSpace(p1)
        )
            projectDir = p1;
        else if (
            global.TryGetValue("build_property.MSBuildProjectDirectory", out var p2)
            && !string.IsNullOrWhiteSpace(p2)
        )
            projectDir = p2;

        if (string.IsNullOrWhiteSpace(projectDir))
            return null;

        try
        {
            projectDir = Path.GetFullPath(projectDir);
            return EnsureTrailingSeparator(projectDir);
        }
        catch
        {
            return null;
        }
    }

    private static string GetRelativeDirectory(string? projectDir, string filePath)
    {
        try
        {
            var fileDir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(fileDir))
                return "";

            // If we can't determine the project root, DO NOT fall back to absolute paths, something must be wrong with set up, so we just skip rule checking.
            if (string.IsNullOrWhiteSpace(projectDir))
                return "";

            var fullProjectDir = EnsureTrailingSeparator(Path.GetFullPath(projectDir));
            var fullFileDir = EnsureTrailingSeparator(Path.GetFullPath(fileDir));

            var projectUri = new Uri(fullProjectDir, UriKind.Absolute);
            var fileUri = new Uri(fullFileDir, UriKind.Absolute);

            // Only compute relative if file is under project dir.
            if (!projectUri.IsBaseOf(fileUri))
                return "";

            var relUri = projectUri.MakeRelativeUri(fileUri);
            var rel = Uri.UnescapeDataString(relUri.ToString());

            // Uri uses '/', normalize to OS separators then trim.
            rel = rel.Replace('/', Path.DirectorySeparatorChar);
            return rel.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return "";
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        var sep = Path.DirectorySeparatorChar;
        var alt = Path.AltDirectorySeparatorChar;

        if (path[path.Length - 1] == sep || path[path.Length - 1] == alt)
            return path;

        return path + sep;
    }

    private static string MakeValidNamespaceSegment(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        // Normalize characters
        var chars = raw.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray();
        var candidate = new string(chars);

        if (candidate.Length == 0)
            return "";

        // Ensure valid start
        if (!SyntaxFacts.IsIdentifierStartCharacter(candidate[0]))
            candidate = "_" + candidate;

        // Ensure all parts valid
        var arr = candidate.ToCharArray();
        for (int i = 1; i < arr.Length; i++)
        {
            if (!SyntaxFacts.IsIdentifierPartCharacter(arr[i]))
                arr[i] = '_';
        }
        candidate = new string(arr);

        // Avoid keywords/contextual keywords & ensure validity
        if (
            !SyntaxFacts.IsValidIdentifier(candidate)
            || SyntaxFacts.GetKeywordKind(candidate)
                != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            || SyntaxFacts.GetContextualKeywordKind(candidate)
                != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
        )
        {
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

                var root =
                    attr.ConstructorArguments.Length > 0
                        ? attr.ConstructorArguments[0].Value as string
                        : null;

                if (string.IsNullOrWhiteSpace(root))
                    return null;

                var depth = 0;
                if (
                    attr.ConstructorArguments.Length > 1
                    && attr.ConstructorArguments[1].Value is int dCtor
                )
                    depth = dCtor;

                foreach (var kv in attr.NamedArguments)
                {
                    if (kv.Value.Value is not int dNamed)
                        continue;

                    if (IsDepthKey(kv.Key))
                        depth = dNamed;
                }

                if (depth < 0)
                    depth = 0;
                if (depth > 5)
                    depth = 5;

                return new NamespaceRule(root!, depth);
            }

            return null;
        }

        private static bool IsDepthKey(string key) =>
            string.Equals(key, "folderTraversalDepth", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "FolderTraversalDepth", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "Depth", StringComparison.OrdinalIgnoreCase);
    }
}
