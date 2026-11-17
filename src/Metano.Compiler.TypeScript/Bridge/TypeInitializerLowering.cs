using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The class an <see cref="TypeInitializerLowering"/> lowers: its emitted name
/// (<see cref="ClassName"/>, which every guard and slot access spells), the
/// name <c>TypeInitializationException</c> reports (<see cref="TypeName"/>,
/// <see cref="Metano.Compiler.Analysis.ClrTypeName.OfTypeInitializer"/>), and the
/// shape facts the constructor guard depends on.
/// </summary>
internal sealed record TypeInitializerTarget(
    string ClassName,
    string TypeName,
    bool HasBaseClass,
    bool IsStruct,
    ImplicitDefaultConstruction ImplicitDefault
);

/// <summary>
/// Where a struct's emitted constructor builds C#'s implicit default value
/// (<c>default(S)</c>, or <c>new S()</c> without a declared parameterless
/// constructor), which runs no constructor, so neither the static constructor
/// nor the instance field initializers.
/// </summary>
internal enum ImplicitDefaultConstruction
{
    /// <summary>Every path of the constructor runs a constructor C# declares.</summary>
    None,

    /// <summary>A struct that declares no constructor: the whole constructor builds it.</summary>
    WholeConstructor,

    /// <summary>
    /// A dispatched struct without a declared parameterless constructor: the
    /// dispatcher's first branch builds it (spec 013 FR-027).
    /// </summary>
    LeadingBranch,
}

/// <summary>
/// A class's constructor and members, as the type initializer completes them.
/// </summary>
internal sealed record TsClassBody(TsConstructor Constructor, IReadOnlyList<TsClassMember> Members);

/// <summary>
/// How a class runs its static initialization (FR-066, #681): eagerly at class
/// definition (<see cref="EagerTypeInitializerLowering"/>, a type without an
/// explicit static constructor), or lazily at first use behind a per-type
/// guard (<see cref="LazyTypeInitializerLowering"/>). The class emitter
/// chooses one per class and routes its data members and its composed body
/// through it.
/// </summary>
internal abstract class TypeInitializerLowering
{
    public static TypeInitializerLowering For(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        TypeInitializerTarget target,
        IrToTsBridgeContext lowering
    )
    {
        if (RunsLazily(type))
            return new LazyTypeInitializerLowering(
                target,
                ir,
                ir.StaticInitializer ?? [],
                lowering
            );

        // A containerless class keeps its static constructor report (MS0001,
        // ModuleVariableValidator) and is emitted without the body.
        if (ir.StaticInitializer is not null && !type.IsContainerless())
            throw new InvalidOperationException(
                $"'{type.Name}' has a static constructor body but is not lowered lazily; "
                    + "the eager lowering has nowhere to run it."
            );

        return EagerTypeInitializerLowering.Instance;
    }

    /// <summary>
    /// The one rule for which types run their static initialization lazily: a
    /// class or struct emitted as a TypeScript class (not a <c>[PlainObject]</c>
    /// interface, a <c>[NoContainer]</c> type or a module-shaped static class)
    /// that declares a static constructor. The class emitter lowers such a type
    /// with <see cref="LazyTypeInitializerLowering"/>, and the barrel orders its
    /// modules by the cycles its initializer forms
    /// (<see cref="InitializerCycleHoists"/>).
    /// </summary>
    public static bool RunsLazily(INamedTypeSymbol type) =>
        type.TypeKind is TypeKind.Class or TypeKind.Struct
        && IrStaticConstructorExtractor.DeclaredBy(type) is not null
        && !type.HasPlainObject()
        && !type.IsContainerless()
        && !TypeTransformer.IsModuleShapedStaticClass(type);

    /// <summary>The members a field lowers to, given what the class bridge built for it.</summary>
    public abstract IReadOnlyList<TsClassMember> LowerField(
        IrFieldDeclaration field,
        TsFieldMember built
    );

    /// <summary>The members a property lowers to, given what the class bridge built for it.</summary>
    public abstract IReadOnlyList<TsClassMember> LowerProperty(
        IrPropertyDeclaration property,
        IReadOnlyList<TsClassMember> built
    );

    /// <summary>
    /// Completes the composed class: <paramref name="userMembers"/> are the members
    /// lowered from the type's own C# members, the ones a guard may start.
    /// </summary>
    public abstract TsClassBody Complete(TsClassBody body, IReadOnlySet<TsClassMember> userMembers);

    /// <summary>
    /// The static members whose TypeScript name another member's lowering
    /// already declares; none when static members keep their own declarations.
    /// </summary>
    public virtual IReadOnlyList<SlotNameClash> SlotNameClashes() => [];
}

/// <summary>
/// A static member (<see cref="Member"/>) whose TypeScript name
/// (<see cref="Name"/>) is also the name of the accessors
/// <see cref="SlotOwner"/>'s slot declares.
/// </summary>
internal sealed record SlotNameClash(
    IrMemberDeclaration Member,
    string Name,
    IrMemberDeclaration SlotOwner
);

/// <summary>
/// Today's static initialization: static field initializers run at class
/// definition, which .NET's <c>beforefieldinit</c> allows for a type without an
/// explicit static constructor. Every member passes through unchanged.
/// </summary>
internal sealed class EagerTypeInitializerLowering : TypeInitializerLowering
{
    public static readonly EagerTypeInitializerLowering Instance = new();

    private EagerTypeInitializerLowering() { }

    public override IReadOnlyList<TsClassMember> LowerField(
        IrFieldDeclaration field,
        TsFieldMember built
    ) => [built];

    public override IReadOnlyList<TsClassMember> LowerProperty(
        IrPropertyDeclaration property,
        IReadOnlyList<TsClassMember> built
    ) => built;

    public override TsClassBody Complete(
        TsClassBody body,
        IReadOnlySet<TsClassMember> userMembers
    ) => body;
}
