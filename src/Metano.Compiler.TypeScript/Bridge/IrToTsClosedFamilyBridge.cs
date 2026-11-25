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
/// export const JobStatus = JobStatus$Base as { [Symbol.hasInstance]: … } &amp; typeof JobStatus$Base;
/// export class Running extends JobStatus { override get kind(): "Running" { return "Running"; } … }
/// </code>
/// </summary>
internal static class IrToTsClosedFamilyBridge
{
    private const string HasInstanceKey = "[Symbol.hasInstance]";

    /// <summary>
    /// The binding's <c>hasInstance</c> type parameter: a <c>$</c> keeps it apart from
    /// every type name a C# declaration emits.
    /// </summary>
    private const string PrototypeParameter = "$P";

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
            new TsCastExpression(new TsIdentifier(self.RuntimeClassName), BindingType(self, ir)),
            Exported: true
        );

        return [alias, new TsTopLevelStatement(binding)];
    }

    /// <summary>
    /// The value binding's type: the runtime class's static side with a
    /// <c>Symbol.hasInstance</c> that narrows <c>v instanceof X</c> to the union
    /// rather than to the runtime class (#758). The signature reads the prototype of
    /// the class <c>instanceof</c> tests against, so a case, or any class below one,
    /// that inherits it still narrows to its own instance type; it comes first, so a
    /// closed intermediate's own signature wins over the one it inherits. The runtime
    /// class's own <c>hasInstance</c> returns a plain boolean, so the binding asserts
    /// the type instead of annotating it. A generic family narrows to its union over
    /// <c>any</c>, which keeps the instantiation a value is already typed with.
    /// <code>
    /// { [Symbol.hasInstance]: &lt;$P&gt;(this: { prototype: $P }, value: unknown) =&gt;
    ///     value is X$Base extends $P ? X : $P } &amp; typeof X$Base
    /// </code>
    /// </summary>
    private static TsType BindingType(ClosedSelfLowering self, IrClassDeclaration ir)
    {
        var anyArguments = ir.TypeParameters is { Count: > 0 } parameters
            ? parameters.Select(_ => (TsType)new TsAnyType()).ToList()
            : null;

        var prototype = new TsNamedType(PrototypeParameter);

        var narrowed = new TsConditionalType(
            new TsNamedType(self.RuntimeClassName, anyArguments),
            prototype,
            new TsNamedType(self.FamilyName, anyArguments),
            prototype
        );

        var hasInstance = new TsFunctionType(
            [new TsParameter("value", new TsNamedType("unknown"))],
            new TsTypePredicateType("value", narrowed),
            ThisType: new TsObjectType([new TsObjectTypeField("prototype", prototype)]),
            TypeParameters: [new TsTypeParameter(PrototypeParameter)]
        );

        return new TsIntersectionType([
            new TsObjectType([new TsObjectTypeField(HasInstanceKey, hasInstance)]),
            new TsTypeQuery(self.RuntimeClassName),
        ]);
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
