using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Extracts the module-level state of a containerless static class
/// (<c>[NoContainer]</c> / <c>[ExportedAsModule]</c>): its static fields and its
/// static properties, as the same <see cref="IrFieldDeclaration"/> /
/// <see cref="IrPropertyDeclaration"/> nodes a class declares. Call sites already
/// read these members as bare identifiers (the container vanishes), so a backend
/// declares each one as a top-level variable in the module.
/// <para>
/// Computed properties (a getter or setter with a body) are returned too, with
/// their bodies, so the backend decides whether it can declare them; extension
/// members, indexers and <c>[Ignore]</c>d members are not module state and are
/// skipped.
/// </para>
/// </summary>
public static class IrModuleVariableExtractor
{
    public static IReadOnlyList<IrMemberDeclaration> Extract(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver = null,
        Compilation? compilation = null,
        Metano.Annotations.TargetLanguage? target = null
    )
    {
        var variables = new List<IrMemberDeclaration>();

        foreach (var member in type.GetMembers())
        {
            if (!IsModuleState(member, target))
                continue;

            var variable = member switch
            {
                IFieldSymbol field => IrFieldExtractor.TryExtract(
                    field,
                    originResolver,
                    compilation,
                    target
                ),
                IPropertySymbol property => IrPropertyExtractor.Extract(
                    property,
                    originResolver,
                    compilation,
                    target
                ),
                _ => (IrMemberDeclaration?)null,
            };

            if (variable is not null)
                variables.Add(variable);
        }

        return variables;
    }

    private static bool IsModuleState(ISymbol member, Metano.Annotations.TargetLanguage? target)
    {
        if (!member.IsStatic || member.IsImplicitlyDeclared)
            return false;

        if (SymbolHelper.HasIgnore(member, target))
            return false;

        return member switch
        {
            IFieldSymbol => true,
            IPropertySymbol property => property.Parameters.Length == 0
                && !property.IsExtensionBlockMember(),
            _ => false,
        };
    }

    /// <summary>
    /// A C# 14 <c>extension(R r) { … }</c> block member, which Roslyn may also surface
    /// on the containing static class. Those members lower to functions through
    /// <see cref="IrModuleFunctionExtractor"/>, never to module state.
    /// </summary>
    private static bool IsExtensionBlockMember(this IPropertySymbol property) =>
        property.DeclaringSyntaxReferences.Any(reference =>
            reference
                .GetSyntax()
                .Ancestors()
                .Any(node => node.Kind().ToString() == "ExtensionBlockDeclaration")
        );
}
