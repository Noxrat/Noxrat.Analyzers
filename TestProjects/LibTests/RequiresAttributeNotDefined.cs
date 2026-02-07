using Noxrat.Analyzers;

namespace Noxrat.Sandbox.Tests;

public static class RequiresAttributeNotDefined
{
    private static void TestGenericFunction<[RequiresAttribute(typeof(TypeAttribute))] T>(
        T objectToCheck
    ) { }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Noxrat.Analyzer",
        "Noxrat0001:Requires attribute on the type is not found",
        Justification = "Test Reports warning successfully"
    )]
    private static void TestingObjectWithoutAttributeCall()
    {
        SomeObject obj = new SomeObject();
        TestGenericFunction(obj);
    }

    private static void TestingObjectWithAttributeCall()
    {
        SomeOtherObject obj2 = new SomeOtherObject();
        TestGenericFunction(obj2);
    }

    private class SomeObject { }

    [TypeAttribute]
    private class SomeOtherObject { }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class TypeAttribute : Attribute { }
}
