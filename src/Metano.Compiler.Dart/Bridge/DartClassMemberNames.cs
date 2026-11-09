using Metano.Compiler.Analysis;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// A user member of a class: the Dart name the class bridge declares it under,
/// its C# name (where a finding about it is located) and whether it is static.
/// </summary>
internal sealed record DartUserMember(string DartName, string CSharpName, bool IsStatic);

/// <summary>
/// The Dart names a class's user members declare — the names the class bridge
/// gives its promoted parameters, fields, properties, single methods and the
/// synthesized record members — which every generated name claims against.
/// </summary>
internal static class DartClassMemberNames
{
    private const string CopyWith = "copyWith";

    public static IReadOnlyList<DartUserMember> Of(IrClassDeclaration ir, DartOverloadShape shape)
    {
        var members = new List<DartUserMember>();

        foreach (var parameter in ir.Constructor?.Parameters ?? [])
        {
            if (parameter.Promotion != IrParameterPromotion.None)
            {
                members.Add(
                    new DartUserMember(
                        IrToDartNamingPolicy.ToParameterName(parameter.Parameter.Name),
                        parameter.Parameter.Name,
                        IsStatic: false
                    )
                );
            }
        }

        foreach (var member in ir.Members ?? [])
        {
            if (NameOf(member, shape) is { } name)
                members.Add(new DartUserMember(name, member.Name, member.IsStatic));
        }

        if (ir.Semantics.IsRecord && !ir.Semantics.IsPlainObject)
            members.Add(new DartUserMember(CopyWith, CopyWith, IsStatic: false));

        return members;
    }

    /// <summary>
    /// Whether a method carries a dispatched group whose overloads Dart
    /// declares as fast paths: an overloaded operator group keeps one
    /// <c>operator</c> declaration instead.
    /// </summary>
    public static bool IsFastPathGroup(IrMethodDeclaration method) =>
        method.OverloadName is not null && !method.Semantics.IsOperator;

    /// <summary>
    /// The Dart name of a method declared under one name: a single method's
    /// <c>[Name]</c> or camelCase name, an operator's (a conversion operator's
    /// <c>[Name]</c> names it), and the primary that a keep-today's-emission
    /// shape keeps of a dispatched group. There the core decides what the
    /// group's <c>[Name]</c> means, and the call sites follow it
    /// (<see cref="Extraction.OverloadEmittedName"/>): it pins a suffix, which
    /// names nothing the primary declares, unless the type renames whole
    /// members (<see cref="DartOverloadShape.PinsSuffixes"/>).
    /// </summary>
    public static string MethodNameOf(DartOverloadShape shape, IrMethodDeclaration method) =>
        method is { OverloadName: not null, Semantics.IsOperator: false } && shape.PinsSuffixes
            ? IrToDartNamingPolicy.ToParameterName(method.Name)
            : IrToDartNamingPolicy.ToMemberName(method.Name, method.Attributes);

    private static string? NameOf(IrMemberDeclaration member, DartOverloadShape shape) =>
        member switch
        {
            IrFieldDeclaration field => IrToDartNamingPolicy.ToParameterName(field.Name),
            IrPropertyDeclaration property => IrToDartNamingPolicy.ToMemberName(
                property.Name,
                property.Attributes
            ),
            IrMethodDeclaration method when IrObjectContract.IsHashCodeOverride(method) =>
                "hashCode",
            IrMethodDeclaration { Semantics.IsOperator: true } => null,
            IrMethodDeclaration method when shape.NamesMembers && IsFastPathGroup(method) => null,
            IrMethodDeclaration method => MethodNameOf(shape, method),
            _ => null,
        };
}
