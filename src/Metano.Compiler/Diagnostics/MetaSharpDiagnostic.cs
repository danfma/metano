using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Diagnostics;

/// <summary>
/// Severity of a transpiler diagnostic.
/// </summary>
public enum MetanoDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A transpiler diagnostic — surfaces issues like unsupported language features,
/// unresolved types, or ambiguous constructs at build time, with the original C#
/// source location preserved when available.
/// </summary>
public sealed record MetanoDiagnostic(
    MetanoDiagnosticSeverity Severity,
    string Code,
    string Message,
    Location? Location = null
)
{
    /// <summary>
    /// Formats the diagnostic in Roslyn-compatible style:
    /// path/to/file.cs(line,col): warning MS0001: message
    /// </summary>
    public string Format()
    {
        var severity = Severity switch
        {
            MetanoDiagnosticSeverity.Error => "error",
            MetanoDiagnosticSeverity.Warning => "warning",
            _ => "info",
        };

        if (Location is null)
            return $"{severity} {Code}: {Message}";

        var pos = Location.GetLineSpan();
        var path = pos.Path;
        var line = pos.StartLinePosition.Line + 1;
        var col = pos.StartLinePosition.Character + 1;
        return $"{path}({line},{col}): {severity} {Code}: {Message}";
    }
}

/// <summary>
/// Catalog of diagnostic codes used by the Metano transpiler.
/// </summary>
public static class DiagnosticCodes
{
    /// <summary>MS0001 — A C# language feature is not supported by the transpiler.</summary>
    public const string UnsupportedFeature = "MS0001";

    /// <summary>MS0002 — A referenced type could not be resolved or is not transpilable.</summary>
    public const string UnresolvedType = "MS0002";

    /// <summary>MS0003 — An ambiguous construct that may produce incorrect output.</summary>
    public const string AmbiguousConstruct = "MS0003";

    /// <summary>MS0004 — Conflicting attributes on a single symbol.</summary>
    public const string ConflictingAttributes = "MS0004";

    /// <summary>MS0005 — A cyclic reference exists between generated TypeScript files.</summary>
    public const string CyclicReference = "MS0005";

    /// <summary>MS0006 — Invalid use of [ModuleEntryPoint] (multiple, non-void/Task return,
    /// or has parameters).</summary>
    public const string InvalidModuleEntryPoint = "MS0006";

    /// <summary>MS0007 — Cross-package resolution failure: the name in <c>package.json</c>
    /// diverges from the assembly's <c>[EmitPackage]</c>, OR a consumer references a type
    /// from an assembly that does not declare <c>[EmitPackage]</c> for the active target.</summary>
    public const string CrossPackageResolution = "MS0007";

    /// <summary>MS0008 — Conflicting <c>[EmitInFile]</c> grouping: types sharing the same
    /// file name belong to different namespaces, so the file would have an ambiguous
    /// folder placement.</summary>
    public const string EmitInFileConflict = "MS0008";

    /// <summary>MS0009 — Source frontend failed to load or compile the project (project
    /// file missing, MSBuild workspace failure, null compilation, or language-level
    /// errors reported by Roslyn).</summary>
    public const string FrontendLoadFailure = "MS0009";

    /// <summary>MS0010 — <c>[Optional]</c> (from
    /// <c>Metano.Annotations.TypeScript</c>) was applied to a
    /// non-nullable parameter or property. The attribute relies on JS
    /// <c>undefined</c> collapsing to C# <c>null</c>; a non-nullable
    /// target cannot represent the absent case without undefined
    /// behavior. The fix is to make the C# type nullable
    /// (<c>[Optional] string? Name</c>).</summary>
    public const string OptionalRequiresNullable = "MS0010";

    /// <summary>MS0011 — <c>[Discriminator("FieldName")]</c> (from
    /// <c>Metano.Annotations.TypeScript</c>) refers to a field that
    /// either doesn't exist on the annotated type, isn't a
    /// <c>[StringEnum]</c>, or is nullable. The short-circuit guard
    /// emission relies on the field being a present, non-null
    /// StringEnum so the discriminant check can narrow the rest of
    /// the shape.</summary>
    public const string InvalidDiscriminator = "MS0011";

