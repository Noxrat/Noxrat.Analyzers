using Noxrat.Analyzers;

namespace Noxrat.Sandbox.Tests;

[BakeStringConstant(
    "{field,separator:','}",
    listOfFields: [KEY1, KEY2, KEY3, CodeGenStringConstantOther.KEY1]
)]
public static partial class CodeGenStringConstant
{
    public const string KEY1 = "spawn_point";
    public const string KEY2 = "body_strength";
    public const string KEY3 = "body_count";

    // expecting:
    // public const BAKED_STRING = "spawn_point,body_strength,body_count,some_other_key";
}
