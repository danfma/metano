namespace Metano.Dart.AST;

/// <summary>Base record for Dart type references.</summary>
public abstract record DartType;

/// <summary>A named Dart type: <c>int</c>, <c>String</c>, <c>List&lt;User&gt;</c>, etc.</summary>
/// <param name="Name">Type name (built-in or user-defined).</param>
/// <param name="TypeArguments">Generic type arguments.</param>
/// <param name="Origin">Cross-package origin for imports; null when the type is declared
/// in the current file or is a Dart built-in.</param>
public sealed record DartNamedType(
    string Name,
    IReadOnlyList<DartType>? TypeArguments = null,
    DartTypeOrigin? Origin = null
) : DartType;

/// <summary>A nullable Dart type rendered as <c>T?</c>.</summary>
public sealed record DartNullableType(DartType Inner) : DartType;

/// <summary>A Dart function type: <c>ReturnType Function(Type1 arg1, Type2 arg2)</c>.</summary>
public sealed record DartFunctionType(IReadOnlyList<DartParameter> Parameters, DartType ReturnType)
    : DartType;

/// <summary>A Dart record (tuple) type: <c>(T1, T2)</c>.</summary>
public sealed record DartRecordType(IReadOnlyList<DartType> Elements) : DartType;

/// <summary>The identity of a type that comes from another Dart package.
/// The Dart printer renders it as <c>import 'package:name/path.dart';</c>.</summary>
public sealed record DartTypeOrigin(string Package, string Path);
