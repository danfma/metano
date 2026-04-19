using Metano.Compiler.IR;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Optional resolver passed to <see cref="IrToTsTypeMapper.Map"/> that lets
/// the caller short-circuit the default mapping for specific IR shapes — for
/// example, mapping <see cref="IrPrimitive.Decimal"/> to a BCL-exported
/// <c>Decimal</c> name from <c>decimal.js</c> when the consuming assembly
/// declares <c>[ExportFromBcl(typeof(decimal), …)]</c>. Returning
/// <c>null</c> means "no override; fall back to the default mapping".
/// </summary>
public interface IrToTsTypeOverrides
{
    TsType? TryResolve(IrTypeRef type);
}

/// <summary>
/// Wraps the frontend-populated
/// <see cref="IrCompilation.BclExports"/> map (carried through
/// <see cref="Metano.Transformation.TypeMappingContext.BclExportMap"/>)
/// into the bridge-friendly resolver interface and tracks the package +
/// version of every override that fires so the package.json writer picks
/// up the cross-package dependency. Today it only resolves <c>decimal</c>
/// (the only <c>[ExportFromBcl]</c> entry shipped by
/// <c>Metano.Runtime</c>); the lookup uses Roslyn's full type name as the
/// dictionary key, matching what the frontend stores.
/// </summary>
public sealed class BclExportTypeOverrides(
    IReadOnlyDictionary<string, IrBclExport> map,
    Dictionary<string, string> usedCrossPackages
) : IrToTsTypeOverrides
{
    public TsType? TryResolve(IrTypeRef type)
    {
        if (type is not IrPrimitiveTypeRef p || BclKey(p.Primitive) is not { } key)
            return null;
        if (!map.TryGetValue(key, out var entry))
            return null;

        // Mirror TypeMapper.Map: every BCL hit records its package name and
        // version so the package.json writer surfaces the dependency.
        if (entry.FromPackage.Length > 0 && entry.Version is { Length: > 0 } version)
            usedCrossPackages[entry.FromPackage] = version;
        return new TsNamedType(entry.ExportedName);
    }

    private static string? BclKey(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.Decimal => "decimal",
            _ => null,
        };
}
