using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Noxrat.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequiresAttributeAnalyzer : DiagnosticAnalyzer
{
    private const string RequiresAttributeMetadataName =
        "Noxrat.Analyzers.RequiresAttributeAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EDiagnosticId.REQUIRE_ATTRIBUTE_DOESNT_CONTAIN_ATTRIBUTE.ExRule());

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(startCtx =>
        {
            var requiresAttrSymbol = startCtx.Compilation.GetTypeByMetadataName(
                RequiresAttributeMetadataName
            );
            if (requiresAttrSymbol is null)
                return;

            // Enforce at call sites (this is the correct semantics for parameter constraints).
            startCtx.RegisterOperationAction(
                ctx =>
                    AnalyzeInvocation((IInvocationOperation)ctx.Operation, ctx, requiresAttrSymbol),
                OperationKind.Invocation
            );

            startCtx.RegisterOperationAction(
                ctx =>
                    AnalyzeObjectCreation(
                        (IObjectCreationOperation)ctx.Operation,
                        ctx,
                        requiresAttrSymbol
                    ),
                OperationKind.ObjectCreation
            );
        });
    }

    // ----------------------------
    // Operations
    // ----------------------------

    private static void AnalyzeInvocation(
        IInvocationOperation op,
        OperationAnalysisContext context,
        INamedTypeSymbol requiresAttrSymbol
    )
    {
        // Generic method type arguments: SerializeType<T>()
        CheckMethodTypeArguments(op, context, requiresAttrSymbol);

        // Parameters: Foo([RequiresAttribute(...)] x)
        CheckCallArguments(op.TargetMethod, op.Arguments, context, requiresAttrSymbol);
    }

    private static void AnalyzeObjectCreation(
        IObjectCreationOperation op,
        OperationAnalysisContext context,
        INamedTypeSymbol requiresAttrSymbol
    )
    {
        // Generic type arguments: new MyGeneric<T>()
        CheckConstructedTypeArguments(op, context, requiresAttrSymbol);

        // Constructor parameters
        if (op.Constructor is not null)
            CheckCallArguments(op.Constructor, op.Arguments, context, requiresAttrSymbol);
    }

    // ----------------------------
    // Generic method type-argument enforcement
    // ----------------------------

    private static void CheckMethodTypeArguments(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        INamedTypeSymbol requiresAttrSymbol
    )
    {
        var constructed = invocation.TargetMethod;
        var definition = constructed.OriginalDefinition;

        if (definition.TypeParameters.Length == 0)
            return;

        for (int i = 0; i < definition.TypeParameters.Length; i++)
        {
            var typeParamDef = definition.TypeParameters[i];
            var requirements = GetRequirements(typeParamDef, requiresAttrSymbol);
            if (requirements.IsDefaultOrEmpty)
                continue;

            var typeArg = UnwrapType(constructed.TypeArguments[i]);
            if (typeArg is not INamedTypeSymbol namedTypeArg)
                continue;

            foreach (var req in requirements)
            {
                if (!TypeSatisfiesRequirement(namedTypeArg, req))
                {
                    var loc =
                        TryGetInvocationTypeArgumentLocation(invocation.Syntax, i)
                        ?? invocation.Syntax.GetLocation();

                    context.ReportDiagnostic(CreateDiagnostic(loc, namedTypeArg, req.AnyOf));
                }
            }
        }
    }

    // ----------------------------
    // Generic constructed type-argument enforcement
    // ----------------------------

    private static void CheckConstructedTypeArguments(
        IObjectCreationOperation creation,
        OperationAnalysisContext context,
        INamedTypeSymbol requiresAttrSymbol
    )
    {
        if (creation.Type is not INamedTypeSymbol constructedType)
            return;

        var def = constructedType.OriginalDefinition;
        if (def.TypeParameters.Length == 0)
            return;

        if (constructedType.TypeArguments.Length != def.TypeParameters.Length)
            return;

        for (int i = 0; i < def.TypeParameters.Length; i++)
        {
            var typeParamDef = def.TypeParameters[i];
            var requirements = GetRequirements(typeParamDef, requiresAttrSymbol);
            if (requirements.IsDefaultOrEmpty)
                continue;

            var typeArg = UnwrapType(constructedType.TypeArguments[i]);
            if (typeArg is not INamedTypeSymbol namedTypeArg)
                continue;

            foreach (var req in requirements)
            {
                if (!TypeSatisfiesRequirement(namedTypeArg, req))
                {
                    var loc =
                        TryGetObjectCreationTypeArgumentLocation(creation.Syntax, i)
                        ?? creation.Syntax.GetLocation();

                    context.ReportDiagnostic(CreateDiagnostic(loc, namedTypeArg, req.AnyOf));
                }
            }
        }
    }

    // ----------------------------
    // Parameter argument enforcement at call sites
    // ----------------------------

    private static void CheckCallArguments(
        IMethodSymbol target,
        ImmutableArray<IArgumentOperation> args,
        OperationAnalysisContext context,
        INamedTypeSymbol requiresAttrSymbol
    )
    {
        foreach (var arg in args)
        {
            var param = arg.Parameter;
            if (param is null)
                continue;

            var requirements = GetRequirements(param, requiresAttrSymbol);
            if (requirements.IsDefaultOrEmpty)
                continue;

            // Enforce on the compile-time type of the argument expression.
            var argType = arg.Value?.Type;
            if (argType is null)
                continue;

            var typeToCheck = UnwrapType(argType);
            if (typeToCheck is not INamedTypeSymbol namedArgType)
                continue;

            foreach (var req in requirements)
            {
                if (!TypeSatisfiesRequirement(namedArgType, req))
                {
                    context.ReportDiagnostic(
                        CreateDiagnostic(arg.Syntax.GetLocation(), namedArgType, req.AnyOf)
                    );
                }
            }
        }
    }

    // ----------------------------
    // Requirement extraction
    // ----------------------------

    private readonly struct Requirement
    {
        public Requirement(ImmutableArray<INamedTypeSymbol> anyOf, Location? location)
        {
            AnyOf = anyOf;
            Location = location;
        }

        public ImmutableArray<INamedTypeSymbol> AnyOf { get; }
        public Location? Location { get; }
    }

    private static ImmutableArray<Requirement> GetRequirements(
        ISymbol symbol,
        INamedTypeSymbol requiresAttrSymbol
    )
    {
        var builder = ImmutableArray.CreateBuilder<Requirement>();

        foreach (var attr in symbol.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, requiresAttrSymbol))
                continue;

            var anyOf = ExtractAnyOfTypes(attr);
            if (anyOf.IsDefaultOrEmpty)
                continue;

            var loc = attr.ApplicationSyntaxReference?.GetSyntax()?.GetLocation();
            builder.Add(new Requirement(anyOf, loc));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<INamedTypeSymbol> ExtractAnyOfTypes(AttributeData attr)
    {
        // Supports:
        //   [RequiresAttribute(typeof(A), typeof(B))]
        // and named form:
        //   [RequiresAttribute(anyOf: new[] { typeof(A), typeof(B) })]
        // or:
        //   [RequiresAttribute(AnyOf = new[] { typeof(A) })]   // if you rename the property later

        var list = new List<INamedTypeSymbol>();

        void AddFromTypedConstant(TypedConstant tc)
        {
            if (tc.Kind == TypedConstantKind.Array)
            {
                foreach (var v in tc.Values)
                    AddFromTypedConstant(v);
                return;
            }

            if (tc.Kind == TypedConstantKind.Type && tc.Value is ITypeSymbol ts)
            {
                // attributes should be named type symbols, but be defensive
                if (ts is INamedTypeSymbol nts && nts.TypeKind != TypeKind.Error)
                    list.Add(nts);
            }
        }

        // ctor args (params Type[])
        foreach (var ca in attr.ConstructorArguments)
            AddFromTypedConstant(ca);

        // named args (property assignment)
        foreach (var kv in attr.NamedArguments)
        {
            if (
                string.Equals(kv.Key, "anyOf", StringComparison.Ordinal)
                || string.Equals(kv.Key, "AnyOf", StringComparison.Ordinal)
            )
            {
                AddFromTypedConstant(kv.Value);
            }
        }

        return list.Distinct(SymbolEqualityComparer<INamedTypeSymbol>.Instance).ToImmutableArray();
    }

    // ----------------------------
    // Type/attribute matching
    // ----------------------------

    // OR-list: anyOf contains acceptable attribute types
    // Multiple RequiresAttributeAttribute instances on the same symbol => AND (checked by caller loop)
    private static bool TypeSatisfiesRequirement(INamedTypeSymbol type, Requirement req)
    {
        foreach (var requiredAttrType in req.AnyOf)
        {
            if (HasAttribute(type, requiredAttrType))
                return true;
        }
        return false;
    }

    private static bool HasAttribute(
        INamedTypeSymbol targetType,
        INamedTypeSymbol requiredAttributeType
    )
    {
        // Check attributes on the target type AND base types.
        // If you *only* want direct attributes, remove the base-type loop.
        for (INamedTypeSymbol? t = targetType; t is not null; t = t.BaseType)
        {
            foreach (var a in t.GetAttributes())
            {
                var cls = a.AttributeClass;
                if (cls is null)
                    continue;

                // Match exact or derived attribute types
                if (IsOrDerivesFrom(cls, requiredAttributeType))
                    return true;
            }
        }

        return false;
    }

    private static bool IsOrDerivesFrom(INamedTypeSymbol candidate, INamedTypeSymbol required)
    {
        for (INamedTypeSymbol? c = candidate; c is not null; c = c.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(c, required))
                return true;
        }
        return false;
    }

    private static ITypeSymbol UnwrapType(ITypeSymbol type)
    {
        // T[] -> T
        if (type is IArrayTypeSymbol arr)
            return UnwrapType(arr.ElementType);

        // Nullable<T> -> T
        if (
            type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && named.TypeArguments.Length == 1
        )
        {
            return UnwrapType(named.TypeArguments[0]);
        }

        return type;
    }

    // ----------------------------
    // Locations (nice-to-have)
    // ----------------------------

    private static Location? TryGetInvocationTypeArgumentLocation(
        SyntaxNode invocationSyntax,
        int index
    )
    {
        if (invocationSyntax is not InvocationExpressionSyntax inv)
            return null;

        TypeArgumentListSyntax? typeArgs = inv.Expression switch
        {
            GenericNameSyntax g => g.TypeArgumentList,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax mg } => mg.TypeArgumentList,
            MemberBindingExpressionSyntax { Name: GenericNameSyntax mbg } => mbg.TypeArgumentList,
            _ => null,
        };

        if (typeArgs is null)
            return null;

        if (index < 0 || index >= typeArgs.Arguments.Count)
            return null;

        return typeArgs.Arguments[index].GetLocation();
    }

    private static Location? TryGetObjectCreationTypeArgumentLocation(
        SyntaxNode creationSyntax,
        int index
    )
    {
        if (creationSyntax is not ObjectCreationExpressionSyntax obj)
            return null;

        if (obj.Type is not GenericNameSyntax g)
            return null;

        if (index < 0 || index >= g.TypeArgumentList.Arguments.Count)
            return null;

        return g.TypeArgumentList.Arguments[index].GetLocation();
    }

    // ----------------------------
    // Diagnostic
    // ----------------------------

    private static Diagnostic CreateDiagnostic(
        Location location,
        INamedTypeSymbol offendingType,
        ImmutableArray<INamedTypeSymbol> anyOf
    )
    {
        var descriptor = EDiagnosticId.REQUIRE_ATTRIBUTE_DOESNT_CONTAIN_ATTRIBUTE.ExRule();

        var typeName = offendingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var required = string.Join(
            " OR ",
            anyOf.Select(t => t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
        );

        return Diagnostic.Create(descriptor, location, typeName, required);
    }

    private sealed class SymbolEqualityComparer<TSymbol> : IEqualityComparer<TSymbol>
        where TSymbol : class, ISymbol
    {
        public static readonly SymbolEqualityComparer<TSymbol> Instance = new();

        public bool Equals(TSymbol? x, TSymbol? y) =>
            Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(x, y);

        public int GetHashCode(TSymbol obj) =>
            Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.GetHashCode(obj);
    }
}
