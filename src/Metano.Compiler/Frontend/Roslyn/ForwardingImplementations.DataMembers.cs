using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// The property and event forwarders (#822): <c>int ICounter.Count =&gt; Count;</c>,
/// <c>object? IStrongBox.Value { get =&gt; Value; set =&gt; Value = value; }</c> and
/// <c>event EventHandler? INotify.Changed { add =&gt; Changed += value; remove =&gt;
/// Changed -= value; }</c>. Every accessor is one access of the claiming member on
/// <c>this</c> that passes the accessor's own <c>value</c>.
/// </summary>
internal static partial class ForwardingImplementations
{
    private static bool IsPurePropertyForwarder(IPropertySymbol property, IPropertySymbol target) =>
        !property.IsIndexer
        && !target.IsIndexer
        && (property.GetMethod is null || IsSameProperty(ReadPropertyOf(property), target))
        && (property.SetMethod is null || WritesProperty(property, target));

    private static bool IsPureEventForwarder(IEventSymbol evt, IEventSymbol target) =>
        IsSameEvent(AddedEventOf(evt), target) && RemovesEvent(evt, target);

    private static IPropertySymbol? ReadPropertyOf(IPropertySymbol property) =>
        GetterExpressionOf(property) is { } expression
        && OperationOf(property, expression)
            is IPropertyReferenceOperation { Property: var read, Instance: var instance }
        && IsOnThis(instance)
            ? read
            : null;

    private static IEventSymbol? AddedEventOf(IEventSymbol evt) =>
        EventAccessorOperationOf(evt, SyntaxKind.AddAccessorDeclaration)
            is { Adds: true, EventReference: IEventReferenceOperation reference } operation
        && IsOnThis(reference.Instance)
        && IsAccessorValue(operation.HandlerValue, evt.AddMethod)
            ? reference.Event
            : null;

    private static bool WritesProperty(IPropertySymbol property, IPropertySymbol target) =>
        AccessorExpressionOf(property, SetterKindOf(property)) is { } expression
        && OperationOf(property, expression)
            is ISimpleAssignmentOperation
            {
                Target: IPropertyReferenceOperation
                {
                    Property: var written,
                    Instance: var instance
                },
                Value: var value,
            }
        && IsOnThis(instance)
        && IsSameProperty(written, target)
        && IsAccessorValue(value, property.SetMethod);

    private static bool RemovesEvent(IEventSymbol evt, IEventSymbol target) =>
        EventAccessorOperationOf(evt, SyntaxKind.RemoveAccessorDeclaration)
            is { Adds: false, EventReference: IEventReferenceOperation reference } operation
        && IsOnThis(reference.Instance)
        && IsSameEvent(reference.Event, target)
        && IsAccessorValue(operation.HandlerValue, evt.RemoveMethod);

    private static IEventAssignmentOperation? EventAccessorOperationOf(
        IEventSymbol evt,
        SyntaxKind kind
    ) =>
        AccessorExpressionOf(evt, kind) is { } expression
            ? OperationOf(evt, expression) as IEventAssignmentOperation
            : null;

    private static ExpressionSyntax? GetterExpressionOf(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences is [var reference]
        && reference.GetSyntax() is PropertyDeclarationSyntax { ExpressionBody: { } arrow }
            ? arrow.Expression
            : AccessorExpressionOf(property, SyntaxKind.GetAccessorDeclaration);

    private static SyntaxKind SetterKindOf(IPropertySymbol property) =>
        property.SetMethod?.IsInitOnly == true
            ? SyntaxKind.InitAccessorDeclaration
            : SyntaxKind.SetAccessorDeclaration;

    /// <summary>The one expression the accessor of <paramref name="kind"/> evaluates.</summary>
    private static ExpressionSyntax? AccessorExpressionOf(ISymbol member, SyntaxKind kind) =>
        member.DeclaringSyntaxReferences is [var reference]
        && reference.GetSyntax() is BasePropertyDeclarationSyntax { AccessorList: { } accessors }
        && accessors.Accessors.FirstOrDefault(accessor => accessor.IsKind(kind)) is { } accessor
            ? SoleExpressionOf(accessor.ExpressionBody, accessor.Body)
            : null;

    private static bool IsAccessorValue(IOperation value, IMethodSymbol? accessor) =>
        value is IParameterReferenceOperation { Parameter: var parameter }
        && accessor?.Parameters is [.., var implicitValue]
        && SymbolEqualityComparer.Default.Equals(parameter, implicitValue);

    private static bool IsSameProperty(IPropertySymbol? property, IPropertySymbol target) =>
        SymbolEqualityComparer.Default.Equals(
            property?.OriginalDefinition,
            target.OriginalDefinition
        );

    private static bool IsSameEvent(IEventSymbol? evt, IEventSymbol target) =>
        SymbolEqualityComparer.Default.Equals(evt?.OriginalDefinition, target.OriginalDefinition);
}
