using Metano.Compiler.IR;
using Metano.Compiler.Dart.AST;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Lowers an <see cref="IrDelegateDeclaration"/> into a Dart <c>typedef</c>
/// that aliases the delegate's function signature. Dart's <c>typedef</c> is the
/// idiomatic equivalent of a C# delegate type — callers reference the alias
/// the same way they would a function type literal.
/// <para>
/// A <c>[This]</c>-attributed first parameter on the C# side gets re-introduced
/// as a regular positional parameter at index 0. Dart has no JS-style <c>this</c>
/// rebinding, so the receiver flows through as plain data — matching the
/// behavior already documented in the existing widget delegate test.
/// </para>
/// </summary>
public static class IrToDartDelegateBridge
{
    /// <summary>
    /// Neutral identifier the bridge synthesizes for the <c>[This]</c>-degraded
    /// receiver. Today the Dart type formatter strips parameter names from
    /// function types (typedefs render as <c>void Function(int)</c>, not
    /// <c>void Function(int x)</c>), so this constant is currently invisible at
    /// the call site. It still documents the chosen name for the day the
    /// formatter starts emitting parameter names — picked because <c>this</c>
    /// is reserved and the original C# parameter name is not preserved through
    /// IR extraction.
    /// </summary>
    private const string ReceiverParameterName = "self";

    public static void Convert(IrDelegateDeclaration ir, List<DartTopLevel> statements)
    {
        var name = IrToDartNamingPolicy.ToTypeName(ir.Name, ir.Attributes);

        var parameters = new List<DartParameter>();
        if (ir.ThisType is not null)
            parameters.Add(
                new DartParameter(ReceiverParameterName, IrToDartTypeMapper.Map(ir.ThisType))
            );

        foreach (var p in ir.Parameters)
            parameters.Add(
                new DartParameter(
                    IrToDartNamingPolicy.ToParameterName(p.Name),
                    IrToDartTypeMapper.Map(p.Type)
                )
            );

        var signature = new DartFunctionType(parameters, IrToDartTypeMapper.Map(ir.ReturnType));
        var typeParameters = IrToDartTypeParameterMapper.Map(ir.TypeParameters);

        statements.Add(new DartTypedef(name, signature, typeParameters));
    }
}
