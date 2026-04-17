using Metano.Dart.AST;

namespace Metano.Dart;

/// <summary>
/// Renders a <see cref="DartType"/> as the Dart source fragment that would appear in
/// a type annotation. Shared between the <see cref="Printer"/> (class headers, field
/// declarations) and <see cref="IrBodyPrinter"/> (cast targets, variable declarations
/// inside bodies).
/// </summary>
public static class DartTypeFormatter
{
    public static string Format(DartType type) =>
        type switch
        {
            DartNamedType { Name: var n, TypeArguments: var a } => a is { Count: > 0 }
                ? $"{n}<{string.Join(", ", a.Select(Format))}>"
                : n,
            DartNullableType { Inner: var inner } => $"{Format(inner)}?",
            DartFunctionType { Parameters: var ps, ReturnType: var r } =>
                $"{Format(r)} Function({string.Join(", ", ps.Select(p => Format(p.Type)))})",
            DartRecordType { Elements: var els } => $"({string.Join(", ", els.Select(Format))})",
            _ => "dynamic",
        };
}
