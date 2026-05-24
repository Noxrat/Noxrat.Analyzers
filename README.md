# Noxrat.Analyzers

Roslyn analyzers + generator for namespace conventions, attribute constraints, and compile-time baked string constants.

## Namespace Rule Configuration (.editorconfig)

EditorConfig-only schema for namespace enforcement.

### Keys

- `noxrat_namespace_root` (required): root namespace for matched files.
- `noxrat_namespace_scope_dir` (optional, default `.`): project-relative scope anchor.
- `noxrat_namespace_folder_traversal_depth` (optional, default `0`, clamped `0..5`): number of directory segments appended from file path relative to `scope_dir`.

### Computation

- Resolve `scope_dir` against `ProjectDir`.
- If file not under `scope_dir`, rule is inactive for that file.
- Compute file-directory relative path from `scope_dir`.
- Take first `depth` segments.
- Expected namespace = `root` + `.` + taken segments (if any segments).

### Section precedence

- Standard `.editorconfig` precedence applies (more specific/later match wins).

### Configuration example

```editorconfig
root = true

[*.cs]
noxrat_namespace_root = Project1
noxrat_namespace_scope_dir = .
noxrat_namespace_folder_traversal_depth = 0

[addons/SomeAddon/**/*.cs]
noxrat_namespace_root = SomeAddon
noxrat_namespace_scope_dir = addons/SomeAddon
noxrat_namespace_folder_traversal_depth = 2

[Scripts/**/*.cs]
noxrat_namespace_root = Project1.Core
noxrat_namespace_scope_dir = Scripts
noxrat_namespace_folder_traversal_depth = 2
```

### Path mapping example

| File Path | Matching Section | Expected Namespace |
|-----------|------------------|--------------------|
| `addons/SomeAddon/File.cs` | `[addons/SomeAddon/**/*.cs]` | `SomeAddon` |
| `addons/SomeAddon/Utils/TestUtil.cs` | `[addons/SomeAddon/**/*.cs]` | `SomeAddon.Utils` |
| `Scripts/DTOs/ServiceDTO.cs` | `[Scripts/**/*.cs]` | `Project1.Core.DTOs` |
| `Program.cs` | `[*.cs]` | `Project1` |

---

## Attributes

### Enforcing Type Attribute Contracts [RequiresAttributeAttribute]

> **Coding impact:** Compile-time contract / constraint enforcement.

**Attribute:** `RequiresAttributeAttribute`

- **Target:** Parameters + generic type parameters.
- **AllowMultiple:** `true`
- **Parameters:**
  - `anyOf` (params `Type[]`, required) -> target type must have any one listed attribute.

**Usage:**

```csharp
void Serialize<[RequiresAttribute(typeof(SerializableAttribute))] T>(T value) { }
void Process([RequiresAttribute(typeof(MarkerA), typeof(MarkerB))] object input) { }
```

**Behavior:**

- Enforced at call sites (`invocation`, `new`), not declaration.
- OR semantics inside one attribute instance.
- AND semantics across multiple attribute instances.
- Checks inheritance chain and derived attribute matches.
- Unwraps arrays and nullable before checking.

**Diagnostic:**

| ID | Severity | Message |
|----|----------|---------|
| `Noxrat0001` | Warning | Type `{0}` does not contain required attributes: `{1}` |

---

### Baking Compile-Time String Constants [BakeStringConstantAttribute]

> **Coding impact:** Compile-time source generation + validation.

**Attribute:** `BakeStringConstantAttribute`

- **Target:** Classes
- **AllowMultiple:** `true`
- **Parameters:**
  - `format` (required)
  - `listOfFields` (required)
  - optional `variableName` overload

**Usage:**

```csharp
[BakeStringConstant("{field,separator:', '}", listOfFields: [KEY1, KEY2, KEY3])]
[BakeStringConstant("PACK", "[{field,separator:', '}] has {field_count}", listOfFields: [KEY1, KEY2])]
public static partial class Keys
{
    public const string KEY1 = "spawn_point";
    public const string KEY2 = "body_strength";
    public const string KEY3 = "body_count";
}
```

**Behavior:**

- No field scanning. Only symbols from `listOfFields` are used.
- Supports cross-type constants (`OtherType.KEY`).
- One generated `public const string <VariableName>` per valid attribute instance.
- Generated constant field is annotated with `GeneratedCodeAttribute`.
- Invalid instance reports diagnostics and skips only that instance.
- Class must be `partial`.
- Nested class unsupported.
- Name collisions reported.
- Uses **Noxrat Format Schema** tokens: `field`, `field_count`.

**Diagnostic:**

| ID Range | Severity |
|----------|----------|
| `Noxrat0002` - `Noxrat0010` | Error |

---

## Noxrat Format Schema

Standalone template schema for codegen formatting.

### Supported specifiers

- `{field}`
- `{field_count}`
- `{field,separator:'...'}`
- `{field,separator:'...',prefix:'...',suffix:'...'}`

### Supported `field` options

- `separator`
- `prefix`
- `suffix`

No other options accepted.

### Escape rules

- In literal text: `\{`, `\}`, `\\`
- In option strings (`'...'`): `\\`, `\'`, `\"`, `\{`, `\}`, `\n`, `\r`, `\t`

### Semantics

- `{field}` joins resolved field values.
- Default separator is `", "`.
- `prefix` and `suffix` apply **per item**.
- `{field_count}` outputs number of resolved field values.
- Format must contain at least one token (`field` or `field_count`).

### Examples

1. `field1, field2, field3`

```csharp
"{field,separator:', '}"
```

2. `[field1, field2, field3]`

```csharp
"[{field,separator:', '}]"
```

3. `{"expected":["field1", "field2", "field3"]}`

