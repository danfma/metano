using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The type parameters an IR type names, and the one rule for the class type parameters
/// a static member's TypeScript declaration declares (TS2302 forbids a static to name the
/// class's own): its emission and its call sites read the same answer.
/// </summary>
internal static class IrTypeParameterReferences
{
    /// <summary>
    /// The class type parameters a static declaration declares, in class order: those
    /// <paramref name="signature"/> names and the <paramref name="required"/> ones (hidden
    /// reified parameters, a self-typed receiver), closed over the constraints of each
    /// (a bound <c>Widget&lt;TSelf, TProps&gt;</c> brings <c>TProps</c>).
    /// </summary>
    public static List<IrTypeParameter> DeclaredClassTypeParameters(
        IReadOnlyList<IrTypeParameter> classTypeParameters,
        IEnumerable<IrTypeRef> signature,
        IEnumerable<string> required
    )
    {
        var named = new HashSet<string>(required, StringComparer.Ordinal);

        foreach (var type in signature)
            Collect(type, named);

        var changed = true;

        while (changed)
        {
            var count = named.Count;

            foreach (var parameter in classTypeParameters.Where(tp => named.Contains(tp.Name)))
            {
                foreach (var constraint in parameter.Constraints ?? [])
                    Collect(constraint, named);
            }

            changed = named.Count > count;
        }

        return [.. classTypeParameters.Where(tp => named.Contains(tp.Name))];
    }

    /// <summary>Adds every type parameter <paramref name="type"/> names to <paramref name="acc"/>.</summary>
    public static void Collect(IrTypeRef type, HashSet<string> acc)
    {
        switch (type)
        {
            case IrTypeParameterRef tp:
                acc.Add(tp.Name);
                break;
            case IrNamedTypeRef named when named.TypeArguments is { Count: > 0 } args:
                foreach (var a in args)
                    Collect(a, acc);

                break;
            case IrCollectionTypeRef collection:
                Collect(collection.LegacyForm, acc);
                break;
            case IrArrayTypeRef arr:
                Collect(arr.ElementType, acc);
                break;
            case IrNullableTypeRef nullable:
                Collect(nullable.Inner, acc);
                break;
            case IrTupleTypeRef tup:
                foreach (var e in tup.Elements)
                    Collect(e, acc);

                break;
            case IIrCompositeTypeRef composite:
                foreach (var component in composite.ComponentTypes)
                    Collect(component, acc);

                break;
            case IrFunctionTypeRef fn:
                Collect(fn.ReturnType, acc);

                if (fn.ThisType is { } thisType)
                    Collect(thisType, acc);

                foreach (var p in fn.Parameters)
                    Collect(p.Type, acc);

                break;
            case IrMapTypeRef map:
                Collect(map.KeyType, acc);
                Collect(map.ValueType, acc);
                break;
            case IrSetTypeRef set:
                Collect(set.ElementType, acc);
                break;
            case IrPromiseTypeRef promise:
                Collect(promise.ResultType, acc);
                break;
            case IrGeneratorTypeRef gen:
                Collect(gen.YieldType, acc);
                break;
            case IrIterableTypeRef it:
                Collect(it.ElementType, acc);
                break;
            case IrIteratorTypeRef iterator:
                Collect(iterator.ElementType, acc);
                break;
            case IrKeyValuePairTypeRef kvp:
                Collect(kvp.KeyType, acc);
                Collect(kvp.ValueType, acc);
                break;
            case IrGroupingTypeRef grp:
                Collect(grp.KeyType, acc);
                Collect(grp.ElementType, acc);
                break;
        }
    }
}
