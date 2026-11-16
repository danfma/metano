using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The value C# gives <c>default</c> for a parameter of a user struct
/// (<c>Point p = default</c>). The opaque <see cref="IrLiteralKind.Default"/>
/// marker prints as <c>undefined</c>, which no strict target accepts for a
/// class-typed parameter, and a named call that skips the parameter passed
/// <c>null</c>. A struct the target emits with a parameterless constructor
/// has an equivalent value, <c>new T()</c>: every field starts at its zero.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <c>new T()</c> for the <c>default</c> of <paramref name="type"/>, when
    /// <paramref name="type"/> is a transpiled user struct the target
    /// constructs without arguments; <c>null</c> for any other type, which keeps
    /// the opaque default.
    /// </summary>
    internal IrExpression? TryBuildStructDefault(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol structType || !IsDefaultConstructibleStruct(structType))
            return null;

        var constructor = structType.InstanceConstructors.Single(ctor => ctor.Parameters.IsEmpty);

        return new IrNewExpression(
            IrTypeRefMapper.Map(structType, _originResolver, _target),
            [],
            Origin: BuildOrigin(constructor),
            TypeArguments: ConstructedTypeArguments(structType)
        );
    }

    /// <summary>
    /// A transpiled struct with no declared instance constructor: the target
    /// emits its implicit parameterless one, and running it equals C#'s
    /// <c>default</c> (a declared constructor, even a parameterless one, may
    /// set state <c>default</c> leaves at zero, and a primary constructor
    /// takes arguments). Wrappers, plain objects and tuples lower to other
    /// shapes.
    /// </summary>
    private bool IsDefaultConstructibleStruct(INamedTypeSymbol type) =>
        type is { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None }
        && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
        && IrTypeRefMapper.IsTranspilableType(type, _target)
        && !type.HasInlineWrapper()
        && !type.HasPlainObject()
        && !type.HasJsTuple()
        && !type.HasImport()
        && !type.HasExternal()
        && type.InstanceConstructors.All(ctor => ctor.IsImplicitlyDeclared);
}
