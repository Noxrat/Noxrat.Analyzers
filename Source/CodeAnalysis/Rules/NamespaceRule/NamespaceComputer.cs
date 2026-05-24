using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Noxrat.Analyzers;

public static class NamespaceComputer
{
    public const string ROOT_NAMESPACE_KEY = "noxrat_namespace_root";
    public const string SCOPE_DIR_KEY = "noxrat_namespace_scope_dir";
    public const string FOLDER_TRAVERSAL_DEPTH_KEY = "noxrat_namespace_folder_traversal_depth";
    public const int MIN_DEPTH = 0;
    public const int MAX_DEPTH = 5;

    public static string? TryGetProjectDir(AnalyzerConfigOptionsProvider config)
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

    public static EffectiveNamespaceRule? TryGetEffectiveNamespaceRule(
        SyntaxTree tree,
        AnalyzerConfigOptionsProvider config,
        string? normalizedProjectDir
    )
    {
        if (tree is null)
            return null;

        var filePath = tree.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var options = config.GetOptions(tree);

        if (!TryGetTrimmed(options, ROOT_NAMESPACE_KEY, out var rootNamespace))
            return null;

        rootNamespace = rootNamespace.Trim('.');
        if (string.IsNullOrWhiteSpace(rootNamespace))
            return null;

        var scopeDir = ".";
        if (TryGetTrimmed(options, SCOPE_DIR_KEY, out var scopeOption))
            scopeDir = scopeOption;

        var depth = ParseDepth(options);

        if (
            depth <= 0
            && (string.IsNullOrWhiteSpace(scopeDir) || string.Equals(scopeDir, ".", StringComparison.Ordinal))
        )
        {
            return new EffectiveNamespaceRule(rootNamespace);
        }

        var projectDir = normalizedProjectDir;
        if (projectDir is null)
            projectDir = TryInferProjectDirectory(filePath, scopeDir);

        if (projectDir is null)
            return null;

        var fullScopeDir = TryResolveScopeDirectory(projectDir, scopeDir);
        if (fullScopeDir is null)
            return null;

        var expectedNamespace = TryComputeExpectedNamespace(
            rootNamespace,
            depth,
            fullScopeDir,
            filePath
        );

        if (expectedNamespace is null)
            return null;

        return new EffectiveNamespaceRule(expectedNamespace);
    }

    public readonly struct EffectiveNamespaceRule
    {
        public readonly string expectedNamespace;

        public EffectiveNamespaceRule(string expectedNamespace)
        {
            this.expectedNamespace = expectedNamespace;
        }
    }

    private static int ParseDepth(AnalyzerConfigOptions options)
    {
        if (!options.TryGetValue(FOLDER_TRAVERSAL_DEPTH_KEY, out var raw) || raw is null)
            return 0;

        if (!int.TryParse(raw.Trim(), out var parsed))
            return 0;

        if (parsed < MIN_DEPTH)
            return MIN_DEPTH;
        if (parsed > MAX_DEPTH)
            return MAX_DEPTH;

        return parsed;
    }

