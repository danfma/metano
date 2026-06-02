using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers an <see cref="IrClassDeclaration"/> marked with <c>[JsTuple]</c>
/// — the array-shape sibling of <c>[PlainObject]</c> — into a TypeScript
/// tuple type alias <c>export type T&lt;…&gt; = [T0, T1, …]</c>. The element
/// list follows the positional declaration order of the record's primary
/// constructor. No class, no <c>equals</c>/<c>hashCode</c>/<c>with</c>, no
/// constructor — the type is purely a shape.
/// <para>
/// A <c>[JsTuple]</c> type that also carries <c>[Import]</c> is erased: it
/// emits nothing, and every reference resolves to the imported library tuple
/// (e.g. <c>Signal&lt;T&gt;</c> from <c>solid-js</c>). That erasure is handled
/// entirely by the <c>[Import]</c> short-circuit in
/// <see cref="Transformation.TypeTransformer.BuildTypeStatements"/>, which
/// returns before this bridge is reached — so this bridge only ever sees the
/// standalone <c>[JsTuple]</c> case and unconditionally emits the alias.
/// </para>
/// </summary>
public static class IrToTsJsTupleBridge
{
    public static bool Convert(IrClassDeclaration ir, List<TsTopLevel> sink)
    {
        if (!ir.Semantics.IsJsTuple)
            return false;

        var tsName = IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
        var elements = new List<TsType>();
        if (ir.Constructor is not null)
        {
            foreach (var ctorParam in ir.Constructor.Parameters)
                elements.Add(IrToTsTypeMapper.Map(ctorParam.Parameter.Type));
        }

        sink.Add(
            new TsTypeAlias(
                tsName,
                new TsTupleType(elements),
                Exported: true,
                TypeParameters: IrToTsClassBridge.BuildTypeParameters(ir)
            )
        );
        return true;
    }
}