    /// <summary>MS0012 — <c>[External]</c> (from
    /// <c>Metano.Annotations.TypeScript</c>) was applied to a
    /// non-static class, or combined with <c>[Transpile]</c>. The
    /// attribute marks a stub for runtime globals — the target class
    /// emits no file. Non-static types and combinations with
    /// <c>[Transpile]</c> force the transpiler to simultaneously honor
    /// "no emission" and "full emission", which are incompatible.</summary>
    public const string InvalidExternal = "MS0012";

    /// <summary>MS0014 — <c>[Constant]</c> (from
    /// <c>Metano.Annotations</c>) was applied to a parameter whose
    /// call-site argument is not a compile-time constant, or to a
    /// field that cannot carry the attribute. Parameters accept
    /// Roslyn <c>ConstantValue</c> expressions (literal,
    /// <c>const</c> field/local) and references to another
    /// <c>[Constant]</c>-decorated field whose own initializer has
    /// been validated. Fields must be <c>const</c> or
    /// <c>readonly</c> with a literal-reducible initializer; mutable
    /// fields are rejected so downstream lowering (<c>[Emit]</c>
    /// templates, <c>[Inline]</c> expansion) can substitute the
    /// literal form without a separate analyzer pass.</summary>
    public const string InvalidConstant = "MS0014";

    /// <summary>MS0018 — <c>[This]</c> (from
    /// <c>Metano.Annotations</c>) was applied to a parameter that
    /// cannot act as the JavaScript <c>this</c> receiver. Valid
    /// placement is strictly the first positional parameter of a
    /// delegate or inlinable method, without <c>ref</c> /
    /// <c>out</c> / <c>params</c> modifiers. The diagnostic surfaces
    /// misuses such as applying the attribute to a later parameter,
    /// to a <c>ref</c> / <c>out</c> / <c>params</c> slot, or to a
    /// parameter list whose emitted shape would leave the function
    /// without a receiver to bind to.</summary>
    public const string InvalidThis = "MS0018";

    /// <summary>MS0017 — Stripping the <c>I</c> prefix from an
    /// interface name would collide with another top-level type in
    /// the same namespace. Emitted only when
    /// <c>--strip-interface-prefix</c> / <c>MetanoStripInterfacePrefix</c>
    /// is enabled. The generator falls back to keeping the original
    /// prefixed name so the consumer surface stays compilable;
    /// authors wanting the strip must rename the conflicting type or
    /// override one side with <c>[Name(TypeScript, "…")]</c>.</summary>
    public const string InterfacePrefixCollision = "MS0017";

    /// <summary>MS0016 — <c>[Inline]</c> (from
    /// <c>Metano.Annotations</c>) was applied to an unsupported
    /// shape or its expansion would cycle. Valid targets are
    /// <c>static readonly</c> fields with an initializer and
    /// <c>static</c> properties with an expression-bodied getter;
    /// instance members, mutable fields, methods, and properties
    /// with block-bodied accessors raise the code with a
    /// shape-specific message. Cycles between <c>[Inline]</c>
    /// members (detected by the frontend validator via DFS over
    /// each initializer) raise the code with a cycle message so
    /// downstream substitution stays bounded.</summary>
    public const string InvalidInline = "MS0016";

    /// <summary>MS0015 — <c>[Erasable]</c> (from
    /// <c>Metano.Annotations</c>) was applied to a non-static class,
    /// or combined with <c>[Transpile]</c>. The attribute marks a
    /// static class whose scope vanishes at the call site — the
    /// class emits no file and member access flattens to a bare
    /// identifier. Non-static targets have no static surface to
    /// flatten, and <c>[Transpile]</c> asks for full emission which
    /// is incompatible with the no-file contract. Member-level
    /// emission contracts inside an <c>[Erasable]</c> class (plain
    /// bodies projected as top-level exports, <c>[Inline]</c>
    /// expansion) are enforced in a follow-up slice; this code does
    /// not yet surface them.</summary>
    public const string InvalidErasable = "MS0015";
}