```csharp
"\\{\"expected\":[{field,separator:', ',prefix:\\\",suffix:\\\"}]\\}"
```

4. `[field1, field2, field3] has 3`

```csharp
"[{field,separator:', '}] has {field_count}"
```

---

## Diagnostic Reports

### `Noxrat0000` Namespace mismatch

**Example**

> `.editorconfig`
> `[Scripts/**/*.cs]`
> `noxrat_namespace_root = Project1.Core`
> `noxrat_namespace_scope_dir = Scripts`
> `noxrat_namespace_folder_traversal_depth = 1`
> `Scripts/DTOs/ServiceDTO.cs`

```csharp
namespace Project1.Core.DTOs.More;

public class SomeClass { } // will report: expected Project1.Core.DTOs
```

**Fix**

```csharp
namespace Project1.Core.DTOs;

public class SomeClass { } // acceptable
```

### `Noxrat0001` Required attribute missing

**Example**

```csharp
[MyMarker]
public class Marked {}

public class Unmarked {}

public static class Api
{
    public static void Run<[RequiresAttribute(typeof(MyMarkerAttribute))] T>() {}

    public static void Test()
    {
        Run<Unmarked>(); // will report: Unmarked missing MyMarkerAttribute
    }
}
```

**Fix**

```csharp
[MyMarker]
public class Marked {}

public static class Api
{
    public static void Run<[RequiresAttribute(typeof(MyMarkerAttribute))] T>() {}

    public static void Test()
    {
        Run<Marked>(); // acceptable
    }
}
```

### `Noxrat0002` BakeStringConstant field is not const

**Example**

```csharp
[BakeStringConstant("{field}", listOfFields: [KEY1])]
public static partial class Keys
{
    public static readonly string KEY1 = "v"; // will report: field must be const
}
```

**Fix**

```csharp
[BakeStringConstant("{field}", listOfFields: [KEY1])]
public static partial class Keys
{
    public const string KEY1 = "v"; // acceptable
}
```

### `Noxrat0003` BakeStringConstant field has invalid type

**Example**

```csharp
[BakeStringConstant("{field}", listOfFields: [KEY1])]
public static partial class Keys
{
    public const int KEY1 = 7; // will report: field must be string or char
}
```

**Fix**

```csharp
[BakeStringConstant("{field}", listOfFields: [KEY1])]
public static partial class Keys
{
    public const string KEY1 = "7"; // acceptable
}
```

### `Noxrat0004` Class must be partial

**Example**

```csharp
[BakeStringConstant("{field}", listOfFields: [KEY1])]
public static class Keys // will report: class must be partial
{
    public const string KEY1 = "v";
}
```

**Fix**

```csharp
[BakeStringConstant("{field}", listOfFields: [KEY1])]
public static partial class Keys // acceptable
{
    public const string KEY1 = "v";
}
```

### `Noxrat0005` Field list empty

**Example**

```csharp
[BakeStringConstant("{field}", listOfFields: [])] // will report: empty field list
public static partial class Keys {}
```

**Fix**

```csharp
[BakeStringConstant("{field}", listOfFields: [KEY1])]
public static partial class Keys
{
    public const string KEY1 = "v"; // acceptable
}
```

### `Noxrat0006` Invalid variable name

**Example**

```csharp
[BakeStringConstant("123_BAD", "{field}", listOfFields: [KEY1])] // will report: invalid identifier
public static partial class Keys
{
    public const string KEY1 = "v";
}
```

**Fix**

```csharp
[BakeStringConstant("GOOD_NAME", "{field}", listOfFields: [KEY1])]
public static partial class Keys
{
    public const string KEY1 = "v"; // acceptable
}
```

### `Noxrat0007` Variable name collision

**Example**

```csharp
[BakeStringConstant("PACK", "{field}", listOfFields: [KEY1])]
public static partial class Keys
{
    public const string PACK = "already"; // will collide with generated PACK
    public const string KEY1 = "v";
}
```

**Fix**

```csharp
[BakeStringConstant("PACK_KEYS", "{field}", listOfFields: [KEY1])]
public static partial class Keys
{
    public const string PACK = "already";
    public const string KEY1 = "v"; // acceptable
}
```

### `Noxrat0008` Nested class unsupported

**Example**

```csharp
public static class Outer
{
    [BakeStringConstant("{field}", listOfFields: [KEY1])] // will report: nested class unsupported
    public static partial class Inner
    {
        public const string KEY1 = "v";
    }
}
```

**Fix**

```csharp
[BakeStringConstant("{field}", listOfFields: [KEY1])]
public static partial class Keys
{
    public const string KEY1 = "v"; // acceptable
}
```

### `Noxrat0009` Field reference invalid

**Example**

```csharp
[BakeStringConstant("{field}", listOfFields: [nameof(KEY1)])] // will report: expression not a field symbol
public static partial class Keys
{
    public const string KEY1 = "v";
}
```

**Fix**

```csharp
[BakeStringConstant("{field}", listOfFields: [KEY1])]
public static partial class Keys
{
    public const string KEY1 = "v"; // acceptable
}
```

### `Noxrat0010` Format schema invalid

**Example**

```csharp
[BakeStringConstant("{field_json}", listOfFields: [KEY1])] // will report: unsupported token
public static partial class Keys
{
    public const string KEY1 = "v";
}
```

**Fix**

```csharp
[BakeStringConstant("{field,separator:', ',prefix:'\"',suffix:'\"'}", listOfFields: [KEY1])]
public static partial class Keys
{
    public const string KEY1 = "v"; // acceptable
}
```

## Installation

```xml
<PackageReference Include="Noxrat.Analyzers" Version="1.0.0" />
```
