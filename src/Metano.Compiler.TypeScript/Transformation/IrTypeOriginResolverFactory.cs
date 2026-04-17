using Metano.Compiler;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Transformation;

/// <summary>
/// Builds an <see cref="IrTypeOriginResolver"/> backed by the TypeScript target's
/// <see cref="TypeMappingContext.CrossAssemblyTypeMap"/>. The resolver:
/// <list type="bullet">
///   <item>Returns <see cref="IrTypeOrigin"/> with package ID + source namespace when
///   the type lives in another transpilable assembly.</item>
///   <item>Records cross-package misses and used-packages as side effects on the context
///   so packaging/diagnostics stay consistent with the legacy pipeline.</item>
/// </list>
/// </summary>
public static class IrTypeOriginResolverFactory
{
    public static IrTypeOriginResolver Create(TypeMappingContext ctx) =>
        named =>
        {
            var key = named.OriginalDefinition;
            if (!ctx.CrossAssemblyTypeMap.TryGetValue(key, out var entry))
            {
                var containingAssembly = key.ContainingAssembly;
                if (
                    containingAssembly is not null
                    && ctx.AssembliesNeedingEmitPackage.Contains(containingAssembly)
                )
                {
                    ctx.CrossPackageMisses.Add(named.ToDisplayString());
                }
                return null;
            }

            // Track package usage so package.json dependencies stay accurate even
            // when the IR path (rather than the legacy TypeMapper) is used.
            if (entry.VersionOverride is not null)
                ctx.UsedCrossPackages[entry.PackageName] = entry.VersionOverride;
            else if (entry.Symbol.ContainingAssembly is { } sourceAsm)
                ctx.UsedCrossPackages[entry.PackageName] = RoslynTypeQueries.FormatAssemblyVersion(
                    sourceAsm
                );

            var sourceNamespace = PathNaming.GetNamespace(entry.Symbol);
            return new IrTypeOrigin(
                PackageId: entry.PackageName,
                Namespace: sourceNamespace.Length > 0 ? sourceNamespace : null,
                AssemblyRootNamespace: entry.AssemblyRootNamespace.Length > 0
                    ? entry.AssemblyRootNamespace
                    : null
            );
        };
}
