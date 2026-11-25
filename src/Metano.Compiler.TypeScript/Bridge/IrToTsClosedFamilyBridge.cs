using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers a C# 15 <c>closed</c> family to the TypeScript union shape (spec 010 FR-C5,
/// #738, decision W8-2). The closed type's runtime class is renamed
/// <c>X$Base</c> and stays module-private; the closed type's name becomes a union
/// alias of its direct cases plus a value binding to the runtime class, so a value
/// typed <c>X</c> narrows on its <c>kind</c> discriminant:
/// <code>
/// abstract class JobStatus$Base { abstract readonly kind: "Queued" | "Running"; … }
/// export type JobStatus = Queued | Running;
/// export const JobStatus: typeof JobStatus$Base = JobStatus$Base;
/// export class Running extends JobStatus { override get kind(): "Running" { return "Running"; } … }
/// </code>
/// </summary>
internal static class IrToTsClosedFamilyBridge
{
    private static readonly TsNamedType Never = new("never");

    /// <summary>
    /// The lowering of <paramref name="type"/> when it is a closed type whose family
    /// lowers to a union; <c>null</c> otherwise.
    /// </summary>
    public static ClosedSelfLowering? SelfOf(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        string typeName,
        ClosedFamilyLowering families
    )
    {
        if (!ir.Semantics.IsClosed || !families.IsLowered(type))
            return null;

        return new ClosedSelfLowering(
            typeName,
            typeName + ClosedFamilyLowering.RuntimeClassSuffix,
            new TsNamedType(typeName, TypeParameterArguments(ir))
        );
    }

    /// <summary>
    /// The <c>kind</c> member <paramref name="type"/> declares: the abstract union of
    /// the tags below a lowered closed type (an <c>override</c> on a closed
    /// intermediate), the tag accessor of a tag owner, or nothing.
    /// </summary>
    public static TsClassMember? KindMember(
        INamedTypeSymbol type,
        ClosedSelfLowering? self,
        ClosedFamilyLowering families
    )
    {
        if (self is not null && families.PlanOf(type) is { } plan)
        {
            var tags = plan.TagsByClosedType[type.OriginalDefinition];

            return new TsFieldMember(
                ClosedFamilyLowering.KindMemberName,
                TagsType(tags),
                Readonly: true,
                Abstract: true,
                Override: !SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, plan.Root)
            );
        }

        if (families.TagOf(type) is not { } tag)
            return null;

        var tagType = new TsStringLiteralType(tag);

        return new TsGetterMember(
            ClosedFamilyLowering.KindMemberName,
            tagType,
            [new TsReturnStatement(new TsStringLiteral(tag))],
            Override: true
        );
    }

    /// <summary>
    /// The union alias and the value binding that follow the runtime class of a
    /// lowered closed type.
    /// </summary>
    public static IReadOnlyList<TsTopLevel> FamilyDeclarations(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        ClosedSelfLowering self,
        IrToTsTypeOverrides? overrides
    )
    {
        var alias = new TsTypeAlias(
            self.FamilyName,
            new TsUnionType(UnionMembers(type, ir, overrides)),
            TypeParameters: IrToTsClassBridge.BuildTypeParameters(ir)
        );

        var binding = new TsVariableDeclaration(
            self.FamilyName,
            new TsIdentifier(self.RuntimeClassName),
            Exported: true,
            Type: new TsTypeQuery(self.RuntimeClassName)
        );

        return [alias, new TsTopLevelStatement(binding)];
    }

    private static List<TsType> UnionMembers(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        IrToTsTypeOverrides? overrides
    )
    {
        var cases = ClosedHierarchy.CasesOf(type).Cases;
        var irCases = ir.ClosedCases ?? [];
        var members = new List<TsType>();

        for (var i = 0; i < cases.Count; i++)
        {
            if (SymbolHelper.HasAttribute(cases[i].Case, "NoTranspile"))
                continue;

            members.Add(UnionMember(irCases[i], ir, overrides));
        }

        return members.Count > 0 ? members : [Never];
    }

    /// <summary>
    /// A case as a union member: the case itself, or, for a case of one
    /// instantiation only (<c>IntLeaf : Tree&lt;int&gt;</c>), a non-distributive
    /// conditional that keeps it exactly for that instantiation and for an open one,
    /// as C# 15 exhaustiveness does (<c>[T] extends [number] ? IntLeaf : never</c>).
    /// </summary>
    private static TsType UnionMember(
        IrClosedCase closedCase,
        IrClassDeclaration ir,
        IrToTsTypeOverrides? overrides
    )
    {
        var caseType = IrToTsTypeMapper.Map(closedCase.Type, overrides);

        if (closedCase.Shape is not ClosedCaseShape.FixedTypeArguments)
            return caseType;

        return new TsConditionalType(
            new TsTupleType(TypeParameterArguments(ir) ?? []),
            new TsTupleType(
                closedCase
                    .FixedTypeArguments.Select(a => IrToTsTypeMapper.Map(a, overrides))
                    .ToList()
            ),
            caseType,
            Never
        );
    }

    private static TsType TagsType(IReadOnlyList<string> tags) =>
        tags.Count switch
        {
            0 => Never,
            1 => new TsStringLiteralType(tags[0]),
            _ => new TsUnionType(tags.Select(tag => (TsType)new TsStringLiteralType(tag)).ToList()),
        };

    private static IReadOnlyList<TsType>? TypeParameterArguments(IrClassDeclaration ir) =>
        ir.TypeParameters is { Count: > 0 } parameters
            ? parameters.Select(p => (TsType)new TsNamedType(p.Name)).ToList()
            : null;
}
