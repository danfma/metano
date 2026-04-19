using Metano.Compiler;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Transformation;

/// <summary>
/// Builds an <see cref="IrTypeOriginResolver"/> backed by the IR's
/// <see cref="IrCompilation.CrossAssemblyOrigins"/> map (carried through
/// <see cref="TypeMappingContext.CrossAssemblyOrigins"/>). The resolver:
/// <list type="bullet">
///   <item>Returns the frontend-built <see cref="IrTypeOrigin"/> when the type
///   lives in another transpilable assembly that declared <c>[EmitPackage]</c>
///   for this target.</item>
///   <item>Falls back to the source assembly's MSBuild
///   <see cref="IAssemblySymbol.Identity"/> version when the IR origin's
///   <see cref="IrTypeOrigin.VersionHint"/> is null, so the package.json writer
///   still pins a deterministic dependency range.</item>
///   <item>Records cross-package misses (referenced assembly opted into
///   transpilation but lacks <c>[EmitPackage]</c>) so the consumer can raise
///   MS0007 at the right call site.</item>
/// </list>
/// </summary>
public static class IrTypeOriginResolverFactory
{
    public static IrTypeOriginResolver Create(TypeMappingContext ctx) =>
        named =>
        {
            var key = named.OriginalDefinition;
            var stableKey = key.GetStableFullName();
            if (!ctx.CrossAssemblyOrigins.TryGetValue(stableKey, out var origin))
            {
                var containingAssembly = key.ContainingAssembly;
                if (
                    containingAssembly is not null
                    && ctx.AssembliesNeedingEmitPackage.Contains(containingAssembly.Name)
                )
                {
                    ctx.CrossPackageMisses.Add(named.ToDisplayString());
                }
                return null;
            }

            // Track package usage so package.json dependencies stay accurate even
            // when the IR path (rather than the legacy TypeMapper) is used. Prefer
            // the explicit [EmitPackage(Version=...)] override; otherwise read the
            // source assembly's MSBuild Identity through Roslyn — that fallback
            // can't move onto the IR until the frontend is target-aware.
            if (origin.VersionHint is { Length: > 0 } versionHint)
                ctx.UsedCrossPackages[origin.PackageId] = versionHint;
            else if (key.ContainingAssembly is { } sourceAsm)
                ctx.UsedCrossPackages[origin.PackageId] = RoslynTypeQueries.FormatAssemblyVersion(
                    sourceAsm
                );

            return origin;
        };
}
