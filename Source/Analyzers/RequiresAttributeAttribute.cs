using System;

namespace Noxrat.Analyzers;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true, Inherited = false)]
public class RequiresAttributeAttribute : Attribute
{
    public string requiredAttribute { get; set; }

    public RequiresAttributeAttribute(string requiredAttribute)
    {
        this.requiredAttribute = requiredAttribute;
    }
}
