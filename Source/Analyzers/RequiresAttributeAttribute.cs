using System;

namespace Noxrat.Analyzers;

[AttributeUsage(
    AttributeTargets.Parameter | AttributeTargets.GenericParameter,
    AllowMultiple = true,
    Inherited = false
)]
public class RequiresAttributeAttribute : Attribute
{
    public Type[] anyOf { get; }

    public RequiresAttributeAttribute(params Type[] anyOf)
    {
        this.anyOf = anyOf;
    }
}
