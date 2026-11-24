using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Emits a record's .NET text (baseline FR-093) from its
/// <see cref="IrClassDeclaration.Printing"/> facts through the protocol of
/// FR-094, which chains like C#'s <c>PrintMembers</c>:
/// <list type="bullet">
///   <item><c>protected $printMembers(): string</c> — the record's own members
///   (<c>P1 = v1, P2 = v2</c>), after the base's text when the record extends an
///   emitted record; declared by every record a record can derive from.</item>
///   <item><c>toString(): string</c> — <c>Name { members }</c>, or
///   <c>Name { }</c> when nothing prints; emitted only where C# synthesizes
///   <c>ToString</c>.</item>
/// </list>
/// A sealed record or a record struct without a record base inlines its members
/// into <c>toString()</c>: nothing can chain to it.
/// </summary>
internal static class IrToTsRecordPrintingBridge
{
    private const string ToStringMethodName = "toString";

    private const string PrintMembersMethodName = "$printMembers";

    private const string MemberSeparator = ", ";

    /// <summary>
    /// The instance members <see cref="Generate"/> declares on
    /// <paramref name="ir"/>, which the class's claim pass treats as taken.
    /// </summary>
    internal static IReadOnlyList<string> SynthesizedMemberNames(
        IrClassDeclaration ir,
        TsClassMemberTable userMembers
    )
    {
        if (!Prints(ir))
            return [];

        string[] toString = EmitsToString(ir, userMembers) ? [ToStringMethodName] : [];

        return DeclaresPrintMembers(ir) ? [PrintMembersMethodName, .. toString] : toString;
    }

    /// <summary><c>$printMembers()</c> and <c>toString()</c>, each when <paramref name="ir"/> needs it.</summary>
    internal static IReadOnlyList<TsClassMember> Generate(
        IrClassDeclaration ir,
        TsClassMemberTable userMembers,
        IrToTsBridgeContext lowering
    )
    {
        if (!Prints(ir))
            return [];

        var members = ir.Printing!.Members.Select(member => new PrintedMember(
                member.Name,
                IrToTsExpressionBridge.Map(member.Text, lowering)
            ))
            .ToList();

        var extendsBase = IrToTsClassBridge.BuildExtends(ir) is not null;
        var generated = new List<TsMethodMember>();

        if (DeclaresPrintMembers(ir))
            generated.Add(GeneratePrintMembers(members, extendsBase));

        if (EmitsToString(ir, userMembers))
            generated.Add(GenerateToString(ir, members));

        return generated.Select(member => member with { Override = extendsBase }).ToList();
    }

