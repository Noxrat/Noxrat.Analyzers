# Noxrat.Analyzers

A Roslyn analyzer package providing compile-time diagnostics for namespace conventions and attribute-based type constraints.

---

## Analyzers

### `Noxrat0000` — Namespace Rule

> **Coding impact:** Style / convention enforcement.

Enforces that every top-level type's namespace matches a deterministic rule derived from the project's folder structure.

**Attribute:** `RootNamespaceAttribute`

- **Target:** `[assembly:]`
- **Parameters:**
  - `rootNamespace` (string, required) — The expected root namespace for the assembly.
  - `folderTraversalDepth` (int, optional, default `0`) — How many levels of subdirectories are appended to the root namespace. Clamped to `0–5`.

**Usage:**

```csharp
[assembly: RootNamespace("MyCompany.MyProject", folderTraversalDepth = 2)]
```

**Behavior:**

- When `folderTraversalDepth = 0`, all types must live in exactly `MyCompany.MyProject`.
- When `folderTraversalDepth = 2`, a file at `Features/Auth/LoginService.cs` must use namespace `MyCompany.MyProject.Features.Auth`.
- Folder names are sanitized into valid C# identifiers (invalid chars become `_`).
- Partial types are validated per-declaration (each file checked independently).
- Types in the global namespace are skipped (CA1050 covers that).

**Diagnostic:**

| ID | Severity | Message |
|----|----------|---------|
| `Noxrat0000` | Warning | File `{0}` does not match the expected namespace: `{1}` |

---

### `Noxrat0001` — Requires Attribute Rule

> **Coding impact:** Compile-time contract / constraint enforcement.

Enforces that types passed as arguments or type parameters carry specific attributes, checked at call sites.

**Attribute:** `RequiresAttributeAttribute`

- **Target:** Parameters and generic type parameters (`AttributeTargets.Parameter | AttributeTargets.GenericParameter`)
- **AllowMultiple:** `true` — multiple instances on the same symbol act as **AND** (all must be satisfied).
- **Parameters:**
  - `anyOf` (params `Type[]`, required) — One or more attribute types. The constraint is satisfied if the type has **any one** of them (**OR** semantics within a single attribute).

**Usage:**

```csharp
// Generic type parameter constraint
void Serialize<[RequiresAttribute(typeof(SerializableAttribute))] T>(T value) { }

// Method parameter constraint
void Process([RequiresAttribute(typeof(MarkerA), typeof(MarkerB))] object input) { }

// Multiple attributes = AND
void Strict<[RequiresAttribute(typeof(FooAttr))] [RequiresAttribute(typeof(BarAttr))] T>() { }
```

**Behavior:**

- Checked at **call sites** (invocations and `new` expressions), not at declaration.
- Inspects the compile-time type of each argument / type argument.
- Attribute lookup walks the type's **inheritance chain** (base types included).
- Attribute matching supports **derived attribute types** (a subclass of the required attribute satisfies the check).
- Arrays (`T[]`) and `Nullable<T>` are unwrapped to their element type before checking.
- **OR** within one `[RequiresAttribute]`: type needs at least one of the listed attributes.
- **AND** across multiple `[RequiresAttribute]` on the same symbol: all must be independently satisfied.

**Diagnostic:**

| ID | Severity | Message |
|----|----------|---------|
| `Noxrat0001` | Warning | Type `{0}` does not contain required attributes: `{1}` |

---

## Quick Reference

| Attribute | Applies To | Analyzer | ID |
|-----------|-----------|----------|----|
| `RootNamespaceAttribute` | `[assembly:]` | Namespace Rule | `Noxrat0000` |
| `RequiresAttributeAttribute` | Parameters, generic type parameters | Requires Attribute Rule | `Noxrat0001` |

## Installation

Reference the `noxrat.analyzers` package through nuget, substitute for latest version:

```xml
<PackageReference Include="Noxrat.Analyzers" Version="1.0.0" />
```
