using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The class an <see cref="TypeInitializerLowering"/> lowers: its emitted name
/// (<see cref="ClassName"/>, which every guard and slot access spells), the
/// name <c>TypeInitializationException</c> reports (<see cref="TypeName"/>:
/// the simple C# name, containing types dotted, no type arguments), and the
/// shape facts the constructor guard depends on.
/// </summary>
internal sealed record TypeInitializerTarget(
    string ClassName,
    string TypeName,
    bool HasBaseClass,
    bool IsStruct,
    bool DeclaresParameterlessConstructor
);

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
        IrClassDeclaration ir,
        TypeInitializerTarget target,
        IrToTsBridgeContext lowering
    ) =>
        ir.StaticInitializer is { } staticConstructorBody
            ? new LazyTypeInitializerLowering(target, staticConstructorBody, lowering)
            : EagerTypeInitializerLowering.Instance;

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
}

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
