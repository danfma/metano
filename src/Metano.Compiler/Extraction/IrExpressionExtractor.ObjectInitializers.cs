using System.Diagnostics.CodeAnalysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Lowers the initializer of an object creation — an object initializer
/// (<c>new T { X = x, Y = { Z = z }, [k] = v }</c>) or a collection
/// initializer (<c>new List&lt;int&gt; { 1, 2 }</c>,
/// <c>new Dictionary&lt;K, V&gt; { { k, v } }</c>) — into the operations C#
/// performs, in C# order: the construction (its arguments first), then each
/// member assignment, index assignment and <c>Add</c> call against the new
/// instance, each value evaluated once. The instance is let-bound and the
/// operations form an <see cref="IrSequenceExpression"/> that yields it, so the
/// whole creation stays an expression and every write goes through the
/// ordinary member, indexer and method lowerings (setters run, declarative
/// <c>Add</c> mappings apply).
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <c>creation</c> followed by the operations of <paramref name="initializer"/>
    /// against the created instance, or <paramref name="creation"/> itself when
    /// there is no (or an empty) initializer.
    /// </summary>
    private IrExpression ApplyInitializer(
        IrExpression creation,
        ITypeSymbol? createdType,
        InitializerExpressionSyntax? initializer
    )
    {
        if (initializer is null)
            return creation;

        if (initializer.Expressions.Count == 0)
            return creation;

        // The shared temp counter gives every bound instance a fresh name, so a
        // nested creation's binding never shadows the enclosing one.
        var instanceName = NextReceiverTempName();
        var instance = new IrIdentifier(instanceName);
        var effects = InitializerEffects(instance, createdType, initializer);

        return new IrLetExpression(
            instanceName,
            creation,
            new IrSequenceExpression(effects, instance)
        );
    }

    /// <summary>
    /// The operations of <paramref name="initializer"/> applied to
    /// <paramref name="receiver"/>, in source order. A nested initializer
    /// (<c>Y = { Z = z }</c>) applies to the member it names: C# reads the
    /// member again for each nested operation, as the receiver chain does here.
    /// </summary>
    private List<IrExpression> InitializerEffects(
        IrExpression receiver,
        ITypeSymbol? receiverType,
        InitializerExpressionSyntax initializer
    ) =>
        initializer.IsKind(SyntaxKind.ObjectInitializerExpression)
            ?
            [
                .. initializer.Expressions.SelectMany(element =>
                    ObjectInitializerEffects(receiver, receiverType, element)
                ),
            ]
            :
            [
                .. initializer.Expressions.Select(element =>
                    CollectionInitializerAdd(receiver, element)
                ),
            ];

    private IEnumerable<IrExpression> ObjectInitializerEffects(
        IrExpression receiver,
        ITypeSymbol? receiverType,
        ExpressionSyntax element
    ) =>
        element switch
        {
            AssignmentExpressionSyntax { Left: IdentifierNameSyntax member } assignment =>
                MemberInitializerEffects(receiver, member, assignment.Right),
            AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax index } assignment =>
                IndexInitializerEffects(receiver, receiverType, index, assignment.Right),
            _ => [ReportUnsupported(element, element.Kind().ToString())],
        };

    /// <summary>
    /// <c>X = value</c> assigns the member; <c>X = { … }</c> applies the nested
    /// initializer to the member's current value without replacing it. A C# 14
    /// extension property is written through its <c>$set</c> helper. A
    /// custom <c>init</c> accessor body is written through the setter it lowers
    /// to; one that is not transpiled (see <see cref="CustomInitAccessor"/>)
    /// leaves the property without a setter, so writing it is reported as
    /// unsupported, as is a nested initializer on an extension property.
    /// </summary>
    private IEnumerable<IrExpression> MemberInitializerEffects(
        IrExpression receiver,
        IdentifierNameSyntax member,
        ExpressionSyntax value
    )
    {
        var symbol = _semantic.GetSymbolInfo(member).Symbol;

        if (
            symbol is IPropertySymbol initProperty
            && CustomInitAccessor.HasUnsupportedBody(initProperty)
        )
            return
            [
                ReportUnsupportedInitializerElement(
                    member,
                    "a custom init accessor, which is not transpiled"
                ),
            ];

        if (
            symbol is IPropertySymbol property
            && TryResolveExtensionPropertyLowering(property, member) is { } extension
        )
            return value is InitializerExpressionSyntax
                ? [ReportUnsupportedInitializerElement(member, "an extension property")]
                : [ExtensionPropertyWrite(receiver, property, extension, value)];

        var target = new IrMemberAccess(receiver, member.Identifier.ValueText, BuildOrigin(symbol));

        if (value is InitializerExpressionSyntax nested)
            return InitializerEffects(target, MemberType(symbol), nested);

        return [new IrBinaryExpression(target, IrBinaryOp.Assign, Extract(value))];
    }

    /// <summary><c>prop$set(receiver, value)</c>, as a plain extension-property assignment lowers.</summary>
    private IrExpression ExtensionPropertyWrite(
        IrExpression receiver,
        IPropertySymbol property,
        ExtensionPropertyLowering extension,
        ExpressionSyntax value
    ) =>
        BuildExtensionHelperCall(
            extension.HelperContainer,
            property.Name + IrExtensionConventions.PropertySetterSuffix,
            ResolvePropertySetterEmittedName(property),
            [new IrArgument(receiver), new IrArgument(Extract(value))],
            typeArguments: null
        );

    /// <summary>
    /// <c>[key] = value</c> assigns through the indexer; <c>[key] = { … }</c>
    /// applies the nested initializer to the element, with the key evaluated
    /// once (a nested value may change what a variable key reads) and the
    /// element read once per nested operation, as C# does. Multi-index and C# 14 extension indexers are reported as
    /// unsupported.
    /// </summary>
    private IEnumerable<IrExpression> IndexInitializerEffects(
        IrExpression receiver,
        ITypeSymbol? receiverType,
        ImplicitElementAccessSyntax index,
        ExpressionSyntax value
    )
    {
        var indexer = _semantic.GetSymbolInfo(index).Symbol as IPropertySymbol;

        if (index.ArgumentList.Arguments.Count != 1)
            return [ReportUnsupportedInitializerElement(index, "more than one index")];

        if (indexer is not null && indexer.IsExtensionBlockMember())
            return [ReportUnsupportedInitializerElement(index, "an extension indexer")];

        var key = Extract(index.ArgumentList.Arguments[0].Expression);
        var receiverTypeRef = receiverType is null ? null : MapNonNullType(receiverType);

        if (value is not InitializerExpressionSyntax nested)
            return
            [
                new IrBinaryExpression(
                    new IrElementAccess(receiver, key, receiverTypeRef),
                    IrBinaryOp.Assign,
                    Extract(value)
                ),
            ];

        List<IrExpression> NestedEffects(IrExpression boundKey) =>
            InitializerEffects(
                new IrElementAccess(receiver, boundKey, receiverTypeRef),
                indexer?.Type,
                nested
            );

        if (key is IrLiteral)
            return NestedEffects(key);

        var keyName = NextReceiverTempName();
        var boundKey = new IrIdentifier(keyName);

        return
        [
            new IrLetExpression(
                keyName,
                key,
                new IrSequenceExpression(NestedEffects(boundKey), boundKey)
            ),
        ];
    }

    /// <summary>
    /// One collection-initializer element: <c>x</c> calls <c>Add(x)</c> and
    /// <c>{ k, v }</c> calls <c>Add(k, v)</c> on the receiver, through the
    /// <c>Add</c> overload Roslyn bound. An element bound to an extension or
    /// call-site-inlined <c>Add</c>, or to no <c>Add</c> at all (a
    /// <c>dynamic</c> receiver), is reported as unsupported.
    /// </summary>
    private IrExpression CollectionInitializerAdd(IrExpression receiver, ExpressionSyntax element)
    {
        var add = _semantic.GetCollectionInitializerSymbolInfo(element).Symbol as IMethodSymbol;

        if (!IsInstanceAdd(add))
            return ReportUnsupported(
                element,
                element.Kind().ToString(),
                $"Collection initializer element '{element}' does not bind to an instance 'Add' method the transpiler supports."
            );

        var origin = BuildOrigin(add);

        return new IrCallExpression(
            new IrMemberAccess(receiver, add.Name, origin),
            AddArgumentsOf(element, add),
            Origin: origin
        );
    }

    /// <summary>
    /// The lowered <c>Add</c> arguments, the trailing one marked as a spread
    /// when it passes an array straight into a <c>params</c> parameter
    /// (normal form) rather than as discrete elements.
    /// </summary>
    private List<IrArgument> AddArgumentsOf(ExpressionSyntax element, IMethodSymbol add)
    {
        var values = AddArguments(element);
        var arguments = values.Select(value => new IrArgument(Extract(value))).ToList();

        if (PassesParamsArrayDirectly(values, add))
            arguments[^1] = NormalizeNullParamsArray(
                arguments[^1] with
                {
                    IsSpread = true,
                },
                values[^1],
                add
            );

        return arguments;
    }

    private bool PassesParamsArrayDirectly(
        IReadOnlyList<ExpressionSyntax> values,
        IMethodSymbol add
    ) =>
        add.Parameters is [.., { IsParams: true } paramsParameter]
        && values.Count == add.Parameters.Length
        && SymbolEqualityComparer.Default.Equals(
            _semantic.GetTypeInfo(values[^1]).ConvertedType,
            paramsParameter.Type
        );

    private IrExpression ReportUnsupportedInitializerElement(
        ExpressionSyntax element,
        string reason
    ) =>
        ReportUnsupported(
            element,
            element.Kind().ToString(),
            $"Initializer element '{element.Parent ?? element}' targets {reason}; it is not supported by the transpiler."
        );

    private IrTypeRef MapNonNullType(ITypeSymbol type) =>
        IrTypeRefMapper.Map(
            type.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
            _originResolver,
            _target
        );

    /// <summary>
    /// Whether the object initializer stays on the <see cref="IrNewExpression"/>
    /// as <see cref="IrNewExpression.Initializers"/> instead of lowering to
    /// operations against the instance (<see cref="ApplyInitializer"/>): a
    /// JSX-renderable construction reads it as element attributes and
    /// children.
    /// </summary>
    private static bool RendersInitializerAsJsx(
        IrTypeRef type,
        InitializerExpressionSyntax? initializer
    ) => initializer is not null && type is IrNamedTypeRef { Semantics.RendersAsJsxElement: true };

    private static InitializerExpressionSyntax? InitializerOf(ExpressionSyntax creationSyntax) =>
        (creationSyntax as BaseObjectCreationExpressionSyntax)?.Initializer;

    /// <summary>
    /// The <c>Add</c> arguments of a collection-initializer element: the
    /// expressions of a <c>{ k, v }</c> element, or the element itself.
    /// </summary>
    private static IReadOnlyList<ExpressionSyntax> AddArguments(ExpressionSyntax element) =>
        element is InitializerExpressionSyntax complex
        && complex.IsKind(SyntaxKind.ComplexElementInitializerExpression)
            ? complex.Expressions
            : [element];

    /// <summary>
    /// An ordinary instance <c>Add</c>: not an extension, and not an
    /// <c>[Emit]</c> / <c>[Import]</c> / <c>[Inline]</c> method, whose call
    /// sites need the invocation rewriters a synthesized call bypasses.
    /// </summary>
    private static bool IsInstanceAdd([NotNullWhen(true)] IMethodSymbol? add) =>
        add is { IsExtensionMethod: false, ReducedFrom: null } && !add.IsInlinedAtCallSite();

    private static ITypeSymbol? MemberType(ISymbol? member) =>
        member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };
}
