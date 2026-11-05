using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

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
/// <see cref="Metano.Compiler.TypeScript.Transformation.TypeMappingContext.BclExportMap"/>)
/// into the bridge-friendly resolver interface and tracks the package +
/// version of every override that fires so the package.json writer picks
/// up the cross-package dependency. Today it only resolves <c>decimal</c>
/// (the only <c>[ExportFromBcl]</c> entry shipped by
/// <c>Metano.Runtime</c>); the dictionary is keyed by the C# display
/// string of the <c>typeof(...)</c> argument (i.e.,
/// <c>ITypeSymbol.ToDisplayString()</c> with the default format —
/// <c>"decimal"</c> rather than <c>"System.Decimal"</c>), which is
/// exactly what <c>BclKey</c> below produces for each
/// <see cref="IrPrimitive"/>.
/// </summary>
public sealed class BclExportTypeOverrides(
    IReadOnlyDictionary<string, IrBclExport> map,
    CrossPackageDependencyLedger dependencyLedger
) : IrToTsTypeOverrides
{
    public TsType? TryResolve(IrTypeRef type)
    {
        if (
            type is not IrPrimitiveTypeRef p
            || BclKey(p.Primitive) is not { } key
            || !map.TryGetValue(key, out var entry)
            || entry.FromPackage.Length == 0
        )
            return null;

        return new TsNamedType(entry.ExportedName, Origin: OriginOf(entry));
    }

    /// <summary>
    /// The cross-package origin the import collector binds the BCL export of
    /// <paramref name="primitive"/> from, or <c>null</c> when the compilation
    /// declares no <c>[ExportFromBcl]</c> for it — or when the entry names no
    /// package, where the base emitter imported nothing. Every
    /// expression-position node the bridge emits for the export carries the
    /// origin, so the collector imports the package only for origin-carrying
    /// nodes — never by name match (issue #477). Sub-path empty: a BCL export
    /// sits on the package root barrel. Records the package dependency.
    /// </summary>
    public TsTypeOrigin? OriginOf(IrPrimitive primitive)
    {
        if (
            BclKey(primitive) is not { } key
            || !map.TryGetValue(key, out var entry)
            || entry.FromPackage.Length == 0
        )
            return null;

        return OriginOf(entry);
    }

    /// <summary>
    /// The origin of <paramref name="entry"/>, recording its package and
    /// version in the dependency ledger: a package referenced from any
    /// position lands in <c>package.json</c> the same way.
    /// </summary>
    private TsTypeOrigin OriginOf(IrBclExport entry)
    {
        if (entry.Version is { Length: > 0 } version)
            dependencyLedger.Record(entry.FromPackage, version);

        return new TsTypeOrigin(entry.FromPackage, SubPath: "");
    }

    private static string? BclKey(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.Decimal => "decimal",
            _ => null,
        };
}