    private static bool TryGetTrimmed(AnalyzerConfigOptions options, string key, out string value)
    {
        value = "";
        if (!options.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        value = raw.Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? TryResolveScopeDirectory(string projectDir, string scopeDir)
    {
        try
        {
            var cleanedScope = scopeDir.Trim();
            if (string.IsNullOrWhiteSpace(cleanedScope) || cleanedScope == ".")
                return EnsureTrailingSeparator(projectDir);

            cleanedScope = cleanedScope.Replace('\\', Path.DirectorySeparatorChar);
            cleanedScope = cleanedScope.Replace('/', Path.DirectorySeparatorChar);

            // scope_dir is project-relative by design. If absolute is passed, ignore it.
            if (Path.IsPathRooted(cleanedScope))
                return null;

            var combined = Path.Combine(projectDir, cleanedScope);
            var full = EnsureTrailingSeparator(Path.GetFullPath(combined));

            // Scope must stay under project directory.
            if (!IsDirectoryUnderBase(projectDir, full))
                return null;

            return full;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryInferProjectDirectory(string filePath, string scopeDir)
    {
        try
        {
            var fileDir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(fileDir))
                return null;

            var fullFileDir = EnsureTrailingSeparator(Path.GetFullPath(fileDir));
            var normalizedScope = scopeDir.Trim();

            if (string.IsNullOrWhiteSpace(normalizedScope) || normalizedScope == ".")
                return null;

            normalizedScope = normalizedScope.Replace('\\', Path.DirectorySeparatorChar);
            normalizedScope = normalizedScope.Replace('/', Path.DirectorySeparatorChar);
            normalizedScope = normalizedScope.Trim(Path.DirectorySeparatorChar);

            if (string.IsNullOrWhiteSpace(normalizedScope))
                return null;

            var scopeSegments = normalizedScope.Split(
                new[] { Path.DirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries
            );

            var fileSegments = fullFileDir
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });

            if (scopeSegments.Length == 0 || fileSegments.Length < scopeSegments.Length)
                return null;

            var comparer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            for (int i = 0; i <= fileSegments.Length - scopeSegments.Length; i++)
            {
                var allMatch = true;
                for (int j = 0; j < scopeSegments.Length; j++)
                {
                    if (!string.Equals(fileSegments[i + j], scopeSegments[j], comparer))
                    {
                        allMatch = false;
                        break;
                    }
                }

                if (!allMatch)
                    continue;

                var builder = new StringBuilder();
                for (int k = 0; k < i; k++)
                {
                    if (k > 0)
                        builder.Append(Path.DirectorySeparatorChar);
                    builder.Append(fileSegments[k]);
                }

                if (builder.Length == 0)
                    return EnsureTrailingSeparator(
                        Path.GetPathRoot(fullFileDir) ?? Path.DirectorySeparatorChar.ToString()
                    );

                return EnsureTrailingSeparator(builder.ToString());
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryComputeExpectedNamespace(
        string rootNamespace,
        int depth,
        string fullScopeDir,
        string filePath
    )
    {
        try
        {
            var fileDir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(fileDir))
                return null;

            var fullScopeDirNormalized = EnsureTrailingSeparator(Path.GetFullPath(fullScopeDir));
            var fullFileDir = EnsureTrailingSeparator(Path.GetFullPath(fileDir));

            // Only active for files under scope.
            if (!IsDirectoryUnderBase(fullScopeDirNormalized, fullFileDir))
                return null;

            if (depth <= 0)
                return rootNamespace;

            var relativeDir = GetRelativeDirectory(fullScopeDirNormalized, fullFileDir);
            if (string.IsNullOrWhiteSpace(relativeDir))
                return rootNamespace;

            var segments = relativeDir.Split(
                new[] { '/', '\\' },
                StringSplitOptions.RemoveEmptyEntries
            );

            var sb = new StringBuilder(rootNamespace);
            var appended = 0;

            for (int i = 0; i < segments.Length; i++)
            {
                if (appended >= depth)
                    break;

                var segment = MakeValidNamespaceSegment(segments[i]);
                if (string.IsNullOrWhiteSpace(segment))
                    continue;

                sb.Append('.').Append(segment);
                appended++;
            }

            return sb.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string GetRelativeDirectory(string baseDir, string fullFileDir)
    {
        var baseUri = new Uri(baseDir, UriKind.Absolute);
        var fileUri = new Uri(fullFileDir, UriKind.Absolute);
        var relUri = baseUri.MakeRelativeUri(fileUri);
        var rel = Uri.UnescapeDataString(relUri.ToString());
        rel = rel.Replace('/', Path.DirectorySeparatorChar);
        return rel.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsDirectoryUnderBase(string baseDir, string dir)
    {
        var comparer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return dir.StartsWith(baseDir, comparer);
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
        var chars = raw.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                chars[i] = '_';
        }
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
}
