using Metano.Compiler.Dart.AST;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Shared converter from <see cref="IrTypeParameter"/> to <see cref="DartTypeParameter"/>.
/// Used by class, delegate, and method bridges so each one renders generic
/// parameters and their <c>extends</c> bounds the same way.
/// </summary>
internal static class IrToDartTypeParameterMapper
{
    public static IReadOnlyList<DartTypeParameter>? Map(
        IReadOnlyList<IrTypeParameter>? typeParameters
    )
    {
        if (typeParameters is null || typeParameters.Count == 0)
            return null;
        return typeParameters
            .Select(tp =>
            {
                var extends = tp.Constraints is { Count: > 0 } c
                    ? IrToDartTypeMapper.Map(c[0])
                    : null;
                return new DartTypeParameter(tp.Name, extends);
            })
            .ToList();
    }
}
