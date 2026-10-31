using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Moves the instance field and property initializers of a type with a
/// primary constructor into that constructor's body when any of them reads a
/// primary-constructor parameter (<c>class C(int a) { int Twice { get; } = a * 2; }</c>).
/// <para>
/// C# initializers see the primary-constructor parameters, but a target's
/// field initializers run in the class body, where the constructor
/// parameters are out of scope. Every initializer moves, not just the ones
/// that read a parameter, so they keep running in declaration order: one may
/// read a field another initializes (<c>int _b = _a + 1</c>). The moved
/// initializers run first in the body (after the base call and the
/// captured-parameter assignments the backend emits), as C# runs them before
/// the constructor's own code; the member keeps its declaration without an
/// initializer. A promoted property is left alone: its parameter property
/// already initializes it.
/// </para>
/// <para>
/// A parameter read is detected by name in the extracted IR, so an
/// initializer that names a same-named local (a lambda parameter) also
/// triggers the move — harmless: the initializers keep their order and
/// still run before the constructor's own code. As in any TypeScript class,
/// they run after the base constructor call, where C# runs them before it.
/// Reads of a captured parameter's backing field count too: the backend
/// assigns it in the constructor body, and inside initializers the extractor
/// reads a captured parameter through that field
/// (<see cref="IrExpressionExtractor.InitializersRunInConstructor"/>), so a
/// lambda shares the storage member bodies mutate.
/// </para>
/// <para>
/// Only a type whose sole constructor is the primary one is handled
/// (<see cref="AppliesTo"/>); with secondary constructors the initializers
/// stay where they are.
/// </para>
/// </summary>
internal static class PrimaryConstructorInitializerDeferral
{
    /// <summary>
    /// The members and constructor after the move: the moved initializers
    /// prepended to the constructor body and cleared on their members.
    /// </summary>
    public sealed record Result(
        List<IrMemberDeclaration> Members,
        IrConstructorDeclaration? Constructor
    );

    /// <summary>
    /// Whether initializers of <paramref name="type"/> that read a
    /// primary-constructor parameter run in its constructor: the type's only
    /// constructor is a primary constructor with parameters. The implicit
    /// capture scan relies on it to leave initializer reads uncaptured.
    /// </summary>
    public static bool AppliesTo(INamedTypeSymbol type) =>
        IrConstructorExtractor.FindPrimaryConstructor(type) is { Parameters.Length: > 0 } primary
        && IrConstructorExtractor.IsPrimaryConstructor(primary)
        && IrConstructorExtractor.ExplicitConstructorCount(type) == 1;

    /// <summary>
    /// Runs the move. <paramref name="constructor"/> must already carry the
    /// captured-parameter field names (<see cref="IrConstructorParameter.CapturedFieldName"/>),
    /// since reads of those fields trigger it too. Returns the inputs
    /// unchanged when the type is not handled or no initializer reads a
    /// parameter.
    /// </summary>
    public static Result Apply(
        INamedTypeSymbol type,
        IReadOnlyList<IrMemberDeclaration> members,
        IrConstructorDeclaration? constructor,
        Metano.Annotations.TargetLanguage? target
    )
    {
        var unchanged = new Result([.. members], constructor);

        if (constructor is null || !AppliesTo(type))
            return unchanged;

        var movableIndices = MovableInitializerIndices(members, constructor);

        if (!AnyReadsConstructionName(members, movableIndices, ConstructionNames(constructor)))
            return unchanged;

        var updatedMembers = members.ToList();
        var assignments = new List<IrStatement>(movableIndices.Count);

        foreach (var index in movableIndices)
        {
            var member = members[index];

            assignments.Add(AssignInitializer(type, member, InitializerOf(member)!, target));
            updatedMembers[index] = WithoutInitializer(member);
        }

        var updatedConstructor = constructor with
        {
            Body = [.. assignments, .. constructor.Body ?? []],
        };

        return new Result(updatedMembers, updatedConstructor);
    }

    /// <summary>
    /// The names an initializer may read that only exist during construction:
    /// the parameters, and the fields the backend assigns from captured
    /// parameters in the constructor body.
    /// </summary>
    private static HashSet<string> ConstructionNames(IrConstructorDeclaration constructor) =>
        constructor
            .Parameters.Select(p => p.Parameter.Name)
            .Concat(constructor.Parameters.Select(p => p.CapturedFieldName).OfType<string>())
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The members whose initializer would move, in declaration order: every
    /// instance initializer except a promoted property's.
    /// </summary>
    private static List<int> MovableInitializerIndices(
        IReadOnlyList<IrMemberDeclaration> members,
        IrConstructorDeclaration constructor
    )
    {
        var promoted = constructor
            .Parameters.Where(p => p.Promotion is not IrParameterPromotion.None)
            .Select(p => p.Parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Enumerable
            .Range(0, members.Count)
            .Where(i =>
                InitializerOf(members[i]) is not null && !promoted.Contains(members[i].Name)
            )
            .ToList();
    }

    private static bool AnyReadsConstructionName(
        IReadOnlyList<IrMemberDeclaration> members,
        IReadOnlyList<int> indices,
        HashSet<string> names
    ) => indices.Any(i => ReadsAny(InitializerOf(members[i])!, names));

    private static IrExpression? InitializerOf(IrMemberDeclaration member) =>
        member switch
        {
            IrFieldDeclaration
            {
                IsStatic: false,
                IsConstant: false,
                IsCapturedByCtor: false
            } field => field.Initializer,
            IrPropertyDeclaration { IsStatic: false } property => property.Initializer,
            _ => null,
        };

    private static bool ReadsAny(IrExpression expression, HashSet<string> names) =>
        expression switch
        {
            IrIdentifier identifier => names.Contains(identifier.Name),
            IrMemberAccess { Target: IrThisExpression } access => names.Contains(access.MemberName),
            _ => IrNodeChildren.ExpressionsOf(expression).Any(e => ReadsAny(e, names))
                || IrNodeChildren.StatementsOf(expression).Any(s => ReadsAny(s, names)),
        };

    private static bool ReadsAny(IrStatement statement, HashSet<string> names) =>
        IrNodeChildren.ExpressionsOf(statement).Any(e => ReadsAny(e, names))
        || IrNodeChildren.StatementsOf(statement).Any(s => ReadsAny(s, names));

    /// <summary>
    /// <c>this.member = initializer</c>, the access carrying the member's
    /// declared-name facts so a backend writes the name it declared.
    /// </summary>
    private static IrStatement AssignInitializer(
        INamedTypeSymbol type,
        IrMemberDeclaration member,
        IrExpression initializer,
        Metano.Annotations.TargetLanguage? target
    )
    {
        var symbol = type.GetMembers(member.Name)
            .FirstOrDefault(m => m is IFieldSymbol or IPropertySymbol);

        var origin = new IrMemberOrigin(
            type.GetStableFullName(),
            member.Name,
            EmittedName: symbol?.GetNameOverride(target),
            IsTranspiledDataMember: true
        );

        var access = new IrMemberAccess(new IrThisExpression(), member.Name, origin);

        return new IrExpressionStatement(
            new IrBinaryExpression(access, IrBinaryOp.Assign, initializer)
        );
    }

    private static IrMemberDeclaration WithoutInitializer(IrMemberDeclaration member) =>
        member switch
        {
            IrFieldDeclaration field => field with { Initializer = null },
            IrPropertyDeclaration property => property with { Initializer = null },
            _ => member,
        };
}
