namespace Metano.Dart.AST;

/// <summary>Base for top-level declarations in a Dart file.</summary>
public abstract record DartTopLevel;

/// <summary>An <c>import 'package:foo/bar.dart';</c> or <c>import 'relative.dart';</c>.</summary>
/// <param name="Path">Full import path (already formatted as the Dart string literal body).</param>
/// <param name="Alias">Optional <c>as alias</c>.</param>
/// <param name="ShowNames">Restricts the import to specific names via <c>show</c>.</param>
public sealed record DartImport(
    string Path,
    string? Alias = null,
    IReadOnlyList<string>? ShowNames = null
) : DartTopLevel;

/// <summary>A Dart <c>class</c> declaration.</summary>
/// <param name="Name">Class name in PascalCase.</param>
/// <param name="Modifier">Optional class modifier (<c>final</c>, <c>base</c>, <c>sealed</c>,
/// <c>abstract</c>, <c>abstract interface</c>).</param>
/// <param name="TypeParameters">Generic type parameters.</param>
/// <param name="ExtendsType">Base class to extend, if any.</param>
/// <param name="Implements">Interfaces this class implements.</param>
/// <param name="Constructor">Primary constructor declaration (optional).</param>
/// <param name="Members">Fields, getters, setters, methods.</param>
public sealed record DartClass(
    string Name,
    DartClassModifier Modifier = DartClassModifier.None,
    IReadOnlyList<DartTypeParameter>? TypeParameters = null,
    DartType? ExtendsType = null,
    IReadOnlyList<DartType>? Implements = null,
    DartConstructor? Constructor = null,
    IReadOnlyList<DartClassMember>? Members = null
) : DartTopLevel;

/// <summary>Class-level modifier keyword combinations supported by the printer.</summary>
public enum DartClassModifier
{
    None,
    Abstract,
    AbstractInterface,
    Final,
    Sealed,
    Base,
}

/// <summary>A Dart <c>enum</c> declaration (plain or enhanced).</summary>
public sealed record DartEnum(string Name, IReadOnlyList<DartEnumValue> Values) : DartTopLevel;

/// <summary>
/// A top-level Dart function. Emitted for C# static classes marked with
/// <c>[ExportedAsModule]</c>, whose methods become module-level functions
/// rather than members of a class.
/// </summary>
public sealed record DartFunction(
    string Name,
    IReadOnlyList<DartParameter> Parameters,
    DartType ReturnType,
    IReadOnlyList<Metano.Compiler.IR.IrStatement>? Body = null,
    bool IsAsync = false
) : DartTopLevel;

/// <summary>A single enum value. <see cref="Value"/> is the constructor arguments string
/// for enhanced enums (e.g., <c>"low"</c>) or null for plain enums.</summary>
public sealed record DartEnumValue(string Name, string? Value = null);

/// <summary>A Dart generic type parameter: <c>T</c> or <c>T extends Comparable</c>.</summary>
public sealed record DartTypeParameter(string Name, DartType? Extends = null);

/// <summary>A primary constructor declaration. When <see cref="Body"/> is non-null the
/// printer emits the IR statements inside the ctor body; otherwise it renders just
/// the parameter list followed by <c>;</c>.</summary>
public sealed record DartConstructor(
    string ClassName,
    IReadOnlyList<DartConstructorParameter> Parameters,
    bool IsConst = false,
    IReadOnlyList<Metano.Compiler.IR.IrStatement>? Body = null
);

/// <summary>A constructor parameter that may bind a field via <c>this.x</c>.</summary>
/// <param name="Name">Parameter name.</param>
/// <param name="Type">Type, used only when <see cref="IsFieldInitializer"/> is false.</param>
/// <param name="IsFieldInitializer">When true the parameter renders as <c>this.x</c>
/// rather than <c>Type x</c>.</param>
/// <param name="IsRequired">False when the parameter has a default value — the
/// printer then renders it as a Dart optional positional parameter
/// (<c>[Type x = default]</c>).</param>
/// <param name="DefaultValue">Parsed default expression, mirrored from
/// <see cref="Metano.Compiler.IR.IrParameter.DefaultValue"/>. Null when the
/// parameter has no default, which — together with <see cref="IsRequired"/> true —
/// yields the plain required-positional form.</param>
public sealed record DartConstructorParameter(
    string Name,
    DartType? Type = null,
    bool IsFieldInitializer = false,
    bool IsRequired = true,
    Metano.Compiler.IR.IrExpression? DefaultValue = null
);

/// <summary>A complete generated Dart source file.</summary>
public sealed record DartSourceFile(string FileName, IReadOnlyList<DartTopLevel> Statements);
