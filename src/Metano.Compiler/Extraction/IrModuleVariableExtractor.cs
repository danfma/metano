using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

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
/// members, indexers, <c>[Inline]</c>, <c>[External]</c> and <c>[Ignore]</c>d
/// members are not module state and are skipped (see <see cref="IsModuleState"/>).
/// </para>
/// </summary>
public static class IrModuleVariableExtractor
{
    public static IReadOnlyList<IrMemberDeclaration> Extract(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver = null,
        Compilation? compilation = null,
        TargetLanguage? target = null
    )
    {
        var variables = new List<IrMemberDeclaration>();

        foreach (var member in type.GetMembers())
        {
            if (!IsModuleState(member, target))
            {
                continue;
            }

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
            {
                variables.Add(variable);
            }
        }

        return variables;
    }

    /// <summary>
    /// True when <paramref name="member"/> of a containerless static class is
    /// module state: a static field or a static non-indexer property that is
    /// neither compiler-generated, <c>[Ignore]</c>d for <paramref name="target"/>,
    /// a C# 14 extension block member, <c>[Inline]</c> nor <c>[External]</c>. <see cref="Extract"/> returns exactly
    /// these members, in declaration order.
    /// </summary>
    public static bool IsModuleState(ISymbol member, TargetLanguage? target)
    {
        if (!member.IsStatic || member.IsImplicitlyDeclared)
            return false;

        if (SymbolHelper.HasIgnore(member, target) || member.IsExtensionBlockMember())
            return false;

        // Rewritten at every call site, so never read as a module variable.
        if (member.IsInlineMember() || member.HasExternal())
            return false;

        return member switch
        {
            IFieldSymbol => true,
            IPropertySymbol property => property.Parameters.Length == 0,
            _ => false,
        };
    }
}
