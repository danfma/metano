using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Pattern tests against a subject: <c>is</c> expressions and the labels of a
/// <c>switch</c> statement.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <c>subject is pattern</c>. A backend without expression-scoped
    /// variables reads the subject once per test and designator, so when the
    /// pattern declares locals and the subject is not
    /// <see cref="IsStableSubject">stable</see>, the subject is captured first
    /// by a synthesized <c>var</c> designator: <c>F() is Circle c</c> becomes
    /// <c>F() is var $subject0 and Circle c</c>, which is exactly its C#
    /// meaning, and the rest of the pattern reads the captured local. A
    /// pattern that binds nothing but reads its subject more than once
    /// (<c>F() is &gt;= '0' and &lt;= '9'</c>) captures it the same way, unless
    /// the subject is a repeatable read (<see cref="NeedsCapturedSubject"/>).
    /// </summary>
    private IrIsPatternExpression ExtractIsPattern(IsPatternExpressionSyntax isPattern) =>
        BuildIsPattern(
            isPattern.Expression,
            Extract(isPattern.Expression),
            ExtractPattern(isPattern.Pattern)
        );

    /// <summary>
    /// The <c>is</c> node of <paramref name="pattern"/> over
    /// <paramref name="subject"/> (the lowering of <paramref name="syntax"/>),
    /// capturing the subject first when <see cref="NeedsCapturedSubject"/>.
    /// </summary>
    private IrIsPatternExpression BuildIsPattern(
        ExpressionSyntax syntax,
        IrExpression subject,
        IrPattern pattern
    ) =>
        NeedsCapturedSubject(syntax, subject, pattern)
            ? new IrIsPatternExpression(subject, CapturedFirst(ExpressionType(syntax), pattern))
            : new IrIsPatternExpression(subject, pattern);

    /// <summary>
    /// Whether the subject runs once, in a synthesized designator, before the
    /// pattern reads it. A pattern that declares locals needs it for a
    /// subject that is not <see cref="IsStableSubject">stable</see>. A pattern
    /// that binds nothing needs it only when it reads the subject more than
    /// once (<see cref="IrEvaluationTraits.ReadsSubjectOnce"/>) and the
    /// subject is not a
    /// <see cref="IrEvaluationTraits.IsRepeatableRead">repeatable read</see>
    /// (a string or array element read is: <c>text[i] is &gt;= '0' and &lt;= '9'</c>
    /// reads it twice).
    /// </summary>
    private bool NeedsCapturedSubject(
        ExpressionSyntax syntax,
        IrExpression subject,
        IrPattern pattern
    ) =>
        IrPatternDesignators.Declares(pattern)
            ? !IsStableSubject(syntax, subject)
            : _context.HasAssignmentInTestForm
                && !IrEvaluationTraits.ReadsSubjectOnce(pattern)
                && !IrEvaluationTraits.IsRepeatableRead(subject);

    /// <summary>
    /// <c>var $subjectN and pattern</c>: captures the matched value in a
    /// synthesized designator (the <c>$</c> keeps it clear of every C#
    /// identifier) that the rest of the pattern reads.
    /// </summary>
    private IrLogicalPattern CapturedFirst(IrTypeRef type, IrPattern pattern) =>
        new(
            IrLogicalOp.And,
            new IrVarPattern(
                new IrPatternDesignator($"$subject{_patternSubjectCounter++}", type)
                {
                    IsSynthesized = true,
                }
            ),
            pattern
        );

    /// <summary>
    /// Whether reading the subject again yields the value of the first read
    /// without running user code: <c>this</c>, a local or parameter, or a
    /// chain of field and auto-property reads rooted at one of those (or at a
    /// type, for static members) — and whose lowered form is still a plain
    /// <see cref="IrEvaluationTraits.IsReusableReceiver">reusable receiver</see>.
    /// A property with a hand-written, <c>field</c>-keyword or overridable
    /// getter, or one declared in another assembly, may compute its value, so
    /// it is not stable.
    /// </summary>
    private bool IsStableSubject(ExpressionSyntax syntax, IrExpression lowered) =>
        IrEvaluationTraits.IsReusableReceiver(lowered) && IsStableRead(syntax);

    private bool IsStableRead(ExpressionSyntax syntax) =>
        syntax switch
        {
            ThisExpressionSyntax => true,
            ParenthesizedExpressionSyntax parenthesized => IsStableRead(parenthesized.Expression),
            IdentifierNameSyntax identifier => IsStableSymbol(
                _semantic.GetSymbolInfo(identifier).Symbol
            ),
            MemberAccessExpressionSyntax access
                when access.IsKind(SyntaxKind.SimpleMemberAccessExpression) => IsStableMemberRead(
                access
            ),
            _ => false,
        };

    private bool IsStableMemberRead(MemberAccessExpressionSyntax access) =>
        IsStableRoot(access.Expression)
        && IsStableSymbol(_semantic.GetSymbolInfo(access.Name).Symbol);

    private bool IsStableRoot(ExpressionSyntax receiver) =>
        _semantic.GetSymbolInfo(receiver).Symbol is ITypeSymbol || IsStableRead(receiver);

    private IrTypeRef ExpressionType(ExpressionSyntax expression) =>
        _semantic.GetTypeInfo(expression).Type is { } type
            ? IrTypeRefMapper.Map(type, _originResolver, _target)
            : new IrUnknownTypeRef();

    /// <summary>
    /// One label of a <c>switch</c> statement section, in source order. A
    /// <c>case</c> whose value names a type (<c>case Circle:</c>) is a type
    /// pattern, as in a switch-expression arm.
    /// </summary>
    internal IrCaseLabel ExtractCaseLabel(SwitchLabelSyntax label) =>
        label switch
        {
            CaseSwitchLabelSyntax constant when NamedPatternType(constant.Value) is { } type =>
                new IrPatternCaseLabel(ExtractCaseTypePattern(constant, type)),
            CaseSwitchLabelSyntax constant => WithConstantFacts(constant),
            CasePatternSwitchLabelSyntax pattern => new IrPatternCaseLabel(
                ExtractPattern(pattern.Pattern),
                pattern.WhenClause is null ? null : Extract(pattern.WhenClause.Condition)
            ),
            _ => new IrDefaultCaseLabel(),
        };

    /// <summary>
    /// <c>case Circle:</c> as a type pattern, classified with the static type
    /// of the switch subject (the label's value is a type name, which has no
    /// pattern operation of its own to report it).
    /// </summary>
    private IrPattern ExtractCaseTypePattern(CaseSwitchLabelSyntax label, IrTypeRef type)
    {
        var pattern = ExtractTypePattern(label.Value, type);

        return pattern is IrTypePattern typePattern && SwitchSubjectType(label) is { } subjectType
            ? typePattern with
            {
                InputType = IrTypeRefMapper.Map(subjectType, _originResolver, _target),
            }
            : pattern;
    }

    /// <summary>The static type of the subject of the switch statement a label belongs to.</summary>
    private ITypeSymbol? SwitchSubjectType(CaseSwitchLabelSyntax label) =>
        (label.Parent?.Parent as SwitchStatementSyntax)?.Expression is { } subject
            ? _semantic.GetTypeInfo(subject).Type
            : null;

    private static bool IsStableSymbol(ISymbol? symbol) =>
        symbol is ILocalSymbol or IParameterSymbol || IsStorageMember(symbol);

    /// <summary>
    /// A field or a property whose read runs no user code (see
    /// <see cref="IrMemberAccess.IsStorageRead"/>).
    /// </summary>
    private static bool IsStorageMember(ISymbol? symbol) =>
        symbol switch
        {
            IFieldSymbol => true,
            IPropertySymbol property => !IsComputed(property),
            _ => false,
        };

    /// <summary>
    /// A property whose getter may run code: every property but a
    /// non-overridable auto-property of this compilation (a hand-written,
    /// <c>field</c>-keyword, overridable or interface getter, an indexer, a
    /// property of another assembly).
    /// </summary>
    private static bool IsComputed(IPropertySymbol property) =>
        !IsAutoProperty(property) || IsOverridable(property);

    /// <summary>
    /// A property with a compiler-generated backing field and a body-less
    /// getter. The body check rules out a C# 14 <c>field</c>-keyword getter,
    /// which has a backing field too but may compute.
    /// </summary>
    private static bool IsAutoProperty(IPropertySymbol property) =>
        IsDeclaredInSource(property)
        && HasBackingField(property)
        && property.GetMethod is { } getter
        && !AccessorBodies.HasBody(getter);

    /// <summary>
    /// A property of another assembly may become a computing getter in a later
    /// version of that assembly, so only properties declared in this
    /// compilation's source can be storage reads.
    /// </summary>
    private static bool IsDeclaredInSource(IPropertySymbol property) =>
        !property.DeclaringSyntaxReferences.IsEmpty;

    private static bool HasBackingField(IPropertySymbol property) =>
        property
            .ContainingType.GetMembers()
            .OfType<IFieldSymbol>()
            .Any(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property));

    /// <summary>A derived type may override the getter with one that computes.</summary>
    private static bool IsOverridable(IPropertySymbol property) =>
        property.IsVirtual
        || property.IsAbstract
        || property is { IsOverride: true, IsSealed: false, ContainingType.IsSealed: false };
}
