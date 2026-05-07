namespace Metano.Compiler.Mappings;

/// <summary>
/// One declarative mapping entry extracted from <c>[MapMethod]</c> or
/// <c>[MapProperty]</c>. <see cref="JsName"/> represents a simple rename;
/// <see cref="JsTemplate"/> represents template-based lowering. The two
/// are mutually exclusive — when both are set on the attribute the
/// frontend raises <see cref="Diagnostics.DiagnosticCodes.ConflictingAttributes"/>
/// (MS0004) and keeps <see cref="JsTemplate"/>, so every constructed entry
/// has at most one of the two populated.
///
/// <see cref="WhenArg0StringEquals"/> is an optional literal-argument filter:
/// when set, the entry only matches a call site whose first argument is a
/// target-language string literal with that exact value (e.g.,
/// <c>Guid.ToString("N")</c>).
///
/// <see cref="WrapReceiver"/> is an optional source-receiver wrapping spec:
/// when set, the target rewrites the call site's receiver to
/// <c>WrapReceiver(source)</c> before applying the rename/template, unless
/// the receiver is already a chained call from the same wrapper (LINQ-style).
///
/// <see cref="RuntimeImports"/> is an optional comma-separated list of
/// runtime-helper identifiers the lowered call site must import from the
/// target's runtime package. The target forwards the list to its import
/// collector since the template body is otherwise opaque text.
/// </summary>
public sealed record DeclarativeMappingEntry(
    string? JsName,
    string? JsTemplate,
    string? WhenArg0StringEquals = null,
    string? WrapReceiver = null,
    string? RuntimeImports = null,
    string? DartName = null,
    string? DartTemplate = null,
    string? DartRuntimeImports = null,
    int? WhenArgCount = null
)
{
    public bool HasTemplate => JsTemplate is not null;

    public bool HasDartTemplate => DartTemplate is not null;

    /// <summary>True when at least one TypeScript-target field is populated.
    /// The TS backend filters with this so a Dart-only entry never reaches
    /// <c>IrToTsBclMapper</c>'s <c>JsName!</c> / <c>JsTemplate!</c> dereferences.</summary>
    public bool HasJsMapping => JsName is not null || JsTemplate is not null;

    /// <summary>True when at least one Dart-target field is populated.</summary>
    public bool HasDartMapping => DartName is not null || DartTemplate is not null;

    public bool HasArgFilter => WhenArg0StringEquals is not null;

    public bool HasArgCountFilter => WhenArgCount is not null;

    public bool HasWrapReceiver => WrapReceiver is not null;

    // Parsed once per entry and reused across every call-site that expands
    // it — template-based mappings are hit many times per compilation.
    private IReadOnlyList<string>? _runtimeImportsList;
    private IReadOnlyList<string>? _dartRuntimeImportsList;

    /// <summary>
    /// Parsed runtime imports (comma-separated identifier list in
    /// <see cref="RuntimeImports"/>). Empty when the raw value is null or
    /// whitespace.
    /// </summary>
    public IReadOnlyList<string> RuntimeImportsList =>
        _runtimeImportsList ??= ParseRuntimeImports(RuntimeImports);

    /// <summary>Dart-target counterpart of <see cref="RuntimeImportsList"/>.</summary>
    public IReadOnlyList<string> DartRuntimeImportsList =>
        _dartRuntimeImportsList ??= ParseRuntimeImports(DartRuntimeImports);

    private static IReadOnlyList<string> ParseRuntimeImports(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];
        return raw!.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }
}
