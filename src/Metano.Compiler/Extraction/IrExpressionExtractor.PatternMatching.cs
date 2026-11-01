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
    /// meaning, and the rest of the pattern reads the captured local. (A
    /// pattern that binds nothing can evaluate its subject once within the
    /// expression itself, which backends do on their own.)
    /// </summary>
    private IrIsPatternExpression ExtractIsPattern(IsPatternExpressionSyntax isPattern)
    {
        var subject = Extract(isPattern.Expression);
        var pattern = ExtractPattern(isPattern.Pattern);

        if (
            !IrPatternDesignators.Declares(pattern)
            || IsStableSubject(isPattern.Expression, subject)
        )
            return new IrIsPatternExpression(subject, pattern);

        var capture = new IrVarPattern(
            new IrPatternDesignator(
                $"$subject{_patternSubjectCounter++}",
                ExpressionType(isPattern.Expression)
            )
        );

        return new IrIsPatternExpression(
            subject,
            new IrLogicalPattern(IrLogicalOp.And, capture, pattern)
        );
    }

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
                new IrPatternCaseLabel(ExtractTypePattern(constant.Value, type)),
            CaseSwitchLabelSyntax constant => new IrConstantCaseLabel(Extract(constant.Value)),
            CasePatternSwitchLabelSyntax pattern => new IrPatternCaseLabel(
                ExtractPattern(pattern.Pattern),
                pattern.WhenClause is null ? null : Extract(pattern.WhenClause.Condition)
            ),
            _ => new IrDefaultCaseLabel(),
        };

    private static bool IsStableSymbol(ISymbol? symbol) =>
        symbol switch
        {
            ILocalSymbol or IParameterSymbol or IFieldSymbol => true,
            IPropertySymbol property => IsAutoProperty(property) && !IsOverridable(property),
            _ => false,
        };

    /// <summary>
    /// A property with a compiler-generated backing field and a body-less
    /// getter. The body check rules out a C# 14 <c>field</c>-keyword getter,
    /// which has a backing field too but may compute.
    /// </summary>
    private static bool IsAutoProperty(IPropertySymbol property) =>
        HasBackingField(property)
        && property.GetMethod is { } getter
        && !AccessorBodies.HasBody(getter);

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
