using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The value C# gives <c>default</c> for a parameter of a user struct
/// (<c>Point p = default</c>). The opaque <see cref="IrLiteralKind.Default"/>
/// marker prints as <c>undefined</c>, which no strict target accepts for a
/// class-typed parameter, and a named call that skips the parameter passed
/// <c>null</c>. A struct the target constructs without arguments
/// (<see cref="IrNamedTypeSemantics.IsDefaultConstructible"/>) has an
/// equivalent value, <c>new T()</c>: every field starts at its zero.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <c>new T()</c> for the <c>default</c> of <paramref name="type"/>, when
    /// <paramref name="type"/> is a transpiled struct that declares no
    /// constructor; <c>null</c> for any other type, which keeps the opaque
    /// default.
    /// </summary>
    internal IrExpression? TryBuildStructDefault(ITypeSymbol type)
    {
        var typeRef = IrTypeRefMapper.Map(type, _originResolver, _target);

        if (typeRef is not IrNamedTypeRef { Semantics.IsDefaultConstructible: true })
            return null;

        var constructor = ((INamedTypeSymbol)type).InstanceConstructors.Single(ctor =>
            ctor.Parameters.IsEmpty
        );

        return new IrNewExpression(
            typeRef,
            [],
            Origin: BuildOrigin(constructor),
            TypeArguments: ConstructedTypeArguments(type)
        );
    }
}
