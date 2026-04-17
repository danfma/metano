using Metano.Compiler.IR;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Stable, structural <see cref="IrTypeRef"/> signatures used for matching
/// IR nodes against Roslyn symbols when name + arity alone aren't enough
/// (e.g., two operator overloads with the same arity but different parameter
/// types). The output is purely an internal join key — never emitted as TS.
/// </summary>
internal static class IrTypeRefSignatures
{
    public static string Describe(IrTypeRef type) =>
        type switch
        {
            IrPrimitiveTypeRef p => $"prim:{p.Primitive}",
            IrNullableTypeRef n => $"null<{Describe(n.Inner)}>",
            IrArrayTypeRef a => $"arr<{Describe(a.ElementType)}>",
            IrMapTypeRef m => $"map<{Describe(m.KeyType)},{Describe(m.ValueType)}>",
            IrSetTypeRef s => $"set<{Describe(s.ElementType)}>",
            IrTupleTypeRef t => $"tuple<{string.Join(",", t.Elements.Select(Describe))}>",
            IrFunctionTypeRef f =>
                $"fn<{string.Join(",", f.Parameters.Select(p => Describe(p.Type)))}->{Describe(f.ReturnType)}>",
            IrPromiseTypeRef pr => $"promise<{Describe(pr.ResultType)}>",
            IrGeneratorTypeRef g => $"gen<{Describe(g.YieldType)}>",
            IrIterableTypeRef i => $"iter<{Describe(i.ElementType)}>",
            IrKeyValuePairTypeRef kv => $"kv<{Describe(kv.KeyType)},{Describe(kv.ValueType)}>",
            IrGroupingTypeRef gr => $"grp<{Describe(gr.KeyType)},{Describe(gr.ElementType)}>",
            IrTypeParameterRef tp => $"tp:{tp.Name}",
            IrNamedTypeRef n => n.TypeArguments is { Count: > 0 } args
                ? $"{n.Name}<{string.Join(",", args.Select(Describe))}>"
                : n.Name,
            IrUnknownTypeRef => "unknown",
            _ => "?",
        };
}