    /// <summary>
    /// Reports <c>MS0001</c> (a warning) when C# synthesizes the record's
    /// <c>ToString</c> but this target emits no text for it (baseline FR-093.5): a
    /// user <c>PrintMembers</c> in the record or a record base, or another member
    /// the target emits as <c>toString</c>.
    /// </summary>
    internal static void ReportUnsupported(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        TsClassMemberTable userMembers,
        Action<MetanoDiagnostic> report
    )
    {
        if (ir.Printing is not { ToStringFate: IrRecordPrintFate.Synthesized } printing)
            return;

        var reason =
            printing.PrintMembersFate is IrRecordPrintFate.UserDeclared
                ? "it or a record it derives from declares PrintMembers"
                : ClaimedToStringReason(userMembers);

        if (reason is null)
            return;

        report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"The synthesized ToString() of record '{type.Name}' is not emitted: {reason}, "
                    + "which this target does not support yet.",
                type.Locations.FirstOrDefault()
            )
        );
    }

    private static string? ClaimedToStringReason(TsClassMemberTable userMembers) =>
        userMembers.Instances.GetValueOrDefault(ToStringMethodName) is { } declaredBy
            ? $"another of its members ('{declaredBy}') is emitted as '{ToStringMethodName}'"
            : null;

    private static bool Prints(IrClassDeclaration ir) =>
        ir.Printing is { PrintMembersFate: not IrRecordPrintFate.UserDeclared };

    private static bool EmitsToString(IrClassDeclaration ir, TsClassMemberTable userMembers) =>
        ir.Printing!.ToStringFate is IrRecordPrintFate.Synthesized
        && !userMembers.Instances.ContainsKey(ToStringMethodName);

    /// <summary>
    /// A record a record can derive from (a class record that is not sealed), or one
    /// that derives from an emitted record.
    /// </summary>
    private static bool DeclaresPrintMembers(IrClassDeclaration ir) =>
        !(ir.Semantics.IsSealed || ir.Semantics.IsValueType)
        || IrToTsClassBridge.BuildExtends(ir) is not null;

    /// <summary>
    /// <c>$printMembers()</c>: the own members; a derived record starts with the
    /// base's text (<c>super.$printMembers?.() ?? ""</c>, which tolerates a base
    /// package generated without the member) and adds the separator only when
    /// both parts print, as C#'s <c>if (base.PrintMembers(b)) b.Append(", ")</c>.
    /// </summary>
    private static TsMethodMember GeneratePrintMembers(
        IReadOnlyList<PrintedMember> members,
        bool extendsBase
    )
    {
        IReadOnlyList<TsStatement> body = (extendsBase, members.Count) switch
        {
            (false, 0) => [new TsReturnStatement(new TsStringLiteral(""))],
            (false, _) => [new TsReturnStatement(MemberList(members, prefix: null))],
            (true, 0) => [new TsReturnStatement(BasePrintMembers())],
            (true, _) =>
            [
                new TsVariableDeclaration("base", BasePrintMembers()),
                new TsReturnStatement(MemberList(members, BasePrefix(new TsIdentifier("base")))),
            ],
        };

        return new TsMethodMember(
            PrintMembersMethodName,
            [],
            new TsStringType(),
            body,
            Accessibility: TsAccessibility.Protected
        );
    }

    /// <summary>
    /// <c>toString()</c>: <c>Name { members }</c> through <c>$printMembers()</c>,
    /// with the runtime test for an empty list when the record's own list is empty
    /// (a derived record may still print); a record that declares no
    /// <c>$printMembers</c> inlines its members.
    /// </summary>
    private static TsMethodMember GenerateToString(
        IrClassDeclaration ir,
        IReadOnlyList<PrintedMember> members
    )
    {
        var name = ir.Printing!.DisplayName;
        var empty = new TsStringLiteral(name + " { }");

        IReadOnlyList<TsStatement> body = (DeclaresPrintMembers(ir), members.Count) switch
        {
            (false, 0) => [new TsReturnStatement(empty)],
            (false, _) => [new TsReturnStatement(Braced(name, MemberList(members, prefix: null)))],
            (true, 0) =>
            [
                new TsVariableDeclaration("members", ThisPrintMembers()),
                new TsReturnStatement(
                    new TsConditionalExpression(
                        new TsIdentifier("members"),
                        Braced(name, new TsIdentifier("members")),
                        empty
                    )
                ),
            ],
            (true, _) => [new TsReturnStatement(Braced(name, ThisPrintMembers()))],
        };

        return new TsMethodMember(ToStringMethodName, [], new TsStringType(), body);
    }

    /// <summary><c>`Name { ${members} }`</c>.</summary>
    private static TsTemplateLiteral Braced(string name, TsExpression members) =>
        members is TsTemplateLiteral inline
            ? Prefixed(name + " { ", inline, suffix: " }")
            : new TsTemplateLiteral([name + " { ", " }"], [members]);

    /// <summary>
    /// <c>`P1 = ${v1}, P2 = ${v2}`</c>, after <paramref name="prefix"/> when given
    /// (a hole that prints the base's text and its separator).
    /// </summary>
    private static TsTemplateLiteral MemberList(
        IReadOnlyList<PrintedMember> members,
        TsExpression? prefix
    )
    {
        var quasis = new List<string>();
        var expressions = new List<TsExpression>();

        if (prefix is not null)
        {
            quasis.Add("");
            expressions.Add(prefix);
        }

        var pending = "";

        foreach (var member in members)
        {
            quasis.Add(pending + member.Name + " = ");
            expressions.Add(member.Text);
            pending = MemberSeparator;
        }

        quasis.Add("");

        return new TsTemplateLiteral(quasis, expressions);
    }

    private static TsTemplateLiteral Prefixed(string prefix, TsTemplateLiteral text, string suffix)
    {
        var quasis = text.Quasis.ToList();

        quasis[0] = prefix + quasis[0];
        quasis[^1] += suffix;

        return new TsTemplateLiteral(quasis, text.Expressions);
    }

    /// <summary><c>base ? `${base}, ` : ""</c>.</summary>
    private static TsConditionalExpression BasePrefix(TsExpression baseText) =>
        new(
            baseText,
            new TsTemplateLiteral(["", MemberSeparator], [baseText]),
            new TsStringLiteral("")
        );

    /// <summary><c>super.$printMembers?.() ?? ""</c>.</summary>
    private static TsBinaryExpression BasePrintMembers() =>
        new(
            new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("super"), PrintMembersMethodName),
                [],
                Optional: true
            ),
            "??",
            new TsStringLiteral("")
        );

    private static TsCallExpression ThisPrintMembers() =>
        new(new TsPropertyAccess(new TsIdentifier("this"), PrintMembersMethodName), []);

    /// <summary>A printed member: its C# name and its text as TypeScript.</summary>
    private sealed record PrintedMember(string Name, TsExpression Text);
}
