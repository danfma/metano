using Metano.Annotations;
using Metano.Compiler.Analysis;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Extracts a Roslyn <see cref="ExpressionSyntax"/> into a semantic
/// <see cref="IrExpression"/>. The extractor covers the foundational subset —
/// literals, identifiers, <c>this</c>/<c>base</c>, binary/unary operators, member
/// access, invocation, object creation, assignment, conditionals. Features that
/// require specialized handling (patterns, LINQ, lambdas, string interpolation)
/// are emitted as <see cref="IrUnsupportedExpression"/> placeholders until later
/// phases add explicit support — this keeps the API total while letting callers
/// detect and fall back to legacy handling.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>The <c>not null</c> pattern a nullable relational pattern implies.</summary>
    private static readonly IrPattern NotNullPattern = new IrLogicalPattern(
        IrLogicalOp.Not,
        new IrConstantPattern(new IrLiteral(null, IrLiteralKind.Null)),
        null
    );

    private readonly SemanticModel _semantic;
    private readonly IrTypeOriginResolver? _originResolver;
    private readonly Metano.Annotations.TargetLanguage? _target;

    /// <summary>
    /// Tracks the set of <c>[Inline]</c> members currently being
    /// expanded so cyclic references (<c>[Inline] A =&gt; B</c>,
    /// <c>[Inline] B =&gt; A</c>) bail out rather than recurse
    /// indefinitely. Shared across nested extractors spawned during
    /// an <c>[Inline]</c> expansion so the cycle set survives the
    /// jump to the initializer's semantic model — an earlier
    /// iteration created a fresh set per nested extractor, which
    /// defeated the guard.
    /// </summary>
    private readonly HashSet<ISymbol> _inlineExpanding;

    private static readonly AsyncLocal<PrimaryCtorExtractionScope?> _primaryCtorScope = new();

    /// <summary>
    /// The primary-constructor facts of the type whose members are being
    /// extracted: the captured parameters whose reads are rewritten to
    /// <c>this._field</c> (so the emitted code goes through the synthesized
    /// field instead of an out-of-scope parameter binding), and whether the
    /// initializers run in the constructor body. Stored in an
    /// <see cref="AsyncLocal{T}"/> to scope it to a single type's member-body
    /// extraction without threading an extra parameter through every
    /// extractor constructor; tests run concurrently each see their own
    /// value. <c>null</c> outside that scope.
    /// </summary>
    internal static PrimaryCtorExtractionScope? PrimaryCtorScope
    {
        get => _primaryCtorScope.Value;
        set => _primaryCtorScope.Value = value;
    }

    /// <summary>
    /// When an <c>[Inline]</c> method is being expanded, the outer
    /// extractor binds each of the method's parameter symbols to the
    /// caller's argument expressions here. Identifier resolution
    /// checks this map first so parameter uses inside the body
    /// substitute to the caller's IR instead of re-resolving to the
    /// (uninvokable) parameter symbol.
    /// </summary>
    private readonly IReadOnlyDictionary<ISymbol, IrExpression>? _inlineParameterSubs;

    /// <summary>
    /// Counter for the synthesized <see cref="Metano.Compiler.IR.IrLetExpression"/>
    /// temporaries of one body (<c>receiver$temp$N</c>, <c>tuple$temp$N</c>).
    /// Every kind draws from it, so each nested binding gets a unique suffix
    /// and an inner one cannot shadow an outer one.
    /// </summary>
    private int _temporaryCounter;

    /// <summary>
    /// Counter for the synthesized <c>$subjectN</c> locals that capture an
    /// <c>is</c> subject evaluated once (see <c>ExtractIsPattern</c>).
    /// <c>$</c> keeps them clear of every C# identifier.
    /// </summary>
    private int _patternSubjectCounter;

    /// <summary>
    /// The receiver syntax of the null-conditional access whose steps
    /// <see cref="LowerConditionalContinuation"/> is extracting; <c>null</c>
    /// outside one. Kept as extractor state rather than threaded through
    /// <see cref="Extract"/> because every receiver-aware lowering reaches
    /// the receiver by extracting its syntax: <see cref="Extract"/> answers
    /// that syntax with an <see cref="IrConditionalReceiver"/>, so the
    /// receiver is never re-extracted.
    /// </summary>
    private ExpressionSyntax? _conditionalReceiverSyntax;

    private string NextReceiverTempName() => $"receiver$temp${_temporaryCounter++}";

    /// <summary>A fresh temporary holding one operand value of a tuple comparison.</summary>
    private string NextTupleOperandTempName() => $"tuple$temp${_temporaryCounter++}";

    public IrExpressionExtractor(
        SemanticModel semanticModel,
        IrTypeOriginResolver? originResolver = null,
        Metano.Annotations.TargetLanguage? target = null
    )
        : this(
            semanticModel,
            originResolver,
            target,
            inlineExpanding: null,
            inlineParameterSubs: null
        ) { }

    internal IrExpressionExtractor(
        SemanticModel semanticModel,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target,
        HashSet<ISymbol>? inlineExpanding,
        IReadOnlyDictionary<ISymbol, IrExpression>? inlineParameterSubs
    )
    {
        _semantic = semanticModel;
        _originResolver = originResolver;
        _target = target;
        _inlineExpanding = inlineExpanding ?? new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        _inlineParameterSubs = inlineParameterSubs;

        // Invocation rewriter chain (#221 — invocation rewriter
        // extraction). Order is load-bearing — see each rewriter's
        // class doc for the priority tier the entry occupies.
        //
        // Pre-pass concerns (LINQ chain detection, queryable-trigger
        // diagnostics) stay above the chain in ExtractInvocation
        // because they don't share the "match-or-decline" contract.
        //
        // [Emit] templates, [Import] facades, [Inline] expansion,
        // and extension-method lowering stay BELOW the chain in the
        // inline cascade for now. They each own mutable state
        // (inline-expansion recursion guard, attribute-driven facade
        // builders) that's still threaded through extractor fields;
        // migrating them into rewriters needs that state ownership
        // sorted first and lands in later PRs of #221.
        var helpers = new Invocation.InvocationLoweringHelpers(
            ApplyParamsSpread: ApplyParamsSpread,
            BuildOrigin: BuildOrigin,
            NormalizeArguments: NormalizeArguments,
            MapTypeRef: t => IrTypeRefMapper.Map(t, _originResolver, _target),
            Target: _target
        );
        _invocationRewriters = new Invocation.IInvocationRewriter[]
        {
            new Invocation.DelegateInvokeRewriter(helpers),
            new Invocation.DictionaryKeysContainsRewriter(helpers),
            new Invocation.IntrinsicBclLoweringTable(semanticModel.Compilation, helpers),
            new Invocation.EmitOrImportRewriter(helpers),
            // Bespoke callback (not via helpers) — inline expansion is tied
            // to the extractor's mutable recursion-guard state; see the
            // rewriter's class doc for the ownership rationale.
            new Invocation.InlineMethodExpansionRewriter(TryExpandInlineMethod),
            // Bespoke callback — extension-call resolution shares helpers
            // (`IsTranspilableExtensionContainer`, `BuildExtensionHelperCall`)
            // with the property-access and assignment paths; threading
            // those through the rewriter façade would touch 5+ call sites
            // for one rewriter's benefit.
            new Invocation.ExtensionMethodRewriter(TryRewriteExtensionCall),
        };
    }

    private readonly Invocation.IInvocationRewriter[] _invocationRewriters;

    internal HashSet<ISymbol> InlineExpandingSet => _inlineExpanding;

    public IrExpression Extract(ExpressionSyntax expression) =>
        _conditionalReceiverSyntax is not null && expression == _conditionalReceiverSyntax
            ? new IrConditionalReceiver()
            : ExtractUnbound(expression);

    private IrExpression ExtractUnbound(ExpressionSyntax expression) =>
        expression switch
        {
            LiteralExpressionSyntax lit => ExtractLiteral(lit),
            IdentifierNameSyntax id => ExtractIdentifierName(id),
            GenericNameSyntax generic => ExtractGenericName(generic),
            ThisExpressionSyntax => new IrThisExpression(),
            BaseExpressionSyntax => new IrBaseExpression(),
            ParenthesizedExpressionSyntax paren => Extract(paren.Expression),
            BinaryExpressionSyntax bin => ExtractBinary(bin),
            AssignmentExpressionSyntax assign => ExtractAssignment(assign),
            MemberAccessExpressionSyntax member => ExtractMemberAccess(
                QualifiedMemberSyntax.Of(member)
            ),
            InvocationExpressionSyntax inv => ExtractInvocation(inv),
            ObjectCreationExpressionSyntax oc => ExtractObjectCreation(oc),
            ImplicitObjectCreationExpressionSyntax ioc => ExtractImplicitObjectCreation(ioc),
            ConditionalExpressionSyntax cond => new IrConditionalExpression(
                Extract(cond.Condition),
                Extract(cond.WhenTrue),
                Extract(cond.WhenFalse)
            ),
            PrefixUnaryExpressionSyntax pre => ExtractUnary(pre, isPrefix: true),
            PostfixUnaryExpressionSyntax post => ExtractUnary(post, isPrefix: false),
            CastExpressionSyntax cast => ExtractCast(cast),
            AwaitExpressionSyntax aw => new IrAwaitExpression(Extract(aw.Expression)),
            ThrowExpressionSyntax throwExpr => new IrThrowExpression(Extract(throwExpr.Expression)),
            ElementAccessExpressionSyntax elem => ExtractElementAccess(elem),
            InterpolatedStringExpressionSyntax interp => ExtractInterpolatedString(interp),
            SimpleLambdaExpressionSyntax simpleLambda => ExtractSimpleLambda(simpleLambda),
            ParenthesizedLambdaExpressionSyntax parenLambda => ExtractParenthesizedLambda(
                parenLambda
            ),
            IsPatternExpressionSyntax isPattern => ExtractIsPattern(isPattern),
            SwitchExpressionSyntax switchExpr => ExtractSwitchExpression(switchExpr),
            WithExpressionSyntax withExpr => ExtractWithExpression(withExpr),
            CollectionExpressionSyntax coll => ExtractCollectionExpression(coll),
            TupleExpressionSyntax tuple => ExtractTupleLiteral(tuple),
            ConditionalAccessExpressionSyntax cond => ExtractConditionalAccess(cond),
            MemberBindingExpressionSyntax binding
                when _conditionalReceiverSyntax is not null
                    && QualifiedMemberSyntax.From(binding) is { } boundMember =>
                ExtractMemberAccess(boundMember),
            ElementBindingExpressionSyntax elementBinding
                when _conditionalReceiverSyntax is not null
                    && QualifiedMemberSyntax.ConditionalReceiverOf(elementBinding)
                        is { } receiver => ExtractElementRead(
                elementBinding,
                receiver,
                elementBinding.ArgumentList
            ),
            ArrayCreationExpressionSyntax arr when arr.Initializer is not null =>
                new IrArrayLiteral(arr.Initializer.Expressions.Select(Extract).ToList()),
            ImplicitArrayCreationExpressionSyntax iarr => new IrArrayLiteral(
                iarr.Initializer.Expressions.Select(Extract).ToList()
            ),
            // C# language keywords used as expressions — `string.Concat(…)`,
            // `int.Parse(…)`, etc. Resolve to the underlying BCL type so the
            // member-access lookup that wraps the expression can route to the
            // correct declarative mapping (e.g. `System.String`).
            PredefinedTypeSyntax pred => ExtractPredefinedType(pred),
            _ => ReportUnsupported(expression, expression.Kind().ToString()),
        };

    /// <summary>
    /// Reports MS0001 (UnsupportedFeature) at <paramref name="node"/> and
    /// returns the <see cref="IrUnsupportedExpression"/> placeholder the
    /// backends render as a parseable expression that throws at runtime, so
    /// one unsupported construct never aborts the whole build.
    /// </summary>
    private static IrUnsupportedExpression ReportUnsupported(
        SyntaxNode node,
        string kind,
        string? message = null
    )
    {
        ReportUnsupportedFeature(
            node,
            message ?? $"Expression '{node}' ({kind}) is not supported by the transpiler."
        );

        return new IrUnsupportedExpression(kind);
    }

    private static void ReportUnsupportedFeature(SyntaxNode node, string message) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                message,
                node.GetLocation()
            )
        );

    /// <summary>
    /// Lowers a C# <c>with</c> expression (<c>source with { X = expr }</c>)
    /// into an <see cref="IrWithExpression"/>. Each
    /// <see cref="AssignmentExpressionSyntax"/> inside the initializer becomes
    /// an <see cref="IrWithAssignment"/> with the left-hand identifier as the
    /// member name and the right-hand expression as the new value.
    /// </summary>
    /// <summary>
    /// `[1, 2, 3]` is a C# 12 collection expression — its concrete runtime
    /// type is decided by the conversion context (target type). When the
    /// target is `HashSet<T>` / `ISet<T>` we lower to `new HashSet()` so the
    /// runtime container has add/has semantics; for `Map<K,V>`-shaped
    /// targets we'd need entries which the spread/element shape doesn't
    /// carry yet, so those still surface as a plain array literal. Plain
    /// `List<T>` / arrays / `IEnumerable<T>` keep the array literal — JS
    /// arrays satisfy that interface natively.
    /// </summary>
    /// <summary>
    /// Lowers a null-conditional access (<c>a?.B</c>, <c>a?.B(x).C</c>,
    /// <c>a?[i]</c>, <c>a?.B?.C</c>, <c>a?.B = c</c>). Every step after the
    /// <c>?.</c> goes through <see cref="LowerConditionalContinuation"/>;
    /// a delegate <c>?.Invoke(args)</c> and an assignment keep dedicated
    /// shapes.
    /// </summary>
    private IrExpression ExtractConditionalAccess(ConditionalAccessExpressionSyntax cond)
    {
        var target = Extract(cond.Expression);
        return LowerConditionalBinding(target, cond.Expression, cond.WhenNotNull);
    }

    private IrExpression LowerConditionalBinding(
        IrExpression receiver,
        ExpressionSyntax receiverSyntax,
        ExpressionSyntax whenNotNull
    )
    {
        switch (whenNotNull)
        {
            case InvocationExpressionSyntax inv
                when inv.Expression is MemberBindingExpressionSyntax
                    && TryLowerOptionalDelegateInvoke(receiver, inv) is { } delegateCall:
                return delegateCall;

            case MemberBindingExpressionSyntax
            or ElementBindingExpressionSyntax
            or MemberAccessExpressionSyntax
            or InvocationExpressionSyntax
            or ElementAccessExpressionSyntax:
                return LowerConditionalContinuation(receiver, receiverSyntax, whenNotNull);

            case ConditionalAccessExpressionSyntax nested:
                var head = LowerConditionalBinding(receiver, receiverSyntax, nested.Expression);

                return LowerConditionalBinding(head, nested.Expression, nested.WhenNotNull);

            case AssignmentExpressionSyntax assign
                when TryLowerNullConditionalAssignment(receiver, assign) is { } shortCircuit:
                return shortCircuit;

            default:
                return ReportUnsupported(whenNotNull, $"ConditionalAccess({whenNotNull.Kind()})");
        }
    }

    /// <summary>
    /// <c>handler?.Invoke(args)</c> lowers to a delegate optional call. The
    /// <c>.Invoke</c> member binding has no runtime counterpart in JS/TS —
    /// the delegate is the function — so the IR drops the binding name and
    /// marks the call as optional. The Dart bridge re-introduces the
    /// explicit <c>.call</c> segment because Dart's optional-call shape is
    /// <c>receiver?.call(...)</c>. <c>[This]</c>-bearing delegates fall
    /// through: the first parameter rebinds JS <c>this</c>, so the binding
    /// must remain visible to whatever lowering wires the receiver argument
    /// into the runtime trampoline.
    /// </summary>
    private IrCallExpression? TryLowerOptionalDelegateInvoke(
        IrExpression receiver,
        InvocationExpressionSyntax inv
    )
    {
        if (
            _semantic.GetSymbolInfo(inv).Symbol is not IMethodSymbol method
            || !IsDelegateInvoke(method)
            || HasThisParameter(method)
        )
            return null;

        var args = inv.ArgumentList.Arguments.Select(ExtractArgument).ToList();
        ApplyParamsSpread(args, method, inv.ArgumentList.Arguments);

        return new IrCallExpression(receiver, args, Origin: BuildOrigin(method), IsOptional: true);
    }

    /// <summary>
    /// Lowers the steps after a <c>?.</c> (<c>a?.B</c>, <c>a?.B.C(x)</c>,
    /// <c>a?[0].B</c>) to an <see cref="IrNullConditionalAccess"/>. The
    /// steps go through the regular <see cref="Extract"/> pipeline with
    /// <paramref name="receiverSyntax"/> read as an
    /// <see cref="IrConditionalReceiver"/>, so every step — the leading one
    /// included — keeps its own lowering (extension rewrites, <c>[Emit]</c>,
    /// <c>[Inline]</c>, LINQ chains, type arguments, member origins).
    /// </summary>
    private IrExpression LowerConditionalContinuation(
        IrExpression receiver,
        ExpressionSyntax receiverSyntax,
        ExpressionSyntax whenNotNull
    )
    {
        var enclosingReceiverSyntax = _conditionalReceiverSyntax;
        _conditionalReceiverSyntax = receiverSyntax;

        try
        {
            return new IrNullConditionalAccess(receiver, Extract(whenNotNull));
        }
        finally
        {
            _conditionalReceiverSyntax = enclosingReceiverSyntax;
        }
    }

    /// <summary>
    /// Lowers <c>a?.b = c</c> (and nested forms like
    /// <c>a?.b.c = d</c> or <c>a?.items[0] = e</c>) into the
    /// short-circuit shape
    /// <c>a != null &amp;&amp; (a.b = c)</c> when the receiver shape
    /// supports duplicate evaluation. Returns <c>null</c> when the
    /// rewrite is unsafe — the caller falls through to
    /// <see cref="IrUnsupportedExpression"/> with the original
    /// diagnostic message so a future slice can broaden the
    /// supported surface without touching this path again.
    /// <para>
    /// Guards in place:
    /// </para>
    /// <list type="bullet">
    ///   <item>The lowering uses TypeScript's <c>&amp;&amp;</c> short-circuit
    ///   semantics (assignment expression evaluates to the value);
    ///   Dart requires a boolean RHS, so the rewrite only fires for
    ///   the TypeScript target (or when no target was specified —
    ///   matches the existing extractor convention).</item>
    ///   <item>The receiver is referenced twice in the emitted code
    ///   (null-check + member write). Side-effecting receivers
    ///   (method calls, property accesses with a getter, indexers)
    ///   would re-evaluate. Restrict the rewrite to receivers whose
    ///   IR shape is a chain of pure identifier / <c>this</c> /
    ///   member-access nodes; complex receivers fall through to
    ///   the unsupported path until the IR grows a let-binding
    ///   shape that can host the temp.</item>
    ///   <item>Event-style left-hand sides (<c>+=</c>/<c>-=</c> on
    ///   an <c>IEventSymbol</c>) need the runtime
    ///   <c>delegateAdd</c>/<c>delegateRemove</c> dispatch from
    ///   <see cref="ExtractAssignment"/>. Bail out so the
    ///   diagnostic surfaces instead of silently emitting a plain
    ///   compound assignment.</item>
    /// </list>
    /// </summary>
    private IrExpression? TryLowerNullConditionalAssignment(
        IrExpression receiver,
        AssignmentExpressionSyntax assign
    )
    {
        if (_target is not null && _target != Metano.Annotations.TargetLanguage.TypeScript)
            return null;

        if (!IrEvaluationTraits.IsReusableReceiver(receiver))
            return null;

        if (RebindAssignmentTarget(assign.Left, receiver) is not { } rebound)
            return null;
        // Reject event accessors — the dedicated `event += handler`
        // path in `ExtractAssignment` synthesizes
        // `delegateAdd`/`delegateRemove` calls that this lowering
        // does not reproduce.
        if (_semantic.GetSymbolInfo(assign.Left).Symbol is IEventSymbol)
            return null;

        // Null-conditional extension property write — the rebound LHS is
        // a member access whose symbol resolves to an extension property
        // with a setter. Build the helper call directly so the inner
        // assignment also benefits from the `$set` lowering; the
        // surrounding null check stays exactly as the legacy form.
        var memberWrite =
            TryBuildExtensionPropertyAssignmentForNullConditional(receiver, assign)
            ?? BuildAssignmentWithIntegerDivision(rebound, assign);
        return new IrBinaryExpression(
            new IrBinaryExpression(
                receiver,
                IrBinaryOp.NotEqual,
                new IrLiteral(null, IrLiteralKind.Null)
            ),
            IrBinaryOp.LogicalAnd,
            memberWrite
        );
    }

    /// <summary>
    /// Mirror of <see cref="BuildExtensionPropertyAssignment"/> for the
    /// inner write of a null-conditional assignment. The receiver was
    /// already validated to be side-effect free by
    /// <see cref="IrEvaluationTraits.IsReusableReceiver"/>, so we can pass it directly into the
    /// helper call without an additional let-binding. Returns
    /// <c>null</c> when the LHS isn't an extension property — the caller
    /// falls back to the legacy member-write shape.
    /// </summary>
    private IrExpression? TryBuildExtensionPropertyAssignmentForNullConditional(
        IrExpression receiver,
        AssignmentExpressionSyntax assign
    )
    {
        if (assign.Left is not MemberBindingExpressionSyntax mb)
            return null;
        if (_semantic.GetSymbolInfo(mb).Symbol is not IPropertySymbol prop)
            return null;
        // The conditional binding has no syntactic MemberAccessExpression
        // we can hand to `TryResolveExtensionPropertyLowering` — synthesize
        // the lookup from the property symbol directly.
        var setterLowering = TryResolveExtensionPropertyLoweringForSymbol(prop);
        if (setterLowering is null)
            return null;

        var setterName = prop.Name + IrExtensionConventions.PropertySetterSuffix;
        var emittedSetterName = ResolvePropertySetterEmittedName(prop);
        var getterName = setterLowering.Value.HelperName;
        var emittedGetterName = setterLowering.Value.EmittedName;
        var rhs = Extract(assign.Right);
        var compoundOp = MapCompoundAssignmentOperator(assign);

        IrExpression newValue;
        if (compoundOp is null)
        {
            newValue = rhs;
        }
        else
        {
            newValue = new IrBinaryExpression(
                BuildExtensionHelperCall(
                    setterLowering.Value.HelperContainer,
                    getterName,
                    emittedGetterName,
                    [new IrArgument(receiver)],
                    typeArguments: null
                ),
                compoundOp.Value,
                rhs
            );
        }

        return BuildExtensionHelperCall(
            setterLowering.Value.HelperContainer,
            setterName,
            emittedSetterName,
            [new IrArgument(receiver), new IrArgument(newValue)],
            typeArguments: null
        );
    }

    /// <summary>
    /// Resolves the property-getter lowering metadata for a property
    /// symbol without a syntactic <see cref="MemberAccessExpressionSyntax"/>
    /// receiver — needed for null-conditional writes where the LHS is a
    /// <see cref="MemberBindingExpressionSyntax"/>. Replicates the type
    /// guards from <see cref="TryResolveExtensionPropertyLowering"/>.
    /// </summary>
    private ExtensionPropertyLowering? TryResolveExtensionPropertyLoweringForSymbol(
        IPropertySymbol prop
    )
    {
        if (prop.IsIndexer)
            return null;
        var containing = prop.ContainingType;
        if (containing is null)
            return null;

        if (
            string.IsNullOrEmpty(containing.Name)
            && containing.ContainingType is { IsStatic: true } parentStatic
            && IsTranspilableExtensionContainer(parentStatic)
        )
        {
            return new ExtensionPropertyLowering(
                prop.Name + IrExtensionConventions.PropertyGetterSuffix,
                ResolvePropertyEmittedName(prop),
                parentStatic
            );
        }

        if (
            containing.IsStatic
            && prop.Parameters.Length > 0
            && IsTranspilableExtensionContainer(containing)
        )
        {
            return new ExtensionPropertyLowering(
                prop.Name + IrExtensionConventions.PropertyGetterSuffix,
                ResolvePropertyEmittedName(prop),
                containing
            );
        }

        return null;
    }

    /// <summary>
    /// Recursively rewrites a null-conditional assignment's
    /// left-hand side (which carries an
    /// <see cref="MemberBindingExpressionSyntax"/> at its leaf) into
    /// an <see cref="IrMemberAccess"/> / <see cref="IrElementAccess"/>
    /// chain rooted on the supplied receiver. Returns <c>null</c>
    /// when the shape is outside the covered subset (e.g., a
    /// pointer-element-binding, a conditional access nested inside
    /// the assignment target).
    /// </summary>
    private IrExpression? RebindAssignmentTarget(ExpressionSyntax target, IrExpression receiver) =>
        target switch
        {
            MemberBindingExpressionSyntax mb => new IrMemberAccess(
                receiver,
                mb.Name.Identifier.ValueText,
                BuildOrigin(_semantic.GetSymbolInfo(mb).Symbol)
            ),
            MemberAccessExpressionSyntax ma
                when RebindAssignmentTarget(ma.Expression, receiver) is { } inner =>
                new IrMemberAccess(
                    inner,
                    ma.Name.Identifier.ValueText,
                    BuildOrigin(_semantic.GetSymbolInfo(ma).Symbol)
                ),
            ElementAccessExpressionSyntax ea
                when RebindAssignmentTarget(ea.Expression, receiver) is { } inner =>
                new IrElementAccess(inner, Extract(ea.ArgumentList.Arguments[0].Expression)),
            _ => null,
        };

    /// <summary>
    /// True when <paramref name="method"/> is a delegate's
    /// synthesized <c>Invoke</c> method. C# manufactures this
    /// member on every delegate type; it has no JS/TS counterpart
    /// because the delegate IS the function. The extractor uses
    /// the predicate at two sites:
    /// <list type="bullet">
    ///   <item><c>handler?.Invoke(args)</c> lowers to a
    ///   delegate-typed optional call (<see cref="IrCallExpression"/>
    ///   with <c>IsOptional = true</c>).</item>
    ///   <item><c>handler.Invoke(args)</c> drops the <c>.Invoke</c>
    ///   indirection and lowers to a plain call on the delegate
    ///   receiver.</item>
    /// </list>
    /// </summary>
    private static bool IsDelegateInvoke(IMethodSymbol method) =>
        method.MethodKind == MethodKind.DelegateInvoke;

    /// <summary>
    /// True when the delegate's first parameter carries
    /// <c>[This]</c> — i.e. the receiver is rebound as JS
    /// <c>this</c> at the call site. The Invoke shortcuts must
    /// fall through for these so the existing
    /// <c>delegate.call(receiver, ...)</c> rewrite still runs.
    /// </summary>
    private static bool HasThisParameter(IMethodSymbol method) =>
        method.Parameters.Length > 0 && SymbolHelper.HasThis(method.Parameters[0]);

    /// <summary>
    /// Evaluates <paramref name="receiver"/> once when <paramref name="build"/>
    /// needs to reference it more than once: a simple receiver is reused
    /// as-is, anything else is let-bound to a fresh temporary.
    /// </summary>
    private IrExpression BindReceiverOnce(
        IrExpression receiver,
        Func<IrExpression, IrExpression> build
    ) => BindOnce(receiver, IrEvaluationTraits.IsReusableReceiver(receiver), build);

    /// <summary>
    /// Like <see cref="BindReceiverOnce"/> for a value that is not a receiver
    /// (an index offset): reused as-is when it has no side effects, so the
    /// common <c>x[^1]</c> stays flat, and let-bound otherwise.
    /// </summary>
    private IrExpression BindValueOnce(
        IrExpression value,
        Func<IrExpression, IrExpression> build
    ) => BindOnce(value, IrEvaluationTraits.IsSideEffectFree(value), build);

    /// <summary>
    /// The single "evaluate once" lowering: a reusable value is passed to
    /// <paramref name="build"/> as-is, anything else is bound once by an
    /// <see cref="IrLetExpression"/>. Every binding gets a fresh name, so a
    /// nested rewrite (a setter whose new value triggers another setter)
    /// never shadows an enclosing one.
    /// </summary>
    private IrExpression BindOnce(
        IrExpression value,
        bool isReusable,
        Func<IrExpression, IrExpression> build
    )
    {
        if (isReusable)
            return build(value);

        var tempName = NextReceiverTempName();

        return new IrLetExpression(tempName, value, build(new IrIdentifier(tempName)));
    }

    /// <summary>
    /// An unsupported (already reported) placeholder is not an assignable
    /// location, so when it is the target of an assignment or an increment it
    /// stands in for the whole write — <c>(...)() = v</c> would not parse.
    /// </summary>
    private static bool IsUnsupportedPlaceholder(IrExpression expression) =>
        expression is IrUnsupportedExpression;

    private IrExpression ExtractCollectionExpression(CollectionExpressionSyntax coll)
    {
        // Preserve source order across both element kinds: a plain expression
        // element extracts directly; a spread element (`..source`) lowers to an
        // IrSpreadExpression so a downstream backend can re-emit `...source`
        // (TS array spread) instead of silently dropping it.
        var elements = coll
            .Elements.Select<CollectionElementSyntax, IrExpression>(e =>
                e switch
                {
                    ExpressionElementSyntax expr => Extract(expr.Expression),
                    SpreadElementSyntax spread => new IrSpreadExpression(
                        Extract(spread.Expression)
                    ),
                    _ => Extract(((ExpressionElementSyntax)e).Expression),
                }
            )
            .ToList();
        var convertedType = _semantic.GetTypeInfo(coll).ConvertedType;
        if (convertedType is INamedTypeSymbol named)
        {
            var fullName = named.OriginalDefinition.ToDisplayString();
            if (
                fullName.StartsWith("System.Collections.Generic.HashSet")
                || fullName.StartsWith("System.Collections.Generic.ISet")
                || fullName.StartsWith("System.Collections.Generic.SortedSet")
            )
            {
                var elementType =
                    named.TypeArguments.Length > 0
                        ? IrTypeRefMapper.Map(named.TypeArguments[0], _originResolver, _target)
                        : new IrUnknownTypeRef();
                var setType = new IrSetTypeRef(elementType);
                return new IrNewExpression(
                    setType,
                    elements.Select(e => new IrArgument(e)).ToList()
                );
            }
        }
        return new IrArrayLiteral(elements);
    }

    private IrWithExpression ExtractWithExpression(WithExpressionSyntax withExpr)
    {
        var assignments = new List<IrWithAssignment>();
        foreach (var expr in withExpr.Initializer.Expressions)
        {
            if (expr is not AssignmentExpressionSyntax assign)
                continue;
            var memberName = assign.Left switch
            {
                IdentifierNameSyntax id => id.Identifier.ValueText,
                _ => assign.Left.ToString(),
            };
            ReportCustomInitAccessor(assign);

            assignments.Add(
                new IrWithAssignment(
                    memberName,
                    Extract(assign.Right),
                    ResolveMemberNameOverride(assign.Left)
                )
            );
        }
        // Mark [PlainObject] sources so backends that emit object literals
        // (TypeScript's `{ ...source, k: v }` form) can pick the right shape.
        // For non-plain sources, the with method on the class prototype is
        // used instead.
        var sourceType = _semantic.GetTypeInfo(withExpr.Expression).Type;
        var isPlainObject =
            sourceType is INamedTypeSymbol named && SymbolHelper.HasPlainObject(named);
        return new IrWithExpression(Extract(withExpr.Expression), assignments, isPlainObject);
    }

    /// <summary>
    /// Folds an invocation the compiler evaluates at compile time — in C#
    /// that is <c>nameof(x)</c>, whose value is the string <c>"x"</c> — into
    /// a string literal, so no target ever emits a call to a function that
    /// does not exist at runtime.
    /// </summary>
    private IrLiteral? TryExtractConstantInvocation(InvocationExpressionSyntax inv) =>
        _semantic.GetConstantValue(inv) is { HasValue: true, Value: string value }
            ? new IrLiteral(value, IrLiteralKind.String)
            : null;

    /// <summary>
    /// Lowers a C# <c>switch</c> expression into <see cref="IrSwitchExpression"/>.
    /// Each <see cref="SwitchExpressionArmSyntax"/> becomes an <see cref="IrSwitchArm"/>
    /// carrying the pattern, an optional <c>when</c> guard, and the arm's result
    /// expression.
    /// </summary>
    private IrSwitchExpression ExtractSwitchExpression(SwitchExpressionSyntax switchExpr)
    {
        var arms = switchExpr
            .Arms.Select(a => new IrSwitchArm(
                ExtractPattern(a.Pattern),
                a.WhenClause is not null ? Extract(a.WhenClause.Condition) : null,
                Extract(a.Expression)
            ))
            .ToList();
        return new IrSwitchExpression(Extract(switchExpr.GoverningExpression), arms);
    }

    /// <summary>
    /// Converts a C# pattern syntax into the IR hierarchy. Covers the foundational
    /// subset (constant, type with optional designator, var, discard); anything
    /// else surfaces as <see cref="IrUnsupportedPattern"/> so backends can emit a
    /// visible TODO without silently producing wrong code.
    /// </summary>
    private IrPattern ExtractPattern(PatternSyntax pattern) =>
        pattern switch
        {
            ConstantPatternSyntax constant when NamedPatternType(constant.Expression) is { } type =>
                ExtractTypePattern(constant, type),
            ConstantPatternSyntax constant => new IrConstantPattern(Extract(constant.Expression)),
            DeclarationPatternSyntax decl => ExtractTypePattern(
                decl,
                ResolvePatternType(decl.Type),
                ResolveOptionalDesignator(decl.Designation)
            ),
            TypePatternSyntax typeOnly => ExtractTypePattern(
                typeOnly,
                ResolvePatternType(typeOnly.Type)
            ),
            VarPatternSyntax { Designation: SingleVariableDesignationSyntax single } =>
                new IrVarPattern(ResolveDesignator(single)),
            DiscardPatternSyntax => new IrDiscardPattern(),
            RecursivePatternSyntax recursive => ExtractRecursivePattern(recursive),
            RelationalPatternSyntax rel => ExtractRelationalPattern(rel),
            BinaryPatternSyntax bin => ExtractBinaryLogicalPattern(bin),
            UnaryPatternSyntax un when un.OperatorToken.Text == "not" => new IrLogicalPattern(
                IrLogicalOp.Not,
                ExtractPattern(un.Pattern),
                null
            ),
            ParenthesizedPatternSyntax paren => ExtractPattern(paren.Pattern),
            ListPatternSyntax list => ExtractListPattern(list),
            _ => new IrUnsupportedPattern(pattern.Kind().ToString()),
        };

    /// <summary>
    /// A type test, or — when the target has no runtime test for
    /// <paramref name="type"/> — an MS0001 report and an
    /// <see cref="IrUnsupportedPattern"/>, so no backend lowers a test against
    /// a type that does not exist at runtime.
    /// </summary>
    private IrPattern ExtractTypePattern(
        SyntaxNode pattern,
        IrTypeRef type,
        IrPatternDesignator? designator = null
    ) =>
        HasRuntimeTypeTest(type)
            ? new IrTypePattern(type, designator)
            : ReportUnsupportedPattern(pattern);

    private static IrUnsupportedPattern ReportUnsupportedPattern(SyntaxNode pattern)
    {
        ReportUnsupportedFeature(pattern, NoRuntimeTypeTestMessage("Pattern", pattern));

        return new IrUnsupportedPattern(pattern.Kind().ToString());
    }

    /// <summary>
    /// Lowers <c>[p1, p2, .., pN]</c>. Roslyn represents the slice (<c>..</c>)
    /// as a <see cref="SlicePatternSyntax"/> entry in the list; at the IR
    /// level we strip it out and record its position in
    /// <see cref="IrListPattern.SliceIndex"/>. Inner slice sub-patterns
    /// (<c>.. var tail</c>) are not captured yet — callers that need them
    /// fall through to <see cref="IrUnsupportedPattern"/>.
    /// </summary>
    private IrListPattern ExtractListPattern(ListPatternSyntax list)
    {
        var elements = new List<IrPattern>();
        int? sliceIndex = null;
        IrPattern? slicePattern = null;
        for (var i = 0; i < list.Patterns.Count; i++)
        {
            var p = list.Patterns[i];
            if (p is SlicePatternSyntax slice)
            {
                sliceIndex = elements.Count;
                if (slice.Pattern is not null)
                    slicePattern = ExtractPattern(slice.Pattern);
                continue;
            }
            elements.Add(ExtractPattern(p));
        }
        return new IrListPattern(elements, sliceIndex, slicePattern);
    }

    /// <summary>
    /// <c>&lt;</c> / <c>&lt;=</c> / <c>&gt;</c> / <c>&gt;=</c> against a
    /// constant. A relational pattern never matches <c>null</c>, so a pattern
    /// whose input is a nullable value (<c>int? x; x is &lt;= 0</c>) carries
    /// that implicit non-null test explicitly: <c>not null and &lt;= 0</c>. An
    /// input already narrowed (the right side of <c>&gt; 0 and &lt; 10</c>)
    /// needs none.
    /// </summary>
    private IrPattern ExtractRelationalPattern(RelationalPatternSyntax rel)
    {
        var op = rel.OperatorToken.Text switch
        {
            "<" => IrRelationalOp.LessThan,
            "<=" => IrRelationalOp.LessThanOrEqual,
            ">" => IrRelationalOp.GreaterThan,
            ">=" => IrRelationalOp.GreaterThanOrEqual,
            _ => IrRelationalOp.LessThan,
        };

        var relational = new IrRelationalPattern(op, Extract(rel.Expression));

        return MatchesNullableValue(rel)
            ? new IrLogicalPattern(IrLogicalOp.And, NotNullPattern, relational)
            : relational;
    }

    private bool MatchesNullableValue(PatternSyntax pattern) =>
        _semantic.GetOperation(pattern) is IPatternOperation { InputType: var input }
        && input.IsNullableValueType();

    private IrLogicalPattern ExtractBinaryLogicalPattern(BinaryPatternSyntax bin)
    {
        var op = bin.OperatorToken.Text switch
        {
            "and" => IrLogicalOp.And,
            "or" => IrLogicalOp.Or,
            _ => IrLogicalOp.And,
        };
        return new IrLogicalPattern(op, ExtractPattern(bin.Left), ExtractPattern(bin.Right));
    }

    /// <summary>
    /// Roslyn uses <see cref="RecursivePatternSyntax"/> for both property
    /// patterns (<c>{ X: 0 }</c>, <c>Point { X: 0 }</c>) and positional
    /// patterns (<c>(0, var y)</c>). Only the property-pattern form is
    /// modeled today; a positional-only shape or a positional + property
    /// mix falls back to <see cref="IrUnsupportedPattern"/> so we don't
    /// silently drop positional elements.
    /// </summary>
    private IrPattern ExtractRecursivePattern(RecursivePatternSyntax recursive)
    {
        var type = recursive.Type is not null
            ? ResolvePatternType(recursive.Type)
            : (IrTypeRef?)null;
        var designator = ResolveOptionalDesignator(recursive.Designation);

        if (type is not null && !HasRuntimeTypeTest(type))
            return ReportUnsupportedPattern(recursive);

        // Positional pattern — `(x, y)` or `Point(x, y)` — decomposes a
        // tuple or calls Deconstruct on a record. Property patterns land on
        // the same syntax node but use PropertyPatternClause instead.
        if (recursive.PositionalPatternClause is not null)
        {
            var elements = recursive
                .PositionalPatternClause.Subpatterns.Select(s => ExtractPattern(s.Pattern))
                .ToList();

            var operation = _semantic.GetOperation(recursive) as IRecursivePatternOperation;

            return new IrPositionalPattern(
                type,
                elements,
                designator,
                ResolvePositionalElementMembers(recursive, operation),
                InputMayBeNull(operation?.InputType)
            );
        }

        var subpatterns = recursive.PropertyPatternClause?.Subpatterns ?? [];
        var extractedSubpatterns = subpatterns.Select(ExtractPropertySubpattern).ToList();

        // A null entry is a member path that is not a chain of simple names.
        if (extractedSubpatterns.Any(sub => sub is null))
            return ReportUnsupportedPattern(recursive);

        return new IrPropertyPattern(
            type,
            extractedSubpatterns.OfType<IrPropertySubpattern>().ToList(),
            designator
        );
    }

    /// <summary>
    /// Extracts one <c>Name: pattern</c> entry of a property pattern. The
    /// C# 10 extended form <c>A.B.C: pattern</c> is defined by the language
    /// as <c>A: { B: { C: pattern } }</c>, so it is desugared into nested
    /// type-less property patterns; each intermediate level keeps the
    /// non-null test a <c>{ … }</c> pattern implies. Returns <c>null</c> when
    /// the member path is not a chain of simple names (only reachable on
    /// code with compile errors).
    /// </summary>
    private IrPropertySubpattern? ExtractPropertySubpattern(SubpatternSyntax subpattern)
    {
        var memberPath = subpattern.NameColon?.Name ?? subpattern.ExpressionColon?.Expression;

        var segments = memberPath is null ? null : MemberPathSegments(memberPath);

        if (segments is null)
            return null;

        var current = PropertySubpattern(segments[^1], ExtractPattern(subpattern.Pattern));

        // Wrap from the innermost member outward: `A.B.C: p` → `A: { B: { C: p } }`.
        for (var index = segments.Count - 2; index >= 0; index--)
        {
            current = PropertySubpattern(
                segments[index],
                new IrPropertyPattern(Type: null, [current])
            );
        }

        return current;
    }

    private IrPropertySubpattern PropertySubpattern(SimpleNameSyntax member, IrPattern pattern) =>
        new(
            member.Identifier.ValueText,
            pattern,
            ResolveMemberNameOverride(member),
            IsTranspiledDataMember(_semantic.GetSymbolInfo(member).Symbol)
        );

    /// <summary>
    /// The simple names of a member path, outermost first:
    /// <c>Text.Length</c> → <c>[Text, Length]</c>. Returns <c>null</c> for
    /// any other expression shape.
    /// </summary>
    private static IReadOnlyList<SimpleNameSyntax>? MemberPathSegments(ExpressionSyntax path) =>
        path switch
        {
            IdentifierNameSyntax name => [name],
            MemberAccessExpressionSyntax
            {
                RawKind: (int)SyntaxKind.SimpleMemberAccessExpression,
                Name: IdentifierNameSyntax name,
            } access => MemberPathSegments(access.Expression) is { } receiver
                ? [.. receiver, name]
                : null,
            _ => null,
        };

    /// <summary>
    /// The member each positional element reads, or <c>null</c> to read the
    /// elements by position:
    /// <list type="bullet">
    ///   <item>A matched value that lowers to an array tuple — <c>ValueTuple</c>,
    ///   <c>Tuple</c> (deconstructed by the <c>TupleExtensions</c> extension),
    ///   <c>KeyValuePair</c>, a <c>[JsTuple]</c> record — is read by
    ///   position.</item>
    ///   <item>A record's compiler-synthesized <c>Deconstruct</c> returns its
    ///   positional members, so each out parameter resolves to the property
    ///   or field of the same name.</item>
    ///   <item>A user-written <c>Deconstruct</c> may compute its outputs.</item>
    /// </list>
    /// Every element left unresolved (<c>null</c> entry) is reported as an
    /// MS0001 warning, never dropped silently.
    /// </summary>
    private IReadOnlyList<IrPositionalElementMember?>? ResolvePositionalElementMembers(
        RecursivePatternSyntax recursive,
        IRecursivePatternOperation? operation
    )
    {
        if (operation?.DeconstructSymbol is not IMethodSymbol deconstruct)
            return null;

        if (LowersToArrayTuple(operation.MatchedType ?? operation.InputType))
            return null;

        var outParameters = deconstruct.Parameters.Where(p => p.RefKind == RefKind.Out);

        var members = outParameters
            .Select(p =>
                deconstruct.IsImplicitlyDeclared
                    ? ToPositionalElementMember(deconstruct.ContainingType, p.Name)
                    : null
            )
            .ToList();

        if (members.Any(m => m is null))
            ReportUnresolvedPositionalElements(recursive, deconstruct);

        return members;
    }

    private bool LowersToArrayTuple(ITypeSymbol type) =>
        SymbolHelper.HasJsTuple(type)
        || IrTypeRefMapper.Map(type, _originResolver, _target)
            is IrTupleTypeRef
                or IrKeyValuePairTypeRef;

    private static bool InputMayBeNull(ITypeSymbol? type) =>
        type switch
        {
            null => false,
            { IsReferenceType: true } => true,
            { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } => true,
            ITypeParameterSymbol parameter => !parameter.HasValueTypeConstraint,
            _ => false,
        };

    private static void ReportUnresolvedPositionalElements(
        RecursivePatternSyntax recursive,
        IMethodSymbol deconstruct
    ) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"Positional pattern over '{deconstruct.ContainingType.Name}.Deconstruct' is not "
                    + "supported: some of its outputs cannot be mapped to members.",
                recursive.GetLocation()
            )
        );

    private IrPositionalElementMember? ToPositionalElementMember(ITypeSymbol record, string name) =>
        SymbolHelper.FindInstanceDataMemberInHierarchy(record, name) is { } member
            ? new IrPositionalElementMember(
                member.Name,
                member.GetNameOverride(_target),
                IsTranspiledDataMember(member)
            )
            : null;

    /// <summary>
    /// A custom <c>set { … }</c> or transpiled <c>init { … }</c> body lowers to a
    /// setter, which runs when a <c>with</c> assigns the property. An
    /// <c>init</c> body that is not transpiled (see
    /// <see cref="CustomInitAccessor"/>) leaves the property without a setter,
    /// so a <c>with</c> that sets it neither runs the body nor applies the new
    /// value. Reported as an MS0001 warning at the assignment.
    /// </summary>
    private void ReportCustomInitAccessor(AssignmentExpressionSyntax assignment)
    {
        if (_semantic.GetSymbolInfo(assignment.Left).Symbol is not IPropertySymbol property)
            return;

        if (!CustomInitAccessor.HasUnsupportedBody(property))
            return;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'with' sets '{property.Name}', whose custom init accessor is not transpiled: "
                    + "the accessor body does not run and the new value is not applied.",
                assignment.GetLocation()
            )
        );
    }

    /// <summary>
    /// The target's <c>[Name]</c> override of the member a property pattern
    /// entry or a <c>with</c> assignment names, when the semantic model
    /// resolves one.
    /// </summary>
    private string? ResolveMemberNameOverride(ExpressionSyntax memberReference) =>
        _semantic.GetSymbolInfo(memberReference).Symbol?.GetNameOverride(_target);

    /// <summary>
    /// A bare type in pattern position without a designator (<c>ImageClip =&gt; …</c>,
    /// <c>{ Head: ImageClip }</c>, <c>ImageClip or SilenceClip</c>) parses as a
    /// constant pattern whose expression names a type; semantically it is a
    /// type pattern.
    /// </summary>
    private IrTypeRef? NamedPatternType(ExpressionSyntax expression) =>
        _semantic.GetSymbolInfo(expression).Symbol is ITypeSymbol type
            ? IrTypeRefMapper.Map(type, _originResolver, _target)
            : null;

    private IrTypeRef ResolvePatternType(TypeSyntax typeSyntax)
    {
        var resolved = _semantic.GetTypeInfo(typeSyntax).Type;
        return resolved is not null
            ? IrTypeRefMapper.Map(resolved, _originResolver, _target)
            : new IrUnknownTypeRef();
    }

    /// <summary>
    /// The local a pattern designation declares, typed as C# declared it, or
    /// <c>null</c> for no designation or a discard (<c>Foo _</c>).
    /// </summary>
    private IrPatternDesignator? ResolveOptionalDesignator(
        VariableDesignationSyntax? designation
    ) => designation is SingleVariableDesignationSyntax single ? ResolveDesignator(single) : null;

    private IrPatternDesignator ResolveDesignator(SingleVariableDesignationSyntax designation)
    {
        IrTypeRef type = _semantic.GetDeclaredSymbol(designation) is ILocalSymbol local
            ? IrTypeRefMapper.Map(local.Type, _originResolver, _target)
            : new IrUnknownTypeRef();

        return new IrPatternDesignator(designation.Identifier.ValueText, type);
    }

    /// <summary>
    /// An identifier can refer to a local, parameter, field, property, method, <em>or
    /// a type</em>. The semantic model tells us which — we emit <see cref="IrTypeReference"/>
    /// when the symbol is a type so backends can keep PascalCase for it (e.g., Dart's
    /// <c>Counter.zero</c> needs <c>Counter</c> preserved).
    /// <para>
    /// When the identifier resolves to an instance member of the containing type
    /// (C# allows the implicit-<c>this</c> shorthand), the extractor synthesizes
    /// an explicit <see cref="IrMemberAccess"/> with an <see cref="IrThisExpression"/>
    /// target. That keeps every backend that lowers the IR from having to
    /// reconstruct the member-vs-local distinction.
    /// </para>
    /// </summary>
    /// <summary>
    /// <c>OperationResult&lt;Issue&gt;</c> used as an expression (e.g., the
    /// receiver of <c>OperationResult&lt;Issue&gt;.Ok(…)</c>). Roslyn surfaces
    /// this as <see cref="GenericNameSyntax"/>; the semantic model still
    /// resolves it to a type or method symbol, so we reuse the identifier
    /// path to apply the same type-reference / implicit-this / static-
    /// qualifier rewrites.
    /// </summary>
    private IrExpression ExtractGenericName(GenericNameSyntax generic)
    {
        var symbol = _semantic.GetSymbolInfo(generic).Symbol;
        if (symbol is ITypeSymbol or INamespaceSymbol)
            return TypeReferenceFromIdentifier(symbol, generic.Identifier.ValueText);

        if (
            symbol
                is { IsStatic: false }
                    and (IPropertySymbol or IFieldSymbol or IEventSymbol or IMethodSymbol)
            && symbol.ContainingType is not null
            && !IsLocalLikeSymbol(symbol)
        )
            return WrapMethodGroupForDelegate(
                generic,
                symbol,
                new IrMemberAccess(
                    new IrThisExpression(),
                    generic.Identifier.ValueText,
                    BuildOrigin(symbol)
                )
            );

        if (
            symbol is { IsStatic: true } and (IPropertySymbol or IFieldSymbol or IMethodSymbol)
            && symbol.ContainingType is not null
        )
            return WrapMethodGroupForDelegate(
                generic,
                symbol,
                new IrMemberAccess(
                    TypeReferenceTo(symbol.ContainingType),
                    generic.Identifier.ValueText,
                    BuildOrigin(symbol)
                )
            );

        return WrapMethodGroupForDelegate(
            generic,
            symbol,
            new IrIdentifier(generic.Identifier.ValueText)
        );
    }

    private IrExpression ExtractIdentifierName(IdentifierNameSyntax id)
    {
        var symbol = _semantic.GetSymbolInfo(id).Symbol;
        if (symbol is ITypeSymbol or INamespaceSymbol)
            return TypeReferenceFromIdentifier(symbol, id.Identifier.ValueText);

        // Inline method expansion: when the enclosing extractor is
        // lowering an `[Inline]` method body, each use of a method
        // parameter (including the extension receiver) substitutes to
        // the caller's argument IR instead of resolving to the
        // parameter symbol — parameters cannot exist at the call site.
        if (symbol is not null && _inlineParameterSubs?.TryGetValue(symbol, out var sub) is true)
            return sub;

        // Captured primary-constructor parameter referenced from a
        // member body. Roslyn synthesizes a backing field on the
        // class for the param; the transpiler does the same and
        // rewrites the bare reference into `this._field` so the
        // emitted code reads from the auto-synthesized field instead
        // of an out-of-scope parameter binding. The map is set by
        // `IrClassExtractor` around member-body extraction; outside
        // that scope the lookup is a no-op.
        //
        // Field / property initializers are exempt from the rewrite
        // unless they run in the constructor body after the captured
        // fields are assigned (`InitializersRunInConstructor`): a class
        // field initializer executes BEFORE the ctor body assigns the
        // synthesized field, so reading `this._view` there would
        // observe `undefined`. Keep the bare param reference in those
        // positions.
        if (
            symbol is IParameterSymbol paramSymbol
            && PrimaryCtorScope is { } scope
            && scope.CapturedParams.TryGetValue(paramSymbol, out var capturedFieldName)
            && (scope.InitializersRunInConstructor || !IsInsideInitializer(id))
        )
            return new IrMemberAccess(new IrThisExpression(), capturedFieldName);

        if (TryReadPromotedParameter(symbol, id) is { } promotedRead)
            return promotedRead;

        // `[Inline]` member referenced without an explicit qualifier
        // (same-type static access, extension receiver, etc.). Expand
        // before synthesizing any member-access wrapper so the
        // initializer flows through as if it had been written at the
        // call site.
        if (TryExpandInlineAccess(symbol) is { } inlined)
            return inlined;

        // Instance member reached through the implicit-this shorthand: promote to
        // an explicit this.Member access so backends don't have to rediscover the
        // elision.
        if (
            symbol
                is { IsStatic: false }
                    and (IPropertySymbol or IFieldSymbol or IEventSymbol or IMethodSymbol)
            && symbol.ContainingType is not null
            && !IsLocalLikeSymbol(symbol)
        )
        {
            var memberAccess = new IrMemberAccess(
                new IrThisExpression(),
                id.Identifier.ValueText,
                BuildOrigin(symbol)
            );
            return WrapMethodGroupForDelegate(id, symbol, memberAccess);
        }

        // Static member of the enclosing (or any other) type reached without a
        // qualifier: C# allows `StaticMethod(…)` from within a class to mean
        // `ClassName.StaticMethod(…)`, but TS/Dart don't. Synthesize the
        // qualifier as `IrMemberAccess(IrTypeReference(ClassName), name)` so
        // backends emit the fully-qualified form.
        if (
            symbol is { IsStatic: true } and (IPropertySymbol or IFieldSymbol or IMethodSymbol)
            && symbol.ContainingType is not null
        )
        {
            var staticAccess = new IrMemberAccess(
                TypeReferenceTo(symbol.ContainingType),
                id.Identifier.ValueText,
                BuildOrigin(symbol)
            );
            return WrapMethodGroupForDelegate(id, symbol, staticAccess);
        }

        return WrapMethodGroupForDelegate(id, symbol, new IrIdentifier(id.Identifier.ValueText));
    }

    private static bool IsLocalLikeSymbol(ISymbol symbol) =>
        symbol is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol;

    /// <summary>
    /// Builds the value-position reference to <paramref name="type"/>. A nested type
    /// is qualified with its containers (<c>Outer.Inner</c>) — the same dotted shape
    /// <see cref="IrNamedTypeRef"/> carries in type positions — because C# lets the
    /// container's members name it unqualified while the emitted declaration lives in
    /// the container's companion namespace (#264). Segments honor <c>[Name]</c> overrides
    /// exactly like the type-position name. Top-level types keep their simple name.
    /// A type from another <c>[EmitPackage]</c> package carries its cross-assembly
    /// origin so the backend can import it from a value position.
    /// </summary>
    private IrTypeReference TypeReferenceTo(INamedTypeSymbol type) =>
        new(QualifiedNestedName(type) ?? type.Name, CrossAssemblyOriginOf(type));

    /// <summary>
    /// Overload for identifier positions: a nested named type is qualified, anything
    /// else (top-level type, type parameter, namespace) keeps the source
    /// <paramref name="spelling"/>. Only a named type carries an origin.
    /// </summary>
    private IrTypeReference TypeReferenceFromIdentifier(ISymbol symbol, string spelling)
    {
        if (symbol is not INamedTypeSymbol type)
            return new(spelling);

        return new(QualifiedNestedName(type) ?? spelling, CrossAssemblyOriginOf(type));
    }

    /// <summary>
    /// The cross-assembly origin of <paramref name="type"/> — the same lookup the
    /// type-position mapper performs — or null for an intra-project or BCL type.
    /// </summary>
    private IrTypeOrigin? CrossAssemblyOriginOf(INamedTypeSymbol type) =>
        _originResolver.ResolveThroughOutermost(type);

    /// <summary>
    /// The container-qualified name of a nested type, or <c>null</c> for a top-level
    /// type (callers fall back to their own spelling on <c>null</c>).
    /// </summary>
    private string? QualifiedNestedName(INamedTypeSymbol type) =>
        type.ContainingType is null ? null : IrTypeRefMapper.BuildQualifiedName(type, _target);

    /// <summary>
    /// True when the identifier runs while a field or property initializer
    /// runs (<see cref="MemberInitializerSyntax.Contains"/>, stopping at
    /// executable bodies: a block lambda inside the initializer runs later).
    /// Captured primary-ctor parameter references must not be rewritten in
    /// these positions because field initializers execute before the
    /// constructor body assigns the synthesized backing field.
    /// </summary>
    private static bool IsInsideInitializer(SyntaxNode node) =>
        MemberInitializerSyntax.Contains(node, stopAtExecutableBodies: true);

    /// <summary>
    /// A member-body read of a primary-constructor parameter folded into the
    /// property that only exposes it (<c>A { get; } = a</c>): the property
    /// the backends emit as a parameter property is the parameter's only
    /// storage, so the read becomes <c>this.A</c>. <c>null</c> for any other
    /// identifier, and inside an initializer, which keeps the bare parameter
    /// as captured parameters do: it is in scope when the initializer is
    /// deferred into the constructor body
    /// (<see cref="PrimaryCtorExtractionScope.InitializersRunInConstructor"/>),
    /// and a field initializer cannot read the parameter property either,
    /// since it runs before the constructor assigns it.
    /// </summary>
    private IrMemberAccess? TryReadPromotedParameter(ISymbol? symbol, SyntaxNode id)
    {
        if (symbol is not IParameterSymbol parameter || PrimaryCtorScope is not { } scope)
            return null;

        if (!scope.PromotedParams.TryGetValue(parameter, out var property))
            return null;

        return IsInsideInitializer(id)
            ? null
            : new IrMemberAccess(new IrThisExpression(), property.Name, BuildOrigin(property));
    }

    // ── Literals ──────────────────────────────────────────────────────────

    private IrLiteral ExtractLiteral(LiteralExpressionSyntax lit)
    {
        var value = _semantic.GetConstantValue(lit).Value;
        return lit.Kind() switch
        {
            SyntaxKind.NullLiteralExpression => new IrLiteral(null, IrLiteralKind.Null),
            SyntaxKind.TrueLiteralExpression => new IrLiteral(true, IrLiteralKind.Boolean),
            SyntaxKind.FalseLiteralExpression => new IrLiteral(false, IrLiteralKind.Boolean),
            SyntaxKind.NumericLiteralExpression => ClassifyNumericInContext(lit, value),
            SyntaxKind.StringLiteralExpression => new IrLiteral(value, IrLiteralKind.String),
            SyntaxKind.CharacterLiteralExpression => new IrLiteral(value, IrLiteralKind.Char),
            SyntaxKind.DefaultLiteralExpression => ExtractDefaultLiteral(lit),
            _ => new IrLiteral(value, IrLiteralKind.Default),
        };
    }

    /// <summary>
    /// Classifies a numeric literal based on the target type the SemanticModel
    /// inferred at the call site. C#'s implicit conversions — `100` in a
    /// `decimal` context, `150` in a `BigInteger` context — carry through the
    /// `ConvertedType`, and the backend needs that info to pick the right
    /// runtime representation (<c>new Decimal("100")</c>, <c>150n</c>).
    /// Mirrors the legacy <see cref="Metano.Compiler.TypeScript.Transformation.LiteralHandler"/>
    /// shape so the IR path produces matching output.
    /// </summary>
    private IrLiteral ClassifyNumericInContext(LiteralExpressionSyntax lit, object? value)
    {
        var convertedType = _semantic.GetTypeInfo(lit).ConvertedType;
        if (convertedType?.SpecialType == SpecialType.System_Decimal)
            return new IrLiteral(lit.Token.ValueText, IrLiteralKind.Decimal);
        if (convertedType?.ToDisplayString() == "System.Numerics.BigInteger")
            return new IrLiteral(lit.Token.ValueText, IrLiteralKind.BigInteger);
        return ClassifyNumeric(value);
    }

    /// <summary>
    /// C#'s target-typed <c>default</c> carries no surface type — the runtime
    /// value depends on what the compiler infers at the call site. Mirrors
    /// the legacy <c>ExpressionTransformer</c> behavior: when the inferred
    /// type is a reference type, type parameter, or nullable, emit a real
    /// <c>null</c> literal (so consumers see <c>null</c> instead of
    /// <c>undefined</c>). Value-type / struct contexts keep the opaque
    /// <see cref="IrLiteralKind.Default"/> so backends can pick a sensible
    /// per-target default.
    /// </summary>
    private IrLiteral ExtractDefaultLiteral(LiteralExpressionSyntax lit)
    {
        var type = _semantic.GetTypeInfo(lit).ConvertedType;
        if (
            type is not null
            && (type.IsReferenceType || type is ITypeParameterSymbol || IsNullableType(type))
        )
            return new IrLiteral(null, IrLiteralKind.Null);
        return new IrLiteral(null, IrLiteralKind.Default);
    }

    private static bool IsNullableType(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated || type.IsNullableValueType();

    private static IrLiteral ClassifyNumeric(object? value) =>
        value switch
        {
            int i => new IrLiteral(i, IrLiteralKind.Int32),
            long l => new IrLiteral(l, IrLiteralKind.Int64),
            double d => new IrLiteral(d, IrLiteralKind.Float64),
            float f => new IrLiteral((double)f, IrLiteralKind.Float64),
            decimal dec => new IrLiteral(dec, IrLiteralKind.Decimal),
            _ => new IrLiteral(value, IrLiteralKind.Int32),
        };

    // ── Operators ─────────────────────────────────────────────────────────

    private IrExpression ExtractBinary(BinaryExpressionSyntax bin)
    {
        // Roslyn parses bare `o is Foo` (type test without designator) as a
        // BinaryExpressionSyntax of kind IsExpression, not as an IsPatternExpression.
        // Normalize to IrIsPatternExpression so backends see a single pattern shape.
        if (bin.Kind() == SyntaxKind.IsExpression && bin.Right is TypeSyntax typeRhs)
            return ExtractIsTypeTest(bin, typeRhs);

        if (bin.Kind() == SyntaxKind.AsExpression && bin.Right is TypeSyntax asType)
            return ExtractAsCast(bin, asType);

        if (MapBinaryOp(bin.Kind()) is not { } mappedOp)
            return ReportUnsupported(bin, bin.Kind().ToString());

        var op = NarrowToIntegerDivision(mappedOp, bin);

        return TryExtractValueEquality(bin)
            ?? TryExtractLiftedBinary(bin, op)
            ?? TryExtractStringConcatenation(bin)
            ?? BuildBinary(op, OperandOf(bin.Left), OperandOf(bin.Right), isLifted: false);
    }

    /// <summary>
    /// The ordinary lowering of <c>left op right</c> over already extracted
    /// operands, whose syntax gives their types. A lifted operator reuses it
    /// for its non-null case: <paramref name="isLifted"/> peels
    /// <c>Nullable&lt;T&gt;</c> from the operand types. It stays false for
    /// an unlifted operator such as <c>a == null</c> on a <c>decimal?</c>,
    /// which is not a Decimal method call.
    /// </summary>
    private IrExpression BuildBinary(
        IrBinaryOp op,
        OperatorOperand left,
        OperatorOperand right,
        bool isLifted
    )
    {
        // decimal arithmetic needs a special lowering: legacy emits
        // `a.plus(b)` / `.times(b)` / `.div(b)` / etc. on the decimal.js
        // Decimal class rather than raw `+` / `*` / `/`. Detect via
        // SemanticModel and normalize to an IrCallExpression so every
        // backend that supports BCL-style dispatch produces the same
        // shape without re-walking types later.
        if (
            MapDecimalBinaryMethod(op) is { } decimalMethod
            && AreDecimalOperands(left, right, isLifted)
        )
        {
            return BuildDecimalBinaryCall(left.Value, right.Value, decimalMethod);
        }

        // Temporal types reject the built-in relational operators at
        // runtime ("TypeError: Do not use built-in arithmetic operators
        // with Temporal objects"). Rewrite `a > b` to
        // `Temporal.PlainDate.compare(a, b) > 0` (and the other three
        // relational operators) so the generated code runs. Equality
        // stays on the existing library-helper path. The lowering is
        // TypeScript-specific — Dart uses a native `DateTime` with
        // working relational operators and Kotlin consumers will
        // surface different receivers entirely — so the rewrite only
        // fires for the TypeScript target or when the extractor is
        // running target-agnostic (unit tests that do not pin a
        // specific backend).
        if (
            (_target is null or Metano.Annotations.TargetLanguage.TypeScript)
            && op.IsRelational()
            && GetTemporalTypeName(left.Syntax, right.Syntax) is { } temporalTypeName
        )
        {
            return BuildTemporalCompareCall(left.Value, right.Value, op, temporalTypeName);
        }

        return new IrBinaryExpression(left.Value, op, right.Value);
    }

    private bool AreDecimalOperands(OperatorOperand left, OperatorOperand right, bool isLifted) =>
        isLifted
            ? IsDecimalOrNullableDecimalOperand(left.Syntax)
                && IsDecimalOrNullableDecimalOperand(right.Syntax)
            : IsDecimalOperand(left.Syntax) && IsDecimalOperand(right.Syntax);

    /// <summary>
    /// Lowers <c>x as T</c> to its definition, <c>x is T ? (T)x : null</c>,
    /// over the existing type-pattern and conditional IR so every backend
    /// reuses its own <c>is</c> lowering. A nullable target (<c>int?</c>,
    /// <c>Person?</c>) tests its underlying type, and a non-stable <c>x</c>
    /// is bound once. A target with no runtime type test reports MS0001
    /// instead.
    /// </summary>
    private IrExpression ExtractAsCast(BinaryExpressionSyntax asCast, TypeSyntax typeSyntax)
    {
        var castType = ResolvePatternType(typeSyntax);
        var testedType = castType is IrNullableTypeRef nullable ? nullable.Inner : castType;

        if (!HasRuntimeTypeTest(testedType))
            return ReportUnsupported(
                asCast,
                asCast.Kind().ToString(),
                NoRuntimeTypeTestMessage("Expression", asCast)
            );

        return BindValueOnce(
            Extract(asCast.Left),
            value => new IrConditionalExpression(
                new IrIsPatternExpression(value, new IrTypePattern(testedType)),
                new IrCastExpression(value, castType),
                new IrLiteral(null, IrLiteralKind.Null)
            )
        );
    }

    /// <summary>
    /// <c>x is T</c> without a designator, normalized to the
    /// <see cref="IrIsPatternExpression"/> shape a pattern takes.
    /// </summary>
    private IrExpression ExtractIsTypeTest(BinaryExpressionSyntax isTest, TypeSyntax typeSyntax)
    {
        var type = ResolvePatternType(typeSyntax);

        if (!HasRuntimeTypeTest(type))
            return ReportUnsupported(
                isTest,
                isTest.Kind().ToString(),
                NoRuntimeTypeTestMessage("Expression", isTest)
            );

        return new IrIsPatternExpression(Extract(isTest.Left), new IrTypePattern(type));
    }

    private static string NoRuntimeTypeTestMessage(string construct, SyntaxNode node) =>
        $"{construct} '{node}' cannot be lowered: its type has no runtime type test in the "
        + "target (an interface or [PlainObject] needs [GenerateGuard]; type parameters and "
        + "collections are erased).";

    /// <summary>
    /// TypeScript erases static types, so a type test exists only where
    /// <see cref="IrRuntimeTypeTest"/> finds a runtime representation to test.
    /// Dart reifies types and tests any type natively.
    /// </summary>
    private bool HasRuntimeTypeTest(IrTypeRef type) =>
        _target is not (null or Metano.Annotations.TargetLanguage.TypeScript)
        || IrRuntimeTypeTest.Of(type) is not IrRuntimeTypeTest.None;

    private IrExpression ExtractAssignment(AssignmentExpressionSyntax assign)
    {
        if (TryReportTupleElementWrite(assign, assign.Left) is { } unsupportedWrite)
            return unsupportedWrite;

        // Extension property assignment (`receiver.Prop = value` or any
        // compound form) — rewrite to a helper call against the property's
        // `$set` companion. Compound operators (`+=`, `++` via this path is
        // never reached but `+=` is) and the simple assign use the same
        // lowering shape; the receiver-once IR (IrLetExpression) protects
        // impure receivers from double evaluation.
        if (
            assign.Left is MemberAccessExpressionSyntax setterMember
            && _semantic.GetSymbolInfo(setterMember).Symbol is IPropertySymbol setterProp
            && TryResolveExtensionPropertyLowering(setterProp, setterMember.Expression)
                is { } setterLowering
        )
        {
            return BuildExtensionPropertyAssignment(
                setterMember,
                setterProp,
                setterLowering,
                assign
            );
        }

        // Extension indexer assignment (`receiver[i] = value`, `+=`, etc.) —
        // routes through the same `$set` companion / receiver-once pattern
        // as extension properties. The index expression is also captured
        // once for compound forms so an impure index (`xs[NextIndex()]`)
        // doesn't fire twice.
        if (
            assign.Left is ElementAccessExpressionSyntax indexerLeft
            && _semantic.GetSymbolInfo(indexerLeft).Symbol is IPropertySymbol indexerProp
            && indexerProp.IsIndexer
            && TryResolveExtensionIndexerLowering(indexerProp, indexerLeft) is { } indexerLowering
        )
        {
            return BuildExtensionIndexerAssignment(
                indexerLeft,
                indexerProp,
                indexerLowering,
                assign
            );
        }

        // `x += y` where the compound operator resolves to a user-defined
        // operator method — rewrite to `x = x.$add(y)`. The semantic model
        // exposes the operator on the assignment expression itself.
        if (
            _semantic.GetSymbolInfo(assign).Symbol is IMethodSymbol
            {
                MethodKind: MethodKind.UserDefinedOperator,
            } opMethod
        )
        {
            var opName = assign.Kind() switch
            {
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddAssignmentExpression => "add",
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.SubtractAssignmentExpression => "subtract",
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.MultiplyAssignmentExpression => "multiply",
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.DivideAssignmentExpression => "divide",
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.ModuloAssignmentExpression => "modulo",
                _ => null,
            };
            if (opName is not null)
            {
                var left = Extract(assign.Left);

                if (IsUnsupportedPlaceholder(left))
                    return left;

                var right = Extract(assign.Right);
                return new IrBinaryExpression(
                    left,
                    IrBinaryOp.Assign,
                    new IrCallExpression(
                        new IrMemberAccess(left, "$" + opName),
                        [new IrArgument(right)]
                    )
                );
            }
        }

        // `event += handler` / `event -= handler` — when the left side binds
        // to an event symbol, rewrite the compound assignment into a call to
        // the synthesized `event$add(handler)` / `event$remove(handler)`
        // helper the runtime emits for each event. The legacy
        // ExpressionTransformer handles this the same way.
        if (
            assign.Kind()
                is Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddAssignmentExpression
                    or Microsoft.CodeAnalysis.CSharp.SyntaxKind.SubtractAssignmentExpression
            && _semantic.GetSymbolInfo(assign.Left).Symbol is IEventSymbol evtSymbol
            && assign.Left is MemberAccessExpressionSyntax memberAccess
        )
        {
            var receiver = Extract(memberAccess.Expression);
            var suffix =
                assign.Kind() is Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddAssignmentExpression
                    ? "$add"
                    : "$remove";
            return new IrCallExpression(
                new IrMemberAccess(receiver, evtSymbol.Name + suffix),
                [new IrArgument(Extract(assign.Right))]
            );
        }

        var target = Extract(assign.Left);

        if (IsUnsupportedPlaceholder(target))
            return target;

        return TryExtractLiftedCompoundAssignment(assign, target)
            ?? TryExtractStringCompoundConcatenation(assign, target)
            ?? BuildAssignmentWithIntegerDivision(target, assign);
    }

    private IrExpression ExtractUnary(ExpressionSyntax node, bool isPrefix)
    {
        // `x!` (SuppressNullableWarning) is null-forgiving in C# but
        // has no JS / TS analogue — pass the operand through
        // untouched. UnaryPlus (`+x`) is a numeric identity; collapse
        // to the operand for the same reason.
        if (
            node is PostfixUnaryExpressionSyntax suppress
            && suppress.IsKind(SyntaxKind.SuppressNullableWarningExpression)
        )
            return Extract(suppress.Operand);
        if (node is PrefixUnaryExpressionSyntax plus && plus.IsKind(SyntaxKind.UnaryPlusExpression))
            return Extract(plus.Operand);

        if (
            IncrementedOperand(node) is { } incremented
            && TryReportTupleElementWrite(node, incremented) is { } unsupportedWrite
        )
            return unsupportedWrite;

        var (mappedOp, operand) = node switch
        {
            PrefixUnaryExpressionSyntax pre => (MapUnaryOp(pre.Kind()), pre.Operand),
            PostfixUnaryExpressionSyntax post => (MapUnaryOp(post.Kind()), post.Operand),
            _ => throw new ArgumentException("Not a unary expression", nameof(node)),
        };

        // `^n` outside an element access (`Index i = ^1;`), pointer `&x` /
        // `*p`: no IR operator models them.
        if (mappedOp is not { } op)
            return ReportUnsupported(node, node.Kind().ToString());

        var isIncrementOrDecrement = op is IrUnaryOp.Increment or IrUnaryOp.Decrement;

        if (!isIncrementOrDecrement)
            return TryExtractLiftedUnary(node, op, operand)
                ?? BuildUnary(op, Extract(operand), operand);

        // `receiver.Prop++` against an extension property rewrites to a
        // let-bound setter call so the receiver evaluates exactly once.
        // Prefix vs postfix is collapsed to the same shape — the MVP only
        // covers the statement-position use, which doesn't observe the
        // pre/post value distinction.
        if (
            operand is MemberAccessExpressionSyntax incMember
            && _semantic.GetSymbolInfo(incMember).Symbol is IPropertySymbol incProp
            && TryResolveExtensionPropertyLowering(incProp, incMember.Expression) is { } incLowering
        )
        {
            return BuildExtensionPropertyIncrement(incMember, incProp, incLowering, op);
        }
        // `receiver[i]++` against an extension indexer — same receiver-once
        // lowering as compound assignment, with the increment expressed as
        // `item$set(r, i, item$get(r, i) + 1)`. Statement-position only;
        // the postfix value isn't observable in MVP scope.
        if (
            operand is ElementAccessExpressionSyntax incIndexer
            && _semantic.GetSymbolInfo(incIndexer).Symbol is IPropertySymbol incIndexerProp
            && incIndexerProp.IsIndexer
            && TryResolveExtensionIndexerLowering(incIndexerProp, incIndexer)
                is { } incIndexerLowering
        )
        {
            return BuildExtensionIndexerIncrement(
                incIndexer,
                incIndexerProp,
                incIndexerLowering,
                op
            );
        }

        var extractedOperand = Extract(operand);

        if (IsUnsupportedPlaceholder(extractedOperand))
            return extractedOperand;

        return new IrUnaryExpression(op, extractedOperand, isPrefix);
    }

    /// <summary>
    /// The ordinary lowering of a prefix <c>-x</c>, <c>!x</c> or <c>~x</c>
    /// over an extracted operand, which a lifted operator reuses for its
    /// non-null case. Decimal negation is not a JS unary operator — the value
    /// is a Decimal instance — so it becomes <c>x.neg()</c>.
    /// </summary>
    private IrExpression BuildUnary(
        IrUnaryOp op,
        IrExpression operand,
        ExpressionSyntax operandSyntax
    )
    {
        return op is IrUnaryOp.Negate && IsDecimalOrNullableDecimalOperand(operandSyntax)
            ? new IrCallExpression(new IrMemberAccess(operand, "neg"), [])
            : new IrUnaryExpression(op, operand);
    }

    private static IrBinaryOp? MapBinaryOp(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.AddExpression => IrBinaryOp.Add,
            SyntaxKind.SubtractExpression => IrBinaryOp.Subtract,
            SyntaxKind.MultiplyExpression => IrBinaryOp.Multiply,
            SyntaxKind.DivideExpression => IrBinaryOp.Divide,
            SyntaxKind.ModuloExpression => IrBinaryOp.Modulo,
            SyntaxKind.EqualsExpression => IrBinaryOp.Equal,
            SyntaxKind.NotEqualsExpression => IrBinaryOp.NotEqual,
            SyntaxKind.LessThanExpression => IrBinaryOp.LessThan,
            SyntaxKind.LessThanOrEqualExpression => IrBinaryOp.LessThanOrEqual,
            SyntaxKind.GreaterThanExpression => IrBinaryOp.GreaterThan,
            SyntaxKind.GreaterThanOrEqualExpression => IrBinaryOp.GreaterThanOrEqual,
            SyntaxKind.LogicalAndExpression => IrBinaryOp.LogicalAnd,
            SyntaxKind.LogicalOrExpression => IrBinaryOp.LogicalOr,
            SyntaxKind.BitwiseAndExpression => IrBinaryOp.BitwiseAnd,
            SyntaxKind.BitwiseOrExpression => IrBinaryOp.BitwiseOr,
            SyntaxKind.ExclusiveOrExpression => IrBinaryOp.BitwiseXor,
            SyntaxKind.LeftShiftExpression => IrBinaryOp.LeftShift,
            SyntaxKind.RightShiftExpression => IrBinaryOp.RightShift,
            SyntaxKind.UnsignedRightShiftExpression => IrBinaryOp.UnsignedRightShift,
            SyntaxKind.CoalesceExpression => IrBinaryOp.NullCoalescing,
            // Unmapped binary kinds surface as null rather than silently
            // miscompiling as IrBinaryOp.Add; the caller reports MS0001 at
            // the expression.
            _ => null,
        };

    private static IrBinaryOp MapAssignmentOp(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.SimpleAssignmentExpression => IrBinaryOp.Assign,
            SyntaxKind.AddAssignmentExpression => IrBinaryOp.AddAssign,
            SyntaxKind.SubtractAssignmentExpression => IrBinaryOp.SubtractAssign,
            SyntaxKind.MultiplyAssignmentExpression => IrBinaryOp.MultiplyAssign,
            SyntaxKind.DivideAssignmentExpression => IrBinaryOp.DivideAssign,
            SyntaxKind.ModuloAssignmentExpression => IrBinaryOp.ModuloAssign,
            SyntaxKind.AndAssignmentExpression => IrBinaryOp.BitwiseAndAssign,
            SyntaxKind.OrAssignmentExpression => IrBinaryOp.BitwiseOrAssign,
            SyntaxKind.ExclusiveOrAssignmentExpression => IrBinaryOp.BitwiseXorAssign,
            SyntaxKind.LeftShiftAssignmentExpression => IrBinaryOp.LeftShiftAssign,
            SyntaxKind.RightShiftAssignmentExpression => IrBinaryOp.RightShiftAssign,
            SyntaxKind.UnsignedRightShiftAssignmentExpression =>
                IrBinaryOp.UnsignedRightShiftAssign,
            SyntaxKind.CoalesceAssignmentExpression => IrBinaryOp.NullCoalescingAssign,
            // Internal invariant: the arms above cover every assignment kind
            // Roslyn parses, so this only fires if a new kind is introduced.
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                $"Unsupported assignment syntax kind '{kind}'."
            ),
        };

    private static IrUnaryOp? MapUnaryOp(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.UnaryMinusExpression => IrUnaryOp.Negate,
            SyntaxKind.LogicalNotExpression => IrUnaryOp.LogicalNot,
            SyntaxKind.BitwiseNotExpression => IrUnaryOp.BitwiseNot,
            SyntaxKind.PreIncrementExpression or SyntaxKind.PostIncrementExpression =>
                IrUnaryOp.Increment,
            SyntaxKind.PreDecrementExpression or SyntaxKind.PostDecrementExpression =>
                IrUnaryOp.Decrement,
            _ => null,
        };

    /// <summary>
    /// Resolves a C# keyword type (<c>string</c>, <c>int</c>, etc.) used as an
    /// expression — typically the receiver of a static call like
    /// <c>string.Concat(…)</c> — to the underlying BCL type name. The semantic
    /// model knows what the keyword aliases to; we surface that as an
    /// <see cref="IrTypeReference"/> so the bridge can apply BCL mappings or
    /// fall back to the literal name.
    /// </summary>
    private IrExpression ExtractPredefinedType(PredefinedTypeSyntax pred)
    {
        var typeSymbol = _semantic.GetTypeInfo(pred).Type;
        if (typeSymbol is not null)
            return new IrTypeReference(typeSymbol.Name);
        // The semantic model failed to resolve the keyword — fall back to the
        // raw token text so downstream code at least sees something useful.
        return new IrTypeReference(pred.Keyword.ValueText);
    }

    // ── Member access / invocation ────────────────────────────────────────

    private IrExpression ExtractMemberAccess(QualifiedMemberSyntax member)
    {
        // `decimal.Zero` / `.One` / `.MinusOne` — no static counterpart in
        // decimal.js, so synthesize `new Decimal(N)` matching the legacy
        // BclMapper behavior. Similarly `decimal.Parse(s)` rewrites to
        // `new Decimal(s)`.
        var symbol = _semantic.GetSymbolInfo(member.Node).Symbol;
        if (symbol is IFieldSymbol { IsStatic: true } field)
        {
            var displayName = field.ContainingType?.ToDisplayString();
            if (displayName == "decimal")
            {
                var value = field.Name switch
                {
                    "Zero" => (object)0,
                    "One" => 1,
                    "MinusOne" => -1,
                    _ => null,
                };
                if (value is not null)
                    return new IrNewExpression(
                        new IrPrimitiveTypeRef(IrPrimitive.Decimal),
                        [new IrArgument(new IrLiteral(value, IrLiteralKind.Int32))]
                    );
            }
        }

        // Extension property read: `receiver.Prop` against a classic
        // `(this T)` extension property or a C# 14 `extension(T) { T Prop }`
        // block lowers to the module-level helper `prop$get(receiver)`.
        // Static extension property reads (`Type.Prop`) drop the receiver
        // — the helper has no parameter list at all.
        if (
            symbol is IPropertySymbol propSymbol
            && TryResolveExtensionPropertyLowering(propSymbol, member.Receiver) is { } propLowering
        )
        {
            var propArgs = propLowering.IsStatic
                ? Array.Empty<IrArgument>()
                : new[] { new IrArgument(Extract(member.Receiver)) };
            return BuildExtensionHelperCall(
                propLowering.HelperContainer,
                propLowering.HelperName,
                propLowering.EmittedName,
                propArgs,
                typeArguments: null
            );
        }

        // A qualified name naming a type — `Outer.Inner` (receiver of
        // `Outer.Inner.Create()` or `Outer.Kind.A`) or a namespace-qualified
        // `Acme.Domain.Kind` — is a type reference, not a static member read. Lowering
        // it as a member access would camel-case the type segment (`Outer.inner`) and
        // keep namespace segments, which are not value bindings (`Acme.domain`).
        if (symbol is INamedTypeSymbol qualifiedType)
            return TypeReferenceTo(qualifiedType);

        if (TryExtractDictionaryCount(member, symbol) is { } dictionaryCount)
            return dictionaryCount;

        var target = Extract(member.Receiver);
        var name = member.Name.Identifier.ValueText;

        if (TryExtractNullableValueMember(member, target) is { } nullableValueMember)
            return nullableValueMember;

        // Property-getter `[Emit]` template: a property whose getter (or the
        // property itself) carries `[Emit("…$0…")]` lowers its read to the
        // template with the receiver threaded as `$0` (e.g. `ISignal<T>.Value`
        // → `$0[0]()` → `count[0]()`). The invocation rewriter handles the
        // method/`Set` side; a getter read has no invocation syntax, so the
        // substitution is wired here. A coexisting `[Import]` on the member is
        // threaded as an external dependency so the helper module is imported.
        if (
            symbol is IPropertySymbol emitProp
            && GetPropertyEmitTemplate(emitProp) is { } propTemplate
        )
        {
            IReadOnlyList<IrExternalImport>? propExternalImports = null;
            if (SymbolHelper.GetImport(emitProp) is { } propImport)
                propExternalImports =
                [
                    new IrExternalImport(
                        propImport.Name,
                        propImport.From,
                        propImport.AsDefault,
                        propImport.Version
                    ),
                ];
            return new IrTemplateExpression(
                propTemplate,
                Receiver: null,
                [target],
                ExternalImports: propExternalImports
            );
        }

        // `[Inline]` fields and properties substitute their initializer
        // (or getter body) at every access site. When expansion
        // succeeds, the member access is replaced by the extracted
        // initializer expression; otherwise fall through to the normal
        // access path so diagnostics still surface at validation time.
        if (TryExpandInlineAccess(symbol) is { } inlined)
            return inlined;

        // PlainObject property fold: `new T(value).Prop` reduces to
        // `value` when `T` is `[PlainObject]` and `Prop` binds to that
        // constructor parameter. Inline method expansion can leave a
        // PlainObject literal in receiver position (e.g. the extension
        // receiver/parameter substitution) with an immediate field
        // access; folding keeps the lowered expression as compact as
        // the original source would have been.
        if (
            target
                is IrNewExpression
                {
                    IsPlainObject: true,
                    ParameterNames: { } parameterNames,
                    Arguments: { } newArgs,
                }
            && TryMatchPlainObjectMember(parameterNames, newArgs, name) is { } folded
        )
            return folded;

        var origin = BuildOrigin(_semantic.GetSymbolInfo(member.Node).Symbol);
        var memberAccess = new IrMemberAccess(target, name, origin);
        return WrapMethodGroupForDelegate(member.Node, symbol, memberAccess);
    }

    /// <summary>
    /// Method-group conversion to a delegate type. C# captures the
    /// instance receiver implicitly so any later invocation runs the
    /// body with the original <c>this</c>; JS does not — a bare
    /// <c>obj.method</c> reference loses its receiver as soon as it
    /// leaves the access expression. This wrapper normalizes the IR
    /// so the emitted TS preserves the C# semantics in two layers:
    /// <list type="bullet">
    ///   <item><c>.bind(receiver)</c> on every instance method group
    ///   assigned to a delegate slot. Static methods skip the bind
    ///   (no instance to capture). Type-qualified references
    ///   (<c>Class.StaticMember</c>) skip too.</item>
    ///   <item><c>bindReceiver(...)</c> trampoline added on top when
    ///   the delegate's first parameter carries <c>[This]</c>: the
    ///   trampoline rebinds JS <c>this</c> from the call-site
    ///   dispatcher into the method's first parameter, matching the
    ///   <c>[This]</c> lambda lowering.</item>
    /// </list>
    /// Plain identifier references (calls or non-delegate uses)
    /// fall through unchanged. The Dart target is opted out — Dart
    /// tear-offs auto-bind, so the runtime helpers and the JS-only
    /// <c>.bind</c> idiom would only get in the way.
    /// </summary>
    private IrExpression WrapMethodGroupForDelegate(
        ExpressionSyntax expression,
        ISymbol? symbol,
        IrExpression reference
    )
    {
        if (symbol is not IMethodSymbol method)
            return reference;

        if (
            _semantic.GetTypeInfo(expression).ConvertedType
            is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Delegate,
                DelegateInvokeMethod: IMethodSymbol invoke,
            }
        )
            return reference;

        if (IsDartTarget)
            return reference;

        var hasThisDelegate = HasThisParameter(invoke);

        var boundReference = ShouldBindInstanceReceiver(method, reference, out var instanceAccess)
            ? BuildBoundReference(instanceAccess)
            : reference;

        return hasThisDelegate
            ? new IrCallExpression(
                new IrIdentifier("bindReceiver"),
                [new IrArgument(boundReference)]
            )
            : boundReference;
    }

    private bool IsDartTarget => _target == Metano.Annotations.TargetLanguage.Dart;

    /// <summary>
    /// Returns the expression that should be passed to
    /// <c>.bind(...)</c> as the receiver. <c>base.Method</c> lowers
    /// to <c>super.method</c> in TypeScript, but <c>super</c> is not
    /// a value expression in JS/TS and cannot be a <c>.bind</c>
    /// argument. Substitute <c>this</c> in that position — the base
    /// method's body still runs against the current instance, which
    /// matches the C# semantics of <c>base</c>-qualified method
    /// groups (the dispatch is non-virtual but the receiver is
    /// still <c>this</c>). The substitution recurses through
    /// <see cref="IrMemberAccess"/> towers so a chain rooted in
    /// <c>base</c> (<c>base.Property.Method</c>) becomes
    /// <c>this.property.method.bind(this.property)</c> instead of
    /// the invalid <c>super.property</c>.
    /// </summary>
    private static IrExpression BindArgumentFor(IrExpression receiver) =>
        receiver switch
        {
            IrBaseExpression => new IrThisExpression(),
            IrMemberAccess access => access with { Target = BindArgumentFor(access.Target) },
            _ => receiver,
        };

    /// <summary>
    /// Builds the <c>.bind(receiver)</c> expression for an instance
    /// method group. When the receiver chain reduces to a pure IR
    /// shape (identifier, <c>this</c>, <c>base</c>, or a chain of
    /// <see cref="IrMemberAccess"/> nodes rooted at one of those)
    /// emits the simple <c>obj.method.bind(obj)</c>. The shape
    /// predicate matches what <see cref="IrEvaluationTraits.IsReusableReceiver"/>
    /// already accepts elsewhere in the extractor, so a property
    /// getter that observably mutates state is treated as pure
    /// here too — symbol-aware purity tracking is a separate
    /// follow-up. Receivers whose IR shape includes a call
    /// expression, indexer, or other non-member node are wrapped
    /// in an IIFE arrow that captures the receiver in a temporary
    /// so the chain is evaluated exactly once:
    /// <c>((__r) => __r.method.bind(__r))(originalReceiver)</c>.
    /// </summary>
    private static IrExpression BuildBoundReference(IrMemberAccess instanceAccess)
    {
        var receiver = instanceAccess.Target;

        if (IrEvaluationTraits.IsReusableReceiver(receiver))
            return new IrCallExpression(
                new IrMemberAccess(instanceAccess, "bind"),
                [new IrArgument(BindArgumentFor(receiver))]
            );

        const string TempName = "__r";
        var tempRef = new IrIdentifier(TempName);
        var rebound = instanceAccess with { Target = tempRef };
        var lambda = new IrLambdaExpression(
            Parameters: [new IrParameter(TempName, new IrUnknownTypeRef(), HasExplicitType: false)],
            ReturnType: null,
            Body:
            [
                new IrReturnStatement(
                    new IrCallExpression(
                        new IrMemberAccess(rebound, "bind"),
                        [new IrArgument(tempRef)]
                    )
                ),
            ]
        );
        return new IrCallExpression(lambda, [new IrArgument(receiver)]);
    }

    /// <summary>
    /// True when an instance method-group reference must
    /// <c>.bind(receiver)</c> the underlying member access. Static
    /// methods and type-qualified references skip the bind because
    /// there is no instance to capture; non-member-access shapes
    /// (bare identifiers from local functions, locals, parameters)
    /// also skip — there is nothing to bind onto.
    /// </summary>
    private static bool ShouldBindInstanceReceiver(
        IMethodSymbol method,
        IrExpression reference,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IrMemberAccess? memberAccess
    )
    {
        memberAccess = null;

        if (method.IsStatic)
            return false;

        if (reference is not IrMemberAccess { Target: { } receiver } access)
            return false;

        if (receiver is IrTypeReference)
            return false;

        memberAccess = access;
        return true;
    }

    private static IrExpression? TryMatchPlainObjectMember(
        IReadOnlyList<string> parameterNames,
        IReadOnlyList<IrArgument> args,
        string memberName
    )
    {
        for (var i = 0; i < parameterNames.Count && i < args.Count; i++)
        {
            if (string.Equals(parameterNames[i], memberName, StringComparison.Ordinal))
                return args[i].Value;
        }
        return null;
    }

    /// <summary>
    /// Reads the raw <c>[Emit("…")]</c> template from a property read site.
    /// The attribute is matched on the property symbol first, then on its
    /// getter accessor, so a binding may place it on either. Returns
    /// <see langword="null"/> when no template is present.
    /// </summary>
    private static string? GetPropertyEmitTemplate(IPropertySymbol property)
    {
        return ReadEmitTemplate(property) ?? ReadEmitTemplate(property.GetMethod);

        static string? ReadEmitTemplate(ISymbol? symbol) =>
            symbol
                ?.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name is "EmitAttribute" or "Emit")
                ?.ConstructorArguments.FirstOrDefault()
                .Value as string;
    }

    private IrExpression? TryExpandInlineAccess(ISymbol? symbol)
    {
        if (symbol is null || !SymbolHelper.IsInlineMember(symbol))
            return null;
        if (!_inlineExpanding.Add(symbol))
            return null;
        try
        {
            // A method symbol reaching this path is being used as a value
            // (delegate conversion, method-group reference, etc.) — never as
            // a direct invocation. Materialize the body as a lambda so the
            // call site has something to pass around.
            if (symbol is IMethodSymbol method)
                return TryMaterializeInlineMethodAsLambda(method);

            var initializer = TryFindInlineInitializer(symbol);
            if (initializer is null)
                return null;

            var semanticModel = SymbolHelper.TryGetSemanticModel(
                _semantic.Compilation,
                initializer.SyntaxTree
            );
            if (semanticModel is null)
                return null;

            // Reuse the declaring syntax tree's SemanticModel so
            // constant folding + symbol resolution inside the
            // initializer reflect the declaration site, not the call
            // site. For source ProjectReferences the syntax tree
            // belongs to the referenced CompilationReference, so look
            // there before giving up. The cycle-tracking set is shared
            // with the nested extractor so a transitive reference back
            // to the original member bails out instead of recursing.
            var extractor = new IrExpressionExtractor(
                semanticModel,
                _originResolver,
                _target,
                _inlineExpanding,
                inlineParameterSubs: null
            );
            return extractor.Extract(initializer);
        }
        finally
        {
            _inlineExpanding.Remove(symbol);
        }
    }

    private static ExpressionSyntax? TryFindInlineInitializer(ISymbol symbol)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            switch (reference.GetSyntax())
            {
                case VariableDeclaratorSyntax declarator
                    when declarator.Initializer?.Value is { } fieldInit:
                    return fieldInit;
                case PropertyDeclarationSyntax prop
                    when prop.ExpressionBody?.Expression is { } arrow:
                    return arrow;
                case PropertyDeclarationSyntax prop
                    when prop.AccessorList?.Accessors is { } accessors:
                    foreach (var accessor in accessors)
                    {
                        if (
                            accessor.IsKind(SyntaxKind.GetAccessorDeclaration)
                            && accessor.ExpressionBody?.Expression is { } body
                        )
                            return body;
                    }
                    break;
            }
        }
        return null;
    }

    private IrExpression ExtractInvocationCore(InvocationExpressionSyntax inv)
    {
        if (TryExtractConstantInvocation(inv) is { } constant)
            return constant;

        var symbol = _semantic.GetSymbolInfo(inv).Symbol as IMethodSymbol;
        ReportTupleValueLookup(inv, symbol);

        // `Nullable<T>.GetValueOrDefault` lives with the other `Nullable<T>`
        // members rather than in `IntrinsicBclLoweringTable`: a side-effecting
        // default must be bound once (`BindOnce`), which the table's
        // `InvocationLoweringHelpers` facade does not expose.
        if (TryExtractNullableGetValueOrDefault(inv, symbol) is { } valueOrDefault)
            return valueOrDefault;

        // LINQ chain detection (#20 pipe lowering). When this invocation
        // is the OUTERMOST stage of a System.Linq.Enumerable / Queryable
        // call chain whose every step has a pipe-runtime counterpart,
        // fold the entire chain into a single IrLinqChain — the bridge
        // emits one `linq(source, op1(...), op2(...), opN(...))` call.
        // Inner stages are walked here via syntax (not via Extract
        // recursion) so the legacy fluent emission path stays clean.
        if (TryExtractLinqChain(inv, symbol) is { } linqChain)
            return linqChain;

        // Non-LINQ invocation with an explicit [Queryable] / Expression<Func<…>>
        // signal on a lambda argument (#218). The walker runs purely for
        // MS0024 reporting — the resulting meta is discarded because the
        // surrounding IR shape has no slot for it yet. LINQ chains never
        // reach here (the check above returned early), and inner LINQ
        // stages are consumed structurally by BuildLinqChain, so this
        // path cannot double-report a lambda the chain walker already
        // covered.
        ReportQueryableDiagnosticsForExplicitOptIn(inv, symbol);

        // Chain dispatch (#221 — invocation rewriter extraction).
        // Order is fixed by the array initialiser in the ctor; each
        // rewriter returns non-null only when its symbol-shape gate
        // matches.
        var invocationContext = new Invocation.InvocationRewriteContext(
            inv,
            symbol,
            _semantic,
            Extract,
            ExtractArgument
        );
        foreach (var rewriter in _invocationRewriters)
        {
            if (rewriter.Rewrite(invocationContext) is { } rewritten)
                return rewritten;
        }

        var target = Extract(inv.Expression);
        var args = inv.ArgumentList.Arguments.Select(ExtractArgument).ToList();
        ApplyParamsSpread(args, symbol, inv.ArgumentList.Arguments);
        IReadOnlyList<IrTypeRef>? typeArguments = null;
        if (symbol is { TypeArguments.Length: > 0 })
            typeArguments = symbol
                .TypeArguments.Select(t => IrTypeRefMapper.Map(t, _originResolver, _target))
                .ToList();

        // Direct invocation of a `[This]`-bearing delegate from C#:
        // `listener(button, "click")` lowers to
        // `listener.call(button, "click")` so the JS dispatch sets
        // `this` to the first argument before the runtime trampoline
        // (bindReceiver) forwards it back. Without `.call`, the
        // delegate fires with `this === undefined` and the body's
        // receiver parameter receives whatever the caller passed in
        // a normal positional slot, which the runtime helper
        // expects to be `this`.
        if (
            symbol is { MethodKind: MethodKind.DelegateInvoke }
            && symbol.Parameters.Length > 0
            && SymbolHelper.HasThis(symbol.Parameters[0])
            && args.Count > 0
            && IsSafeForCallRewrite(inv)
        )
        {
            var receiverIndex = FindReceiverArgumentIndex(symbol, inv.ArgumentList);
            if (receiverIndex >= 0)
            {
                var receiver = args[receiverIndex].Value;
                var rest = args.Where((_, i) => i != receiverIndex).Select(a => a).ToList();
                return new IrCallExpression(
                    new IrMemberAccess(target, "call"),
                    [new IrArgument(receiver), .. rest]
                );
            }
        }

        if (symbol is not null && SymbolHelper.HasObjectArgs(symbol))
        {
            args =
                TryWrapObjectArgsViaOperation(inv, symbol)
                ?? WrapInObjectArgsFromArgs(args, symbol);
        }
        else if (symbol is not null && args.Any(a => a.Name is not null))
        {
            args = NormalizeArguments(args, symbol).ToList();
        }

        return new IrCallExpression(target, args, typeArguments, BuildOrigin(symbol));
    }

    /// <summary>
    /// Builds the <c>[ObjectArgs]</c> object-literal payload from the
    /// Roslyn-bound <see cref="IInvocationOperation"/>. The operation
    /// already pairs every source argument (positional, named, mixed,
    /// <c>params</c>-expanded) with its declaring parameter, so the
    /// expansion sees:
    /// <list type="bullet">
    ///   <item><c>params T[]</c> trailing args folded into a synthesized
    ///   <c>IArrayCreationOperation</c> — we pluck its elements and emit
    ///   one <see cref="IrArrayLiteral"/>.</item>
    ///   <item>Default-filled slots (<c>ArgumentKind.DefaultValue</c>)
    ///   skipped so the emitted object literal stays minimal.</item>
    ///   <item>Explicit values whose literal matches the parameter's
    ///   default — same elision rule as the legacy path.</item>
    /// </list>
    /// Returns <c>null</c> when the operation isn't available; the
    /// caller falls back to the args-only path.
    /// </summary>
    private List<IrArgument>? TryWrapObjectArgsViaOperation(
        InvocationExpressionSyntax inv,
        IMethodSymbol symbol
    )
    {
        if (_semantic.GetOperation(inv) is not IInvocationOperation op)
            return null;
        var properties = new List<(string Name, IrExpression Value)>();
        foreach (var arg in op.Arguments)
        {
            if (arg.Parameter is null)
                continue;
            if (arg.ArgumentKind == ArgumentKind.DefaultValue)
                continue;

            IrExpression value;
            if (
                arg.ArgumentKind == ArgumentKind.ParamArray
                && arg.Value is IArrayCreationOperation arrayOp
            )
            {
                var elements = arrayOp.Initializer?.ElementValues ?? default;
                if (elements.IsDefaultOrEmpty)
                    continue;
                value = new IrArrayLiteral(
                    elements
                        .Select(e =>
                            e.Syntax is ExpressionSyntax es
                                ? Extract(es)
                                : new IrUnsupportedExpression(e.Kind.ToString())
                        )
                        .ToList()
                );
            }
            else if (arg.Value.Syntax is ExpressionSyntax valueSyntax)
            {
                value = Extract(valueSyntax);
            }
            else
            {
                continue;
            }

            if (
                arg.Parameter.HasExplicitDefaultValue
                && value is IrLiteral lit
                && IsLiteralEqualToDefault(lit, arg.Parameter.ExplicitDefaultValue)
            )
                continue;

            properties.Add((arg.Parameter.Name, value));
        }
        return new List<IrArgument> { new(new IrObjectLiteral(properties)) };
    }

    private List<IrArgument> WrapInObjectArgsFromArgs(
        IReadOnlyList<IrArgument> args,
        IMethodSymbol symbol
    )
    {
        var normalized = args.Any(a => a.Name is not null) ? NormalizeForArgs(args, symbol) : args;
        return WrapInObjectArgs(normalized, symbol);
    }

    private IReadOnlyList<IrArgument> NormalizeForArgs(
        IReadOnlyList<IrArgument> args,
        IMethodSymbol symbol
    )
    {
        var byName = args.Where(a => a.Name is not null).ToDictionary(a => a.Name!, a => a);
        var positional = args.TakeWhile(a => a.Name is null).ToList();
        var result = new List<IrArgument>(symbol.Parameters.Length);
        for (var i = 0; i < symbol.Parameters.Length; i++)
        {
            if (i < positional.Count)
            {
                result.Add(positional[i]);
                continue;
            }
            var p = symbol.Parameters[i];
            if (byName.TryGetValue(p.Name, out var named))
            {
                result.Add(named);
                continue;
            }
            result.Add(new IrArgument(BuildDefaultArgument(p)));
        }
        return result;
    }

    /// <summary>
    /// Collapses an ordered argument list into a single
    /// <see cref="IrObjectLiteral"/> whose property names come from
    /// the resolved method's parameter names. Argument slots whose
    /// value matches the parameter's explicit default literal are
    /// dropped so the emitted object literal stays minimal.
    /// </summary>
    private static List<IrArgument> WrapInObjectArgs(
        IReadOnlyList<IrArgument> orderedArgs,
        IMethodSymbol symbol
    ) =>
        new()
        {
            new IrArgument(new IrObjectLiteral(BuildObjectArgsProperties(symbol, orderedArgs))),
        };

    private static bool IsLiteralEqualToDefault(IrLiteral literal, object? defaultValue)
    {
        if (literal.Value is null)
            return defaultValue is null;
        if (defaultValue is null)
            return false;
        return literal.Value.Equals(defaultValue);
    }

    /// <summary>
    /// Guards the direct-invocation <c>.call(...)</c> rewrite against
    /// receiver expressions whose precedence does not survive the
    /// property-access wrap. <c>(a ?? b)(args)</c> /
    /// <c>(cond ? a : b)(args)</c> would print as
    /// <c>a ?? b.call(args)</c> / <c>cond ? a : b.call(args)</c>
    /// without an extra paren, changing the parse. Until the IR
    /// gains a parenthesizing wrapper, the rewrite skips these
    /// shapes and falls back to the plain call (the runtime
    /// `bindReceiver` trampoline still receives `this === undefined`,
    /// matching the legacy behavior — a smaller, documented gap).
    /// </summary>
    private static bool IsSafeForCallRewrite(InvocationExpressionSyntax inv)
    {
        var target = inv.Expression;
        while (target is ParenthesizedExpressionSyntax paren)
            target = paren.Expression;
        return target
            is not (
                ConditionalExpressionSyntax
                or BinaryExpressionSyntax
                or AssignmentExpressionSyntax
                or AwaitExpressionSyntax
            );
    }

    /// <summary>
    /// Returns the syntactic argument index that corresponds to the
    /// first parameter of <paramref name="symbol"/> (the
    /// <c>[This]</c> receiver). Honors named arguments by matching
    /// <see cref="ArgumentSyntax.NameColon"/> against the parameter
    /// name; positional arguments fall through to index 0. Returns
    /// <c>-1</c> when the receiver slot cannot be located, which
    /// signals the caller to skip the <c>.call(...)</c> rewrite
    /// and emit the plain delegate invocation (still semantically
    /// off, but no worse than the pre-rewrite behavior).
    /// </summary>
    private static int FindReceiverArgumentIndex(
        IMethodSymbol symbol,
        ArgumentListSyntax argumentList
    )
    {
        var receiverName = symbol.Parameters[0].Name;
        for (var i = 0; i < argumentList.Arguments.Count; i++)
        {
            var arg = argumentList.Arguments[i];
            if (arg.NameColon?.Name.Identifier.ValueText == receiverName)
                return i;
        }
        if (argumentList.Arguments.All(a => a.NameColon is null))
            return 0;
        // Mixed positional + named where the named arg targets a
        // non-receiver slot: cannot safely identify the receiver
        // syntactic position. Bail out.
        return -1;
    }

    private static bool IsPrimitiveBackedBrandedValueRead(ISymbol symbol) =>
        SymbolHelper.IsBrandedValueMember(symbol)
        && SymbolHelper.GetBrandedValueType(symbol.ContainingType.OriginalDefinition)
            is { } valueType
        && IrTypeRefMapper.Map(valueType).IsBrandablePrimitive();

    private IrMemberOrigin? BuildOrigin(ISymbol? symbol)
    {
        if (symbol?.ContainingType is null)
            return null;
        var declaringTypeName = symbol.ContainingType.GetStableFullName();
        // Flag enum members so backends can preserve the source-casing —
        // TypeScript enums (numeric or string-backed) expose members with
        // their original PascalCase, while ordinary static members get the
        // target's normal member-casing policy.
        var isEnumMember =
            symbol.ContainingType.TypeKind == TypeKind.Enum && symbol is IFieldSymbol;
        var isStringEnumMember = isEnumMember && SymbolHelper.HasStringEnum(symbol.ContainingType);
        var isBrandedMethod =
            symbol is IMethodSymbol && SymbolHelper.HasBranded(symbol.ContainingType);
        // Reading the wrapped value of a primitive-backed `[Branded]` receiver
        // (`id.Value`) is the identity at runtime — the brand IS the primitive
        // (ADR-0005). A wrapper over anything else falls back to a class, so
        // its `.value` read must stay.
        var isBrandedValueMember = IsPrimitiveBackedBrandedValueRead(symbol);
        var isPlainObjectInstanceMethod =
            symbol is IMethodSymbol { IsStatic: false, MethodKind: MethodKind.Ordinary }
            && SymbolHelper.HasPlainObject(symbol.ContainingType);
        // `[Name("x")]` (target-aware) is resolved once here so backends
        // consult the emitted name instead of re-scanning attributes.
        var emittedName = SymbolHelper.GetNameOverride(symbol, _target);
        // `[External]` (TS-specific) and `[NoContainer]`
        // (cross-target) both cause static member access to flatten
        // to a bare identifier at the call site, but they express
        // different intents (runtime-provided stub vs. compile-time
        // sugar container). The flags stay distinct so later slices
        // can diverge their lowering paths without churn; today's
        // bridge honors either to drop the enclosing type reference.
        var isDeclaringTypeExternal = SymbolHelper.HasExternal(symbol.ContainingType);
        // `[ExportedAsModule]` flattens like `[NoContainer]` at call sites (#286).
        var isDeclaringTypeNoContainer = symbol.ContainingType.IsContainerless();
        // `[JsCallable]` interface `Invoke(…)` call → the receiver IS the JS
        // function, so the call lowers to `recv(args)`. A `[JsTuple]`
        // positional member or C# tuple element read carries its position,
        // which each backend reads its own way. Both reuse the member
        // origin dispatch channel so the backends can branch without
        // re-reading the Roslyn symbol.
        var isJsCallableInvoke =
            symbol is IMethodSymbol invokeMethod && SymbolHelper.IsJsCallableInvoke(invokeMethod);
        var tupleElement = SymbolHelper.GetPositionalTupleElement(symbol);
        return new IrMemberOrigin(
            declaringTypeName,
            symbol.Name,
            symbol.IsStatic,
            isEnumMember,
            isBrandedMethod,
            EmittedName: emittedName,
            IsPlainObjectInstanceMethod: isPlainObjectInstanceMethod,
            IsStringEnumMember: isStringEnumMember,
            IsDeclaringTypeExternal: isDeclaringTypeExternal,
            IsDeclaringTypeNoContainer: isDeclaringTypeNoContainer,
            IsJsCallableInvoke: isJsCallableInvoke,
            TupleElement: tupleElement.Kind,
            TupleIndex: tupleElement.Index,
            IsBrandedValueMember: isBrandedValueMember,
            IsTranspiledDataMember: IsTranspiledDataMember(symbol),
            ParameterTypes: OverloadParameterTypes(symbol),
            IsInitOnly: symbol is IPropertySymbol { SetMethod.IsInitOnly: true },
            CompanionOwner: isPlainObjectInstanceMethod
                ? TypeReferenceTo(symbol.ContainingType)
                : null,
            IsForeignMember: SymbolHelper.IsForeignMember(symbol, _semantic.Compilation)
        );
    }

    /// <summary>
    /// A field or property, other than an enum member or an indexer, whose
    /// declaring type the toolchain declares (see
    /// <see cref="IrMemberOrigin.IsTranspiledDataMember"/> and
    /// <see cref="SymbolHelper.IsToolchainDeclared"/>).
    /// </summary>
    private bool IsTranspiledDataMember(ISymbol? symbol) =>
        symbol is not null
        && IsDataMember(symbol)
        && SymbolHelper.IsToolchainDeclared(symbol.ContainingType, _semantic.Compilation.Assembly);

    private static bool IsDataMember(ISymbol symbol) =>
        symbol
            is IPropertySymbol { IsIndexer: false }
                or IFieldSymbol { ContainingType.TypeKind: not TypeKind.Enum };

    /// <summary>
    /// The stable full names of the parameter types of the overload
    /// <paramref name="symbol"/> binds to, read from its original definition
    /// so a generic parameter keeps its open shape (<c>IEnumerable&lt;T&gt;</c>);
    /// <c>null</c> when the symbol is not a method.
    /// </summary>
    private static IReadOnlyList<string>? OverloadParameterTypes(ISymbol symbol) =>
        symbol is IMethodSymbol method
            ? method
                .OriginalDefinition.Parameters.Select(parameter =>
                    parameter.Type.GetStableFullName()
                )
                .ToList()
            : null;

    private IrExpression ExtractElementAccess(ElementAccessExpressionSyntax elem)
    {
        // Extension indexer read — `bag[i]` against a C# 14 extension
        // indexer lowers to a flat `item$get(receiver, i)` helper call so
        // the call site has no surviving bracket access on the receiver
        // type. Falls through to the legacy bracket form for arrays /
        // dictionaries / non-extension indexers.
        if (
            _semantic.GetSymbolInfo(elem).Symbol is IPropertySymbol indexerProp
            && indexerProp.IsIndexer
            && TryResolveExtensionIndexerLowering(indexerProp, elem) is { } readLowering
        )
        {
            return BuildExtensionIndexerGetCall(elem, indexerProp, readLowering);
        }

        return ExtractElementRead(elem, elem.Expression, elem.ArgumentList);
    }

    /// <summary>
    /// Bracket read <c>receiver[index]</c>, shared by an element access and
    /// the leading <c>?[index]</c> of a null-conditional access
    /// (<paramref name="node"/> is either), index-from-end and ranges
    /// included.
    /// </summary>
    private IrExpression ExtractElementRead(
        ExpressionSyntax node,
        ExpressionSyntax receiverSyntax,
        BracketedArgumentListSyntax arguments
    )
    {
        if (TryExtractIndexOrRangeAccess(node, receiverSyntax, arguments) is { } indexOrRangeAccess)
            return indexOrRangeAccess;

        var target = Extract(receiverSyntax);

        // Treat the first argument as the index; multi-arg indexers are uncommon.
        var index =
            arguments.Arguments.Count > 0
                ? Extract(arguments.Arguments[0].Expression)
                : new IrLiteral(0, IrLiteralKind.Int32);

        return new IrElementAccess(target, index, MapExpressionType(receiverSyntax));
    }

    /// <summary>
    /// The IR type of a receiver expression. A <c>?[index]</c> receiver is
    /// typed nullable (<c>Dictionary&lt;K, V&gt;?</c>), but the read only runs
    /// on the non-null value, so the annotation is dropped.
    /// </summary>
    private IrTypeRef? MapExpressionType(ExpressionSyntax expression) =>
        _semantic.GetTypeInfo(expression).Type is { } type
            ? IrTypeRefMapper.Map(
                type.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
                _originResolver,
                _target
            )
            : null;

    // ── Lambdas ──────────────────────────────────────────────────────────

    /// <summary>
    /// <c>void</c> for a lambda bound to a <c>void</c> delegate, whose
    /// expression body's value is discarded; <c>null</c> (not resolved)
    /// otherwise.
    /// </summary>
    private IrTypeRef? VoidReturnTypeOf(LambdaExpressionSyntax lambda) =>
        _semantic.GetSymbolInfo(lambda).Symbol
            is IMethodSymbol { ReturnsVoid: true, IsAsync: false }
            ? new IrPrimitiveTypeRef(IrPrimitive.Void)
            : null;

    private IrParameter BuildLambdaParameter(ParameterSyntax parameter) =>
        new(
            parameter.Identifier.ValueText,
            ResolveParameterType(parameter),
            HasExplicitType: parameter.Type is not null
        );

    private IrExpression ExtractSimpleLambda(SimpleLambdaExpressionSyntax lambda)
    {
        var receiverType = ResolveLambdaReceiverType(lambda);
        var parameter = BuildLambdaParameter(lambda.Parameter);
        var body = ExtractLambdaBody(lambda.Body);
        return new IrLambdaExpression(
            [parameter],
            ReturnType: VoidReturnTypeOf(lambda),
            Body: body,
            IsAsync: lambda.AsyncKeyword.ValueText == "async",
            UsesThis: receiverType is not null,
            ThisType: receiverType
        );
    }

    private IrExpression ExtractParenthesizedLambda(ParenthesizedLambdaExpressionSyntax lambda)
    {
        var receiverType = ResolveLambdaReceiverType(lambda);
        var parameters = lambda.ParameterList.Parameters.Select(BuildLambdaParameter).ToList();
        var body = ExtractLambdaBody(lambda.Body);
        return new IrLambdaExpression(
            parameters,
            ReturnType: VoidReturnTypeOf(lambda),
            Body: body,
            IsAsync: lambda.AsyncKeyword.ValueText == "async",
            UsesThis: receiverType is not null,
            ThisType: receiverType
        );
    }

    /// <summary>
    /// Returns the receiver type for a lambda whose target delegate
    /// declares <c>[This]</c> on its first parameter, so the TS
    /// bridge can wrap the emitted arrow in a <c>bindReceiver</c>
    /// runtime helper call. Returns <c>null</c> otherwise — the
    /// lambda emits as a plain arrow.
    /// <para>
    /// The arrow's first parameter stays in the positional list so
    /// the runtime wrapper can forward the dispatcher's JS
    /// <c>this</c> into it; the lambda body never mentions the
    /// keyword <c>this</c> itself, so an outer <c>this</c> captured
    /// from the enclosing C# class continues to resolve through
    /// lexical closure (<c>const self = this</c> is emitted by the
    /// runtime helper, not by the generated lambda).
    /// </para>
    /// </summary>
    private IrTypeRef? ResolveLambdaReceiverType(ExpressionSyntax lambdaSyntax)
    {
        if (
            _semantic.GetTypeInfo(lambdaSyntax).ConvertedType
            is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Delegate,
                DelegateInvokeMethod: IMethodSymbol invoke,
            }
        )
            return null;
        if (invoke.Parameters.Length == 0)
            return null;
        var receiverParam = invoke.Parameters[0];
        if (!SymbolHelper.HasThis(receiverParam))
            return null;
        return IrTypeRefMapper.Map(receiverParam.Type, _originResolver, _target);
    }

    private IrTypeRef ResolveParameterType(ParameterSyntax parameter)
    {
        // Lambda params often have no explicit type; Roslyn infers them from context.
        if (parameter.Type is not null)
        {
            var explicitType = _semantic.GetTypeInfo(parameter.Type).Type;
            if (explicitType is not null)
                return IrTypeRefMapper.Map(explicitType, _originResolver, _target);
        }
        if (
            _semantic.GetDeclaredSymbol(parameter) is IParameterSymbol paramSymbol
            && paramSymbol.Type is not null
        )
            return IrTypeRefMapper.Map(paramSymbol.Type, _originResolver, _target);
        return new IrUnknownTypeRef();
    }

    /// <summary>
    /// Lambda bodies come as either an expression (<c>x => x + 1</c>) or a block
    /// (<c>x => { ... return x + 1; }</c>). We normalize to a list of
    /// <see cref="IrStatement"/> so the IR carries a single uniform shape.
    /// </summary>
    private IReadOnlyList<IrStatement> ExtractLambdaBody(CSharpSyntaxNode body) =>
        body switch
        {
            BlockSyntax block => new IrStatementExtractor(_semantic, _originResolver).ExtractBody(
                block,
                arrow: null,
                isVoid: false
            ),
            ExpressionSyntax expr => LowerOutArgumentCalls(new IrReturnStatement(Extract(expr))),
            _ => [new IrUnsupportedStatement(body.Kind().ToString())],
        };

    // ── String interpolation ─────────────────────────────────────────────

    private IrExpression ExtractInterpolatedString(InterpolatedStringExpressionSyntax interp)
    {
        var parts = new List<IrInterpolationPart>();
        foreach (var content in interp.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    parts.Add(new IrInterpolationText(CookedInterpolationText(text)));
                    break;
                case InterpolationSyntax expr:
                    var formatSpec = expr.FormatClause?.FormatStringToken.ValueText;
                    parts.Add(
                        new IrInterpolationExpression(ExtractInterpolationHole(expr), formatSpec)
                    );
                    break;
            }
        }
        return new IrStringInterpolation(parts);
    }

    /// <summary>
    /// The runtime value of an interpolated string's literal segment. The
    /// token's <c>ValueText</c> decodes character escapes but keeps the
    /// doubled-brace escapes (<c>{{</c> / <c>}}</c>) of a non-raw
    /// interpolation, so the bound operation's constant is read instead: it
    /// is exactly the text the segment contributes in the regular, verbatim
    /// and raw forms. A string converted to an interpolated-string handler
    /// binds the segment as the <c>AppendLiteral</c> argument (a literal
    /// operation) instead of an interpolated-text operation; both carry the
    /// cooked constant.
    /// </summary>
    private string CookedInterpolationText(InterpolatedStringTextSyntax text) =>
        _semantic.GetOperation(text) switch
        {
            IInterpolatedStringTextOperation
            {
                Text.ConstantValue: { HasValue: true, Value: string cooked },
            } => cooked,
            ILiteralOperation { ConstantValue: { HasValue: true, Value: string cooked } } => cooked,
            _ => text.TextToken.ValueText,
        };

    // ── Object creation ──────────────────────────────────────────────────

    /// <summary>
    /// Turns one Roslyn argument node into an <see cref="IrArgument"/>,
    /// capturing its source-side name when the caller used the
    /// <c>Name: value</c> shorthand so backends that care (Dart's named
    /// arguments, the TS dispatcher's reordering pass) can reconstruct the
    /// original intent.
    /// </summary>
    private IrArgument ExtractArgument(ArgumentSyntax argument) =>
        argument.RefOrOutKeyword.IsKind(SyntaxKind.None)
            ? new(Extract(argument.Expression), argument.NameColon?.Name.Identifier.ValueText)
            : ExtractByReferenceArgument(argument);

    /// <summary>
    /// Lowers an extension-method call site (classic <c>(this T)</c>
    /// reduced form or C# 14 <c>extension(T r) { … }</c> block) into
    /// the module-level helper call <c>method(receiver, args)</c>.
    /// Returns <see langword="null"/> when the invocation isn't an
    /// extension call so the rewriter chain falls through to the
    /// regular call lowering. Without the rewrite the receiver
    /// carries a phantom property access (<c>receiver.method()</c>)
    /// at runtime — no such member exists on the receiver type
    /// because the helper lives on the extension's static container.
    /// Static extension members dispatch through
    /// <c>Type.Member(args)</c> — the receiver is the type itself,
    /// not a value, so the helper takes only the syntactic
    /// arguments.
    /// </summary>
    internal IrExpression? TryRewriteExtensionCall(
        InvocationExpressionSyntax inv,
        IMethodSymbol extensionCallee
    )
    {
        if (QualifiedMemberSyntax.From(inv.Expression) is not { } extensionAccess)
            return null;

        if (
            TryResolveExtensionLowering(extensionCallee, extensionAccess.Receiver)
            is not { } extLowering
        )
            return null;

        var extensionArgs = inv.ArgumentList.Arguments.Select(ExtractArgument).ToList();
        // The reduced `extensionCallee` is the receiver-less view of the
        // method (parameter count matches the syntax arg count); use it
        // for argument-shape decisions so params spreading and named
        // argument normalization key off the right parameter list.
        ApplyParamsSpread(extensionArgs, extensionCallee, inv.ArgumentList.Arguments);
        if (extensionArgs.Any(a => a.Name is not null))
            extensionArgs = NormalizeArguments(extensionArgs, extensionCallee).ToList();

        IReadOnlyList<IrTypeRef>? extensionTypeArgs = null;
        if (extensionCallee is { TypeArguments.Length: > 0 })
            extensionTypeArgs = extensionCallee
                .TypeArguments.Select(t => IrTypeRefMapper.Map(t, _originResolver, _target))
                .ToList();

        if (extLowering.IsStatic)
        {
            return BuildExtensionHelperCall(
                extLowering.HelperContainer,
                extLowering.HelperName,
                extLowering.EmittedName,
                extensionArgs,
                extensionTypeArgs
            );
        }

        var receiver = Extract(extensionAccess.Receiver);
        return BuildExtensionHelperCall(
            extLowering.HelperContainer,
            extLowering.HelperName,
            extLowering.EmittedName,
            [new IrArgument(receiver), .. extensionArgs],
            extensionTypeArgs
        );
    }

    /// <summary>
    /// Builds the canonical IR shape for an extension-helper call:
    /// <c>helper(receiver, args)</c> emitted as a static call on the
    /// helper's enclosing container. Carries the
    /// <see cref="IrMemberOrigin.IsDeclaringTypeNoContainer"/> invariant so
    /// the bridge drops the type qualifier and the import collector
    /// imports the helper directly. Shared by the method-call and
    /// property-read rewrites so the contract has a single source of
    /// truth.
    /// </summary>
    private static IrCallExpression BuildExtensionHelperCall(
        INamedTypeSymbol helperContainer,
        string helperName,
        string? emittedName,
        IReadOnlyList<IrArgument> arguments,
        IReadOnlyList<IrTypeRef>? typeArguments
    )
    {
        var origin = new IrMemberOrigin(
            DeclaringTypeFullName: helperContainer.GetStableFullName(),
            MemberName: helperName,
            IsStatic: true,
            EmittedName: emittedName,
            IsDeclaringTypeExternal: SymbolHelper.HasExternal(helperContainer),
            IsDeclaringTypeNoContainer: true
        );
        return new IrCallExpression(
            new IrMemberAccess(new IrTypeReference(helperContainer.Name), helperName, origin),
            arguments,
            typeArguments,
            origin
        );
    }

    /// <summary>
    /// Builds the call-site IR for <c>receiver.Prop = value</c> (and every
    /// compound-assignment variant) against an extension property whose
    /// host has a <c>$set</c> companion. Simple assignment lowers to a
    /// single <c>prop$set(receiver, value)</c> call. Compound forms read
    /// the current value via <c>prop$get(receiver)</c>, combine it with
    /// the right-hand side using the equivalent binary operator, and feed
    /// the result back into the setter. The receiver is shared with a
    /// let-binding (IIFE in TS) so impure receivers — method results,
    /// chained property reads — evaluate exactly once, matching the
    /// observable behavior of the source. Simple identifiers / <c>this</c>
    /// receivers skip the binding to keep the emitted code compact.
    /// </summary>
    private IrExpression BuildExtensionPropertyAssignment(
        MemberAccessExpressionSyntax targetSyntax,
        IPropertySymbol property,
        ExtensionPropertyLowering setterLowering,
        AssignmentExpressionSyntax assign
    )
    {
        var setterName = property.Name + IrExtensionConventions.PropertySetterSuffix;
        var emittedSetterName = ResolvePropertySetterEmittedName(property);
        var getterName = setterLowering.HelperName;
        var emittedGetterName = setterLowering.EmittedName;

        var rhs = Extract(assign.Right);
        var compoundOp = MapCompoundAssignmentOperator(assign);

        // Static extension property: no receiver to thread through — the
        // setter takes only the new value, and compound forms read via the
        // matching zero-arg getter.
        if (setterLowering.IsStatic)
        {
            if (compoundOp is null)
            {
                return BuildExtensionHelperCall(
                    setterLowering.HelperContainer,
                    setterName,
                    emittedSetterName,
                    [new IrArgument(rhs)],
                    typeArguments: null
                );
            }

            var currentValue = BuildExtensionHelperCall(
                setterLowering.HelperContainer,
                getterName,
                emittedGetterName,
                Array.Empty<IrArgument>(),
                typeArguments: null
            );
            return BuildExtensionHelperCall(
                setterLowering.HelperContainer,
                setterName,
                emittedSetterName,
                [new IrArgument(new IrBinaryExpression(currentValue, compoundOp.Value, rhs))],
                typeArguments: null
            );
        }

        var receiver = Extract(targetSyntax.Expression);

        if (compoundOp is null)
        {
            return BuildExtensionHelperCall(
                setterLowering.HelperContainer,
                setterName,
                emittedSetterName,
                [new IrArgument(receiver), new IrArgument(rhs)],
                typeArguments: null
            );
        }

        return BuildReceiverOnceSetter(
            setterLowering.HelperContainer,
            receiver,
            getterName,
            emittedGetterName,
            setterName,
            emittedSetterName,
            recv => new IrBinaryExpression(
                BuildExtensionHelperCall(
                    setterLowering.HelperContainer,
                    getterName,
                    emittedGetterName,
                    [new IrArgument(recv)],
                    typeArguments: null
                ),
                compoundOp.Value,
                rhs
            )
        );
    }

    /// <summary>
    /// Lowers <c>receiver.Prop++</c> / <c>receiver.Prop--</c> against an
    /// extension property to a let-bound setter-call shape so the
    /// receiver evaluates exactly once. The new value is computed as
    /// <c>prop$get(r) ± 1</c>, matching C# increment semantics for the
    /// numeric extension properties the MVP covers.
    /// </summary>
    private IrExpression BuildExtensionPropertyIncrement(
        MemberAccessExpressionSyntax targetSyntax,
        IPropertySymbol property,
        ExtensionPropertyLowering propLowering,
        IrUnaryOp op
    )
    {
        if (property.SetMethod is null)
            return new IrUnsupportedExpression("ExtensionPropertyIncrementWithoutSetter");

        var setterName = property.Name + IrExtensionConventions.PropertySetterSuffix;
        var emittedSetterName = ResolvePropertySetterEmittedName(property);
        var getterName = propLowering.HelperName;
        var emittedGetterName = propLowering.EmittedName;
        var binaryOp = op == IrUnaryOp.Increment ? IrBinaryOp.Add : IrBinaryOp.Subtract;

        // Static increment: no receiver to capture — emit
        // `prop$set(prop$get() ± 1)` directly.
        if (propLowering.IsStatic)
        {
            var currentValue = BuildExtensionHelperCall(
                propLowering.HelperContainer,
                getterName,
                emittedGetterName,
                Array.Empty<IrArgument>(),
                typeArguments: null
            );
            return BuildExtensionHelperCall(
                propLowering.HelperContainer,
                setterName,
                emittedSetterName,
                [
                    new IrArgument(
                        new IrBinaryExpression(
                            currentValue,
                            binaryOp,
                            new IrLiteral(1, IrLiteralKind.Int32)
                        )
                    ),
                ],
                typeArguments: null
            );
        }

        var receiver = Extract(targetSyntax.Expression);

        return BuildReceiverOnceSetter(
            propLowering.HelperContainer,
            receiver,
            getterName,
            emittedGetterName,
            setterName,
            emittedSetterName,
            recv => new IrBinaryExpression(
                BuildExtensionHelperCall(
                    propLowering.HelperContainer,
                    getterName,
                    emittedGetterName,
                    [new IrArgument(recv)],
                    typeArguments: null
                ),
                binaryOp,
                new IrLiteral(1, IrLiteralKind.Int32)
            )
        );
    }

    /// <summary>
    /// Lowers an indexer read (<c>receiver[i]</c>) against a C# 14
    /// extension indexer into <c>item$get(receiver, i, …)</c>. Extra
    /// arguments beyond the first index slot are forwarded verbatim so
    /// multi-key indexers stay correctly typed at the helper.
    /// </summary>
    private IrExpression BuildExtensionIndexerGetCall(
        ElementAccessExpressionSyntax targetSyntax,
        IPropertySymbol indexer,
        ExtensionPropertyLowering lowering
    )
    {
        var arguments = new List<IrArgument>(targetSyntax.ArgumentList.Arguments.Count + 1)
        {
            new IrArgument(Extract(targetSyntax.Expression)),
        };
        foreach (var arg in targetSyntax.ArgumentList.Arguments)
            arguments.Add(new IrArgument(Extract(arg.Expression)));
        return BuildExtensionHelperCall(
            lowering.HelperContainer,
            lowering.HelperName,
            lowering.EmittedName,
            arguments,
            typeArguments: null
        );
    }

    /// <summary>
    /// Builds the call-site IR for an extension-indexer write
    /// (<c>receiver[i] = value</c> and every compound-assignment form).
    /// Simple assignment lowers to <c>item$set(receiver, i, value)</c>.
    /// Compound forms read the slot via <c>item$get</c>, combine with the
    /// right-hand side, and feed the result back into the setter. The
    /// receiver is shared with an <see cref="IrLetExpression"/> when it is
    /// impure (method results, chained reads) so it evaluates exactly once,
    /// matching C# semantics.
    /// <para>
    /// MVP scope mirrors Stage 2 — only the receiver is protected against
    /// double evaluation. An impure index expression
    /// (e.g., <c>xs[NextIndex()] += 1</c>) still appears in both the
    /// getter and setter call positions; a follow-up may wrap the indices
    /// in additional let-bindings if the use case shows up in practice.
    /// </para>
    /// </summary>
    private IrExpression BuildExtensionIndexerAssignment(
        ElementAccessExpressionSyntax targetSyntax,
        IPropertySymbol indexer,
        ExtensionPropertyLowering getterLowering,
        AssignmentExpressionSyntax assign
    )
    {
        var setterName = indexer.Name + IrExtensionConventions.PropertySetterSuffix;
        var emittedSetterName = ResolvePropertySetterEmittedName(indexer);
        var getterName = getterLowering.HelperName;
        var emittedGetterName = getterLowering.EmittedName;

        var receiver = Extract(targetSyntax.Expression);
        var indexArgs = targetSyntax
            .ArgumentList.Arguments.Select(a => Extract(a.Expression))
            .ToList();
        var rhs = Extract(assign.Right);
        var compoundOp = MapCompoundAssignmentOperator(assign);

        if (compoundOp is null)
        {
            var args = new List<IrArgument>(indexArgs.Count + 2) { new IrArgument(receiver) };
            foreach (var idx in indexArgs)
                args.Add(new IrArgument(idx));
            args.Add(new IrArgument(rhs));
            return BuildExtensionHelperCall(
                getterLowering.HelperContainer,
                setterName,
                emittedSetterName,
                args,
                typeArguments: null
            );
        }

        return BuildReceiverOnceIndexerSetter(
            getterLowering.HelperContainer,
            receiver,
            indexArgs,
            getterName,
            emittedGetterName,
            setterName,
            emittedSetterName,
            (recv, idxRefs) =>
                new IrBinaryExpression(
                    BuildExtensionIndexerHelperCall(
                        getterLowering.HelperContainer,
                        getterName,
                        emittedGetterName,
                        recv,
                        idxRefs
                    ),
                    compoundOp.Value,
                    rhs
                )
        );
    }

    /// <summary>
    /// Lowers <c>receiver[i]++</c> / <c>receiver[i]--</c> against an
    /// extension indexer to a let-bound setter-call shape so the receiver
    /// evaluates exactly once. The new slot value is computed as
    /// <c>item$get(r, i) ± 1</c>. Statement-position only — the post/pre
    /// value distinction is out of MVP scope.
    /// </summary>
    private IrExpression BuildExtensionIndexerIncrement(
        ElementAccessExpressionSyntax targetSyntax,
        IPropertySymbol indexer,
        ExtensionPropertyLowering lowering,
        IrUnaryOp op
    )
    {
        if (indexer.SetMethod is null)
            return new IrUnsupportedExpression("ExtensionIndexerIncrementWithoutSetter");

        var setterName = indexer.Name + IrExtensionConventions.PropertySetterSuffix;
        var emittedSetterName = ResolvePropertySetterEmittedName(indexer);
        var getterName = lowering.HelperName;
        var emittedGetterName = lowering.EmittedName;
        var binaryOp = op == IrUnaryOp.Increment ? IrBinaryOp.Add : IrBinaryOp.Subtract;
        var receiver = Extract(targetSyntax.Expression);
        var indexArgs = targetSyntax
            .ArgumentList.Arguments.Select(a => Extract(a.Expression))
            .ToList();

        return BuildReceiverOnceIndexerSetter(
            lowering.HelperContainer,
            receiver,
            indexArgs,
            getterName,
            emittedGetterName,
            setterName,
            emittedSetterName,
            (recv, idxRefs) =>
                new IrBinaryExpression(
                    BuildExtensionIndexerHelperCall(
                        lowering.HelperContainer,
                        getterName,
                        emittedGetterName,
                        recv,
                        idxRefs
                    ),
                    binaryOp,
                    new IrLiteral(1, IrLiteralKind.Int32)
                )
        );
    }

    /// <summary>
    /// Variant of <see cref="BuildReceiverOnceSetter"/> tailored for
    /// indexers: the new-value callback receives both the receiver
    /// reference and the (already evaluated) index argument list so the
    /// caller can plug them into a getter sub-call without re-evaluating
    /// the original syntax. Index expressions are captured into the
    /// surrounding scope by the caller — this method only protects the
    /// receiver.
    /// </summary>
    private IrExpression BuildReceiverOnceIndexerSetter(
        INamedTypeSymbol helperContainer,
        IrExpression receiver,
        IReadOnlyList<IrExpression> indexArgs,
        string getterName,
        string? emittedGetterName,
        string setterName,
        string? emittedSetterName,
        Func<IrExpression, IReadOnlyList<IrExpression>, IrExpression> buildNewValue
    )
    {
        return BindReceiverOnce(
            receiver,
            target =>
            {
                var setterArgs = new List<IrArgument>(indexArgs.Count + 2)
                {
                    new IrArgument(target),
                };

                foreach (var idx in indexArgs)
                    setterArgs.Add(new IrArgument(idx));

                setterArgs.Add(new IrArgument(buildNewValue(target, indexArgs)));

                return BuildExtensionHelperCall(
                    helperContainer,
                    setterName,
                    emittedSetterName,
                    setterArgs,
                    typeArguments: null
                );
            }
        );
    }

    /// <summary>
    /// Convenience wrapper that builds an <c>item$get(receiver, …)</c> call
    /// from a receiver expression and the already-extracted index argument
    /// list, mirroring the shape <see cref="BuildExtensionHelperCall"/>
    /// expects.
    /// </summary>
    private static IrExpression BuildExtensionIndexerHelperCall(
        INamedTypeSymbol helperContainer,
        string helperName,
        string? emittedName,
        IrExpression receiver,
        IReadOnlyList<IrExpression> indexArgs
    )
    {
        var args = new List<IrArgument>(indexArgs.Count + 1) { new IrArgument(receiver) };
        foreach (var idx in indexArgs)
            args.Add(new IrArgument(idx));
        return BuildExtensionHelperCall(
            helperContainer,
            helperName,
            emittedName,
            args,
            typeArguments: null
        );
    }

    /// <summary>
    /// Resolves the helper-emission metadata for an extension indexer
    /// referenced at a call site. Mirrors
    /// <see cref="TryResolveExtensionPropertyLowering"/> but accepts an
    /// <see cref="ElementAccessExpressionSyntax"/> receiver and skips the
    /// non-indexer guard. Reuses <see cref="ExtensionPropertyLowering"/>
    /// because the call-site contract is identical: a static helper on the
    /// extension container, keyed by the indexer's <c>Name</c>
    /// (Roslyn substitutes <c>[IndexerName]</c> overrides into the symbol
    /// name, so no extra attribute read is required).
    /// </summary>
    private ExtensionPropertyLowering? TryResolveExtensionIndexerLowering(
        IPropertySymbol indexer,
        ElementAccessExpressionSyntax access
    )
    {
        if (!indexer.IsIndexer)
            return null;
        var containing = indexer.ContainingType;
        if (containing is null)
            return null;

        // C# 14 extension block indexer — ContainingType is the synthetic
        // anonymous type Roslyn manufactures inside `extension(R r) { … }`.
        if (
            string.IsNullOrEmpty(containing.Name)
            && containing.ContainingType is { IsStatic: true } parentStatic
            && IsTranspilableExtensionContainer(parentStatic)
        )
        {
            var receiverSymbol = _semantic.GetSymbolInfo(access.Expression).Symbol;
            if (receiverSymbol is INamedTypeSymbol)
                return null;
            return new ExtensionPropertyLowering(
                indexer.Name + IrExtensionConventions.PropertyGetterSuffix,
                ResolvePropertyEmittedName(indexer),
                parentStatic
            );
        }

        return null;
    }

    /// <summary>
    /// Wraps a setter call whose new-value expression references the
    /// receiver more than once in an <see cref="IrLetExpression"/>, but
    /// only when the receiver is impure. Simple identifiers / <c>this</c>
    /// skip the binding to keep the output close to the source.
    /// </summary>
    private IrExpression BuildReceiverOnceSetter(
        INamedTypeSymbol helperContainer,
        IrExpression receiver,
        string getterName,
        string? emittedGetterName,
        string setterName,
        string? emittedSetterName,
        Func<IrExpression, IrExpression> buildNewValue
    )
    {
        return BindReceiverOnce(
            receiver,
            target =>
                BuildExtensionHelperCall(
                    helperContainer,
                    setterName,
                    emittedSetterName,
                    [new IrArgument(target), new IrArgument(buildNewValue(target))],
                    typeArguments: null
                )
        );
    }

    /// <summary>
    /// <see cref="TryMapCompoundAssignmentToBinary"/> narrowed by
    /// <see cref="NarrowToIntegerDivision"/>: an integer <c>/=</c> maps to
    /// <see cref="IrBinaryOp.IntegerDivide"/>.
    /// </summary>
    private IrBinaryOp? MapCompoundAssignmentOperator(AssignmentExpressionSyntax assign) =>
        TryMapCompoundAssignmentToBinary(assign.Kind()) is { } op
            ? NarrowToIntegerDivision(op, assign)
            : null;

    /// <summary>
    /// Maps the compound assignment kinds (<c>+=</c>, <c>-=</c>, …) to
    /// the equivalent binary operator so a setter rewrite can synthesize
    /// the read-modify-write expression. Returns <c>null</c> for plain
    /// <c>=</c> (no read needed) and for forms that don't have a clean
    /// binary equivalent (currently <c>??=</c>, which the MVP doesn't
    /// support against extension setters).
    /// </summary>
    private static IrBinaryOp? TryMapCompoundAssignmentToBinary(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.AddAssignmentExpression => IrBinaryOp.Add,
            SyntaxKind.SubtractAssignmentExpression => IrBinaryOp.Subtract,
            SyntaxKind.MultiplyAssignmentExpression => IrBinaryOp.Multiply,
            SyntaxKind.DivideAssignmentExpression => IrBinaryOp.Divide,
            SyntaxKind.ModuloAssignmentExpression => IrBinaryOp.Modulo,
            SyntaxKind.AndAssignmentExpression => IrBinaryOp.BitwiseAnd,
            SyntaxKind.OrAssignmentExpression => IrBinaryOp.BitwiseOr,
            SyntaxKind.ExclusiveOrAssignmentExpression => IrBinaryOp.BitwiseXor,
            SyntaxKind.LeftShiftAssignmentExpression => IrBinaryOp.LeftShift,
            SyntaxKind.RightShiftAssignmentExpression => IrBinaryOp.RightShift,
            SyntaxKind.UnsignedRightShiftAssignmentExpression => IrBinaryOp.UnsignedRightShift,
            _ => null,
        };

    /// <summary>
    /// Per-target <c>[Name]</c> override for the setter companion of an
    /// extension property — mirrors <see cref="ResolvePropertyEmittedName"/>
    /// but appends the setter suffix so the call site matches the helper
    /// emitted by <see cref="IrModuleFunctionExtractor"/>.
    /// </summary>
    private string? ResolvePropertySetterEmittedName(IPropertySymbol property)
    {
        var overrideName = SymbolHelper.GetNameOverride(property, _target);
        return overrideName is null
            ? null
            : overrideName + IrExtensionConventions.PropertySetterSuffix;
    }

    private readonly record struct ExtensionCallLowering(
        IMethodSymbol OriginSymbol,
        string HelperName,
        string? EmittedName,
        INamedTypeSymbol HelperContainer,
        bool IsStatic = false
    );

    private readonly record struct ExtensionPropertyLowering(
        string HelperName,
        string? EmittedName,
        INamedTypeSymbol HelperContainer,
        bool IsStatic = false
    );

    private ExtensionPropertyLowering? TryResolveExtensionPropertyLowering(
        IPropertySymbol prop,
        ExpressionSyntax receiver
    )
    {
        if (prop.IsIndexer)
            return null;
        var containing = prop.ContainingType;
        if (containing is null)
            return null;

        // C# 14 extension block property: ContainingType is a synthetic
        // anonymous type whose own ContainingType is a static class.
        if (
            string.IsNullOrEmpty(containing.Name)
            && containing.ContainingType is { IsStatic: true } parentStatic
            && IsTranspilableExtensionContainer(parentStatic)
        )
        {
            var receiverSymbol = _semantic.GetSymbolInfo(receiver).Symbol;
            // Static extension property: LHS is the type, the helper takes
            // no receiver, the call site emits `prop$get()` / `prop$set(v)`.
            if (prop.IsStatic && receiverSymbol is INamedTypeSymbol)
            {
                return new ExtensionPropertyLowering(
                    prop.Name + IrExtensionConventions.PropertyGetterSuffix,
                    ResolvePropertyEmittedName(prop),
                    parentStatic,
                    IsStatic: true
                );
            }
            if (receiverSymbol is INamedTypeSymbol)
                return null;
            return new ExtensionPropertyLowering(
                prop.Name + IrExtensionConventions.PropertyGetterSuffix,
                ResolvePropertyEmittedName(prop),
                parentStatic
            );
        }

        // Classic extension property — Roslyn surfaces the property with
        // the receiver in `prop.Parameters[0]` and the property declared
        // on a static class.
        if (
            containing.IsStatic
            && prop.Parameters.Length > 0
            && IsTranspilableExtensionContainer(containing)
        )
        {
            var receiverSymbol = _semantic.GetSymbolInfo(receiver).Symbol;
            if (receiverSymbol is INamedTypeSymbol)
                return null;
            return new ExtensionPropertyLowering(
                prop.Name + IrExtensionConventions.PropertyGetterSuffix,
                ResolvePropertyEmittedName(prop),
                containing
            );
        }

        return null;
    }

    /// <summary>
    /// Mirrors <see cref="ResolveEmittedName(IMethodSymbol)"/> for an
    /// extension property: the override (when present) is appended with
    /// the getter suffix so the call site agrees with the helper emitted
    /// by <see cref="IrModuleFunctionExtractor"/>.
    /// </summary>
    private string? ResolvePropertyEmittedName(IPropertySymbol property)
    {
        var overrideName = SymbolHelper.GetNameOverride(property, _target);
        return overrideName is null
            ? null
            : overrideName + IrExtensionConventions.PropertyGetterSuffix;
    }

    /// <summary>
    /// Detects an extension-style call (classic <c>(this T)</c> reduced
    /// form or C# 14 <c>extension(T r) { … }</c> block) and returns the
    /// lowering target — the symbol to import from and the helper name
    /// at the call site. Returns <c>null</c> when the call is a plain
    /// instance / static method invocation.
    /// </summary>
    private ExtensionCallLowering? TryResolveExtensionLowering(
        IMethodSymbol callee,
        ExpressionSyntax receiver
    )
    {
        if (
            callee.ReducedFrom is { } reduced
            && IsTranspilableExtensionContainer(reduced.ContainingType)
        )
            return new ExtensionCallLowering(
                reduced,
                reduced.Name,
                ResolveEmittedName(reduced),
                reduced.ContainingType
            );

        var containing = callee.ContainingType;
        if (containing is null)
            return null;

        // C# 14 extension block: ContainingType is a synthetic anonymous
        // type whose own ContainingType is the user's static class.
        if (
            string.IsNullOrEmpty(containing.Name)
            && containing.ContainingType is { IsStatic: true } parentStatic
            && IsTranspilableExtensionContainer(parentStatic)
        )
        {
            var receiverSymbol = _semantic.GetSymbolInfo(receiver).Symbol;
            // Static extension members dispatch through `Type.Member(args)` —
            // the LHS is the receiver-type symbol, the receiver isn't a value,
            // and the helper takes no implicit first argument. Treat the
            // instance form (LHS is a value, callee is non-static) as the
            // default and split the static case into a dedicated branch so
            // the call-site rewrite knows to drop the receiver.
            if (callee.IsStatic && receiverSymbol is INamedTypeSymbol)
            {
                return new ExtensionCallLowering(
                    callee,
                    callee.Name,
                    ResolveEmittedName(callee),
                    parentStatic,
                    IsStatic: true
                );
            }
            if (receiverSymbol is INamedTypeSymbol)
                return null;
            return new ExtensionCallLowering(
                callee,
                callee.Name,
                ResolveEmittedName(callee),
                parentStatic
            );
        }

        return null;
    }

    /// <summary>
    /// Returns the per-target <c>[Name]</c> override for an extension
    /// helper symbol, or <c>null</c> when the source uses no override.
    /// The result lands on <see cref="IrMemberOrigin.EmittedName"/> so
    /// the bridge picks it up at the call site, keeping the rewrite in
    /// lockstep with module-function emission (which honors the same
    /// override through <c>IrToTsNamingPolicy.ToFunctionName</c>).
    /// </summary>
    private string? ResolveEmittedName(IMethodSymbol method) =>
        SymbolHelper.GetNameOverride(method, _target);

    /// <summary>
    /// Skips BCL / external static classes (LINQ, framework helpers) so
    /// their extension calls keep flowing through the BCL mapper instead
    /// of getting rewritten to local helper calls. The lowering applies
    /// only when the extension lives in user-transpilable code.
    /// </summary>
    private bool IsTranspilableExtensionContainer(INamedTypeSymbol? container)
    {
        if (container is null)
            return false;
        if (SymbolHelper.HasIgnore(container))
            return false;
        if (SymbolHelper.HasExternal(container))
            return false;
        if (SymbolHelper.HasImport(container))
            return false;
        var assembly = container.ContainingAssembly;
        if (assembly is null)
            return false;
        if (
            !SymbolEqualityComparer.Default.Equals(assembly, _semantic.Compilation.Assembly)
            && !HasAssemblyTranspileAttribute(assembly)
        )
            return false;
        return true;
    }

    private static bool HasAssemblyTranspileAttribute(IAssemblySymbol assembly) =>
        assembly
            .GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name is "TranspileAssemblyAttribute" or "TranspileAssembly"
            );

    /// <summary>
    /// Marks the trailing argument as a spread when the C# call passes a
    /// single value into a <c>params</c> slot (the value is the array
    /// itself). Discrete element calls (<c>Foo("x", "a", 42)</c>) leave the
    /// arguments untouched — they already line up with the rest parameter
    /// at the TypeScript surface.
    /// </summary>
    private void ApplyParamsSpread(
        List<IrArgument> args,
        IMethodSymbol? symbol,
        SeparatedSyntaxList<ArgumentSyntax> syntaxArgs
    )
    {
        if (symbol is null || symbol.Parameters.Length == 0)
            return;
        var lastParam = symbol.Parameters[^1];
        if (!lastParam.IsParams)
            return;
        if (syntaxArgs.Count != symbol.Parameters.Length)
            return;
        var lastSyntax = syntaxArgs[^1];
        var lastType = _semantic.GetTypeInfo(lastSyntax.Expression).ConvertedType;
        if (lastType is null)
            return;
        if (!SymbolEqualityComparer.Default.Equals(lastType, lastParam.Type))
            return;
        args[^1] = NormalizeNullParamsArray(
            args[^1] with
            {
                IsSpread = true,
            },
            lastSyntax.Expression,
            symbol
        );
    }

    /// <summary>
    /// A <c>params</c> array passed as a constant <c>null</c> (<c>f(null)</c>,
    /// <c>f((T[]?)null)</c>, <c>f(default)</c>) becomes the plain null literal, so a
    /// backend that spreads the array tells it apart from an array value without
    /// unwrapping casts. A backend with rest parameters cannot pass <c>null</c> to
    /// one (TypeScript drops the argument, so the callee receives an empty array), so
    /// a call to a transpiled method is reported as an MS0001 warning at the
    /// argument for such a target. Dart lowers <c>params</c> to a list parameter that
    /// holds <c>null</c>, and a BCL callee keeps the argument through its mapping
    /// (most BCL methods reject a <c>null</c> array anyway), so neither is reported.
    /// </summary>
    private IrArgument NormalizeNullParamsArray(
        IrArgument argument,
        ExpressionSyntax syntax,
        IMethodSymbol callee
    )
    {
        if (_semantic.GetConstantValue(syntax) is not { HasValue: true, Value: null })
            return argument;

        if (HasRestParameters && IsTranspiledCallee(callee))
            ReportNullParamsArray(syntax);

        return argument with
        {
            Value = new IrLiteral(null, IrLiteralKind.Null),
        };
    }

    private bool HasRestParameters =>
        _target is null or Metano.Annotations.TargetLanguage.TypeScript;

    private bool IsTranspiledCallee(IMethodSymbol callee) =>
        callee.ContainingAssembly is { } assembly
        && (
            SymbolEqualityComparer.Default.Equals(assembly, _semantic.Compilation.Assembly)
            || HasAssemblyTranspileAttribute(assembly)
        );

    private static void ReportNullParamsArray(ExpressionSyntax argument) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'{argument}' passes a null array to a params parameter: a rest parameter "
                    + "cannot hold null, so the callee receives an empty array.",
                argument.GetLocation()
            )
        );

    private IrExpression ExtractObjectCreation(ObjectCreationExpressionSyntax oc)
    {
        var args = oc.ArgumentList?.Arguments.Select(ExtractArgument).ToList() ?? [];
        var ctorSymbol = _semantic.GetSymbolInfo(oc).Symbol as IMethodSymbol;
        ReportTupleValueLookup(oc, ctorSymbol);
        if (oc.ArgumentList is { } argList)
            ApplyParamsSpread(args, ctorSymbol, argList.Arguments);
        var typeSymbol = _semantic.GetTypeInfo(oc).Type;
        var type = typeSymbol is not null
            ? IrTypeRefMapper.Map(typeSymbol, _originResolver, _target)
            : new IrUnknownTypeRef();
        return BuildNewExpression(type, args, typeSymbol, oc);
    }

    private IrExpression ExtractImplicitObjectCreation(ImplicitObjectCreationExpressionSyntax ioc)
    {
        var args = ioc.ArgumentList.Arguments.Select(ExtractArgument).ToList();
        var ctorSymbol = _semantic.GetSymbolInfo(ioc).Symbol as IMethodSymbol;
        ReportTupleValueLookup(ioc, ctorSymbol);
        ApplyParamsSpread(args, ctorSymbol, ioc.ArgumentList.Arguments);
        var typeSymbol = _semantic.GetTypeInfo(ioc).Type;
        var type = typeSymbol is not null
            ? IrTypeRefMapper.Map(typeSymbol, _originResolver, _target)
            : new IrUnknownTypeRef();
        return BuildNewExpression(type, args, typeSymbol, ioc);
    }

    /// <summary>
    /// Shared tail for explicit and implicit object creations. When the target
    /// type is tagged <c>[PlainObject]</c> we pre-compute the constructor's
    /// parameter names so the TS bridge can emit an object literal keyed by
    /// name (the shape <c>[PlainObject]</c> expects), mirroring the legacy
    /// <c>ObjectCreationHandler.CreatePlainObjectLiteral</c> behavior.
    /// </summary>
    private IrExpression BuildNewExpression(
        IrTypeRef type,
        IReadOnlyList<IrArgument> args,
        ITypeSymbol? typeSymbol,
        ExpressionSyntax creationSyntax
    )
    {
        var isPlainObject =
            typeSymbol is INamedTypeSymbol named && SymbolHelper.HasPlainObject(named);
        var isJsTuple =
            typeSymbol is INamedTypeSymbol jsTupleNamed && SymbolHelper.HasJsTuple(jsTupleNamed);
        IReadOnlyList<string>? parameterNames = null;
        var ctor = _semantic.GetSymbolInfo(creationSyntax).Symbol as IMethodSymbol;
        if (isPlainObject && ctor is not null && ctor.Parameters.Length > 0)
            parameterNames = ctor.Parameters.Select(p => p.Name).ToList();

        // When the caller mixed named arguments into the list, reorder them
        // into the constructor's parameter order and fill any skipped spots
        // with the parameter's default value. This gives backends that can't
        // express named arguments (TypeScript) a positional list that still
        // behaves like the source, while backends that can (Dart) keep the
        // name on each IrArgument and render `name: value`.
        if (ctor is not null && args.Any(a => a.Name is not null))
            args = NormalizeArguments(args, ctor);

        var initializer = InitializerOf(creationSyntax);

        if (ctor is not null && SymbolHelper.HasObjectArgs(ctor))
        {
            var properties = BuildObjectArgsProperties(ctor, args);
            var objectArgsCreation = new IrNewExpression(
                type,
                [new IrArgument(new IrObjectLiteral(properties))],
                IsObjectArgsCtor: true
            );

            return ApplyInitializer(objectArgsCreation, typeSymbol, initializer);
        }

        var keepsInitializer = RendersInitializerAsJsx(type, initializer);

        var initializers = keepsInitializer ? ExtractObjectInitializer(creationSyntax) : null;

        var creation = new IrNewExpression(
            type,
            args,
            isPlainObject,
            parameterNames,
            IsJsTuple: isJsTuple,
            Initializers: initializers,
            ExternalImports: BuildImportedRenderableImports(typeSymbol),
            ExceptionConstruction: ExtractExceptionConstruction(ctor, args, creationSyntax),
            Origin: BuildOrigin(ctor),
            TypeArguments: ConstructedTypeArguments(typeSymbol)
        );

        var constructed = BindExceptionArguments(creation);

        return keepsInitializer
            ? constructed
            : ApplyInitializer(constructed, typeSymbol, initializer);
    }

    /// <summary>
    /// The role-ordered BCL exception lowering may reorder or drop arguments,
    /// so the side-effecting ones are bound first.
    /// </summary>
    private IrExpression BindExceptionArguments(IrNewExpression creation)
    {
        if (creation.ExceptionConstruction is not { RequiresArgumentTemporaries: true } exception)
            return creation;

        return BindArgumentsOnce(
            creation,
            bound =>
                bound with
                {
                    ExceptionConstruction = exception with { RequiresArgumentTemporaries = false },
                }
        );
    }

    /// <summary>
    /// The type arguments of a constructed generic type
    /// (<c>Dictionary&lt;string, int&gt;</c> → <c>[string, int]</c>), which a
    /// declarative constructor mapping splices into its template; <c>null</c>
    /// for a non-generic type.
    /// </summary>
    private IReadOnlyList<IrTypeRef>? ConstructedTypeArguments(ITypeSymbol? typeSymbol) =>
        typeSymbol is INamedTypeSymbol { TypeArguments.Length: > 0 } generic
            ? generic
                .TypeArguments.Select(typeArgument =>
                    IrTypeRefMapper.Map(typeArgument, _originResolver, _target)
                )
                .ToList()
            : null;

    /// <summary>
    /// Evaluates the arguments of <paramref name="creation"/> once, in source
    /// order: each side-effecting argument is let-bound to a temporary (see
    /// <see cref="BindValueOnce"/>) and <paramref name="build"/> receives the
    /// construction with those arguments replaced, free to reorder or drop
    /// them.
    /// </summary>
    private IrExpression BindArgumentsOnce(
        IrNewExpression creation,
        Func<IrNewExpression, IrExpression> build
    ) => BindArgumentsFrom(creation, 0, build);

    private IrExpression BindArgumentsFrom(
        IrNewExpression creation,
        int index,
        Func<IrNewExpression, IrExpression> build
    )
    {
        if (index == creation.Arguments.Count)
            return build(creation);

        return BindValueOnce(
            creation.Arguments[index].Value,
            bound => BindArgumentsFrom(WithArgumentValue(creation, index, bound), index + 1, build)
        );
    }

    private static IrNewExpression WithArgumentValue(
        IrNewExpression creation,
        int index,
        IrExpression value
    ) =>
        creation with
        {
            Arguments = creation
                .Arguments.Select(
                    (argument, position) =>
                        position == index ? argument with { Value = value } : argument
                )
                .ToList(),
        };

    /// <summary>
    /// Classifies the arguments of a <c>new</c> of a BCL exception by
    /// constructor-parameter role (see <see cref="IrExceptionConstructionExtractor"/>).
    /// Returns <c>null</c> for every other construction.
    /// </summary>
    private IrExceptionConstruction? ExtractExceptionConstruction(
        IMethodSymbol? ctor,
        IReadOnlyList<IrArgument> args,
        ExpressionSyntax creationSyntax
    ) =>
        ctor is null
            ? null
            : IrExceptionConstructionExtractor.Extract(
                ctor,
                args,
                ctor.ContainingType,
                _target,
                (creationSyntax as BaseObjectCreationExpressionSyntax)?.ArgumentList,
                _semantic
            );

    /// <summary>
    /// When the constructed type is an <em>imported</em> JSX renderable — it is
    /// JSX-renderable (FR-022) but neither a Metano component (does not derive
    /// from a <c>[JsxComponentBuilder]</c> base) nor a native intrinsic element
    /// (<c>[JsxNativeElement]</c>) — its <c>[Import]</c> module is threaded onto
    /// the IR so the backend's JSX lowering can resolve the capitalized tag to
    /// its npm module (<c>import { Route } from "solid-router"</c>) instead of a
    /// wrong intra-project component import. Returns <c>null</c> for every other
    /// construction (components resolve their import from the declaring type;
    /// native elements emit a lowercase intrinsic tag that needs no import).
    /// </summary>
    private static IReadOnlyList<IrExternalImport>? BuildImportedRenderableImports(
        ITypeSymbol? typeSymbol
    )
    {
        if (typeSymbol is not INamedTypeSymbol named)
            return null;
        var isComponent = named.DerivesFromJsxComponentBuilder();
        var isNative = SymbolHelper.GetJsxNativeElementTag(named) is not null;
        if (isComponent || isNative || !SymbolHelper.IsJsxRenderable(named))
            return null;
        if (SymbolHelper.GetImport(named) is not { } import)
            return null;
        return [new IrExternalImport(import.Name, import.From, import.AsDefault, import.Version)];
    }

    /// <summary>
    /// Reads the object-initializer clause of an object creation
    /// (<c>new T { Member = value, ... }</c>) into an ordered list of
    /// <see cref="IrMemberInit"/>. Returns <c>null</c> when the creation has no
    /// initializer (preserving the prior behavior for every existing call
    /// site) or when the clause is a collection initializer rather than an
    /// object initializer. Each <see cref="AssignmentExpressionSyntax"/>'s
    /// left identifier resolves to the assigned member symbol so the
    /// <c>[Name]</c> override is captured as <see cref="IrMemberInit.EmittedName"/>;
    /// the right side is lowered through the normal expression path.
    /// </summary>
    private IReadOnlyList<IrMemberInit>? ExtractObjectInitializer(ExpressionSyntax creationSyntax)
    {
        var initializer = creationSyntax switch
        {
            ObjectCreationExpressionSyntax oc => oc.Initializer,
            ImplicitObjectCreationExpressionSyntax ioc => ioc.Initializer,
            _ => null,
        };
        if (initializer is null || !initializer.IsKind(SyntaxKind.ObjectInitializerExpression))
            return null;

        var inits = new List<IrMemberInit>();
        foreach (var expression in initializer.Expressions)
        {
            if (expression is not AssignmentExpressionSyntax assign)
                continue;
            var memberName = assign.Left switch
            {
                IdentifierNameSyntax id => id.Identifier.ValueText,
                _ => assign.Left.ToString(),
            };
            var memberSymbol = _semantic.GetSymbolInfo(assign.Left).Symbol;
            var emittedName = memberSymbol is not null
                ? SymbolHelper.GetNameOverride(
                    memberSymbol,
                    Metano.Annotations.TargetLanguage.TypeScript
                )
                : null;
            var isChildrenSlot = memberSymbol is not null && memberSymbol.IsJsxChildrenSlot();
            inits.Add(
                new IrMemberInit(memberName, emittedName, Extract(assign.Right), isChildrenSlot)
            );
        }
        return inits;
    }

    private static List<(string Name, IrExpression Value)> BuildObjectArgsProperties(
        IMethodSymbol target,
        IReadOnlyList<IrArgument> orderedArgs
    )
    {
        var properties = new List<(string Name, IrExpression Value)>();
        for (var i = 0; i < target.Parameters.Length && i < orderedArgs.Count; i++)
        {
            var argument = orderedArgs[i];
            if (
                target.Parameters[i].HasExplicitDefaultValue
                && argument.Value is IrLiteral literal
                && IsLiteralEqualToDefault(literal, target.Parameters[i].ExplicitDefaultValue)
            )
                continue;
            properties.Add((target.Parameters[i].Name, argument.Value));
        }
        return properties;
    }

    /// <summary>
    /// Expands a mixed positional + named argument list into strict positional
    /// order against the target method's parameters. Missing named entries
    /// (parameters the caller skipped) are filled with their explicit default
    /// value; when a parameter has no explicit default (shouldn't happen for
    /// valid C#) the slot falls back to <c>IrLiteralKind.Default</c> so the
    /// pipeline keeps a visible marker instead of silently dropping it.
    /// </summary>
    private IReadOnlyList<IrArgument> NormalizeArguments(
        IReadOnlyList<IrArgument> args,
        IMethodSymbol target
    )
    {
        // Positional prefix: walk until we hit the first named argument.
        var byName = args.Where(a => a.Name is not null).ToDictionary(a => a.Name!, a => a);
        var positional = args.TakeWhile(a => a.Name is null).ToList();
        var result = new List<IrArgument>(target.Parameters.Length);
        for (var i = 0; i < target.Parameters.Length; i++)
        {
            if (i < positional.Count)
            {
                result.Add(positional[i]);
                continue;
            }
            var p = target.Parameters[i];
            if (byName.TryGetValue(p.Name, out var named))
            {
                result.Add(named);
                continue;
            }
            result.Add(new IrArgument(BuildDefaultArgument(p)));
        }
        return result;
    }

    private IrExpression BuildDefaultArgument(IParameterSymbol parameter)
    {
        if (parameter.HasExplicitDefaultValue)
            return BuildLiteralForDefault(parameter.ExplicitDefaultValue, parameter.Type);
        // No explicit default — surface an IrLiteral(Default) so the printer
        // emits a visible marker (`undefined`) rather than producing invalid
        // code. Roslyn should never let us reach this branch for a well-formed
        // call, but we stay defensive.
        return new IrLiteral(null, IrLiteralKind.Default);
    }

    private IrExpression BuildLiteralForDefault(object? value, ITypeSymbol type)
    {
        if (value is null)
            return new IrLiteral(null, IrLiteralKind.Null);
        // Enums surface their underlying numeric constant in
        // ExplicitDefaultValue. Translate back to a member access on the enum
        // type so the backend lowering prints `Priority.Medium` rather than a
        // raw `1`.
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            var member = DefaultValueLiterals.FindEnumMember(enumType, value);
            if (member is not null)
                return new IrMemberAccess(TypeReferenceTo(enumType), member.Name);
        }
        return value switch
        {
            bool b => new IrLiteral(b, IrLiteralKind.Boolean),
            string s => new IrLiteral(s, IrLiteralKind.String),
            char c => new IrLiteral(c, IrLiteralKind.Char),
            long l => new IrLiteral(l, IrLiteralKind.Int64),
            int i => new IrLiteral(i, IrLiteralKind.Int32),
            double d => new IrLiteral(d, IrLiteralKind.Float64),
            float f => new IrLiteral(f, IrLiteralKind.Float64),
            _ => new IrLiteral(value, IrLiteralKind.Default),
        };
    }

    private IrTypeRef ExtractTargetType(CastExpressionSyntax cast)
    {
        var info = _semantic.GetTypeInfo(cast.Type).Type;
        return info is not null
            ? IrTypeRefMapper.Map(info, _originResolver, _target)
            : new IrUnknownTypeRef();
    }

    /// <summary>
    /// C# has no runtime cast — TS is structurally typed and every numeric is
    /// just <c>number</c> or <c>bigint</c>. The only casts that need actual
    /// runtime code are the ones that change numeric representation:
    /// <list type="bullet">
    ///   <item><c>(decimal)bigIntExpr</c> → <c>new Decimal(expr.toString())</c></item>
    ///   <item><c>(BigInteger)decimalExpr</c> → <c>BigInt(expr.toFixed(0))</c></item>
    ///   <item><c>(int|long|short|byte)decimalExpr</c> → <c>expr.toNumber()</c></item>
    ///   <item><c>(BigInteger)intExpr</c> → <c>BigInt(expr)</c></item>
    /// </list>
    /// All other casts collapse to the inner expression.
    /// </summary>
    private IrExpression ExtractCast(CastExpressionSyntax cast)
    {
        var inner = Extract(cast.Expression);
        var sourceType = _semantic.GetTypeInfo(cast.Expression).Type;
        var targetType = _semantic.GetTypeInfo(cast).Type;
        if (sourceType is null || targetType is null)
            return new IrCastExpression(inner, ExtractTargetType(cast));

        var sourceSpecial = sourceType.SpecialType;
        var targetSpecial = targetType.SpecialType;
        var sourceIsBigInt = sourceType.ToDisplayString() == "System.Numerics.BigInteger";
        var targetIsBigInt = targetType.ToDisplayString() == "System.Numerics.BigInteger";

        // BigInteger → decimal: new Decimal(value.toString())
        if (sourceIsBigInt && targetSpecial == SpecialType.System_Decimal)
        {
            return new IrNewExpression(
                new IrPrimitiveTypeRef(IrPrimitive.Decimal),
                [new IrArgument(new IrCallExpression(new IrMemberAccess(inner, "toString"), []))]
            );
        }

        // decimal → BigInteger: BigInt(value.toFixed(0))
        if (sourceSpecial == SpecialType.System_Decimal && targetIsBigInt)
        {
            return new IrCallExpression(
                // IrTypeReference (not IrIdentifier) so the TS bridge doesn't
                // camelCase the global JS `BigInt` builtin into `bigInt`.
                new IrTypeReference("BigInt"),
                [
                    new IrArgument(
                        new IrCallExpression(
                            new IrMemberAccess(inner, "toFixed"),
                            [new IrArgument(new IrLiteral(0, IrLiteralKind.Int32))]
                        )
                    ),
                ]
            );
        }

        // decimal → any integer: value.toNumber()
        if (
            sourceSpecial == SpecialType.System_Decimal
            && targetSpecial
                is SpecialType.System_Int16
                    or SpecialType.System_Int32
                    or SpecialType.System_Int64
                    or SpecialType.System_UInt16
                    or SpecialType.System_UInt32
                    or SpecialType.System_UInt64
                    or SpecialType.System_Byte
                    or SpecialType.System_SByte
        )
        {
            return new IrCallExpression(new IrMemberAccess(inner, "toNumber"), []);
        }

        // int/long/decimal → BigInteger: BigInt(value)
        if (
            targetIsBigInt
            && sourceSpecial
                is SpecialType.System_Int32
                    or SpecialType.System_Int64
                    or SpecialType.System_Int16
                    or SpecialType.System_Decimal
        )
        {
            return new IrCallExpression(new IrTypeReference("BigInt"), [new IrArgument(inner)]);
        }

        return new IrCastExpression(inner, ExtractTargetType(cast));
    }
}

/// <summary>
/// Placeholder for expressions that the extractor doesn't yet understand. Backends
/// can either produce a visible <c>TODO</c> in the output or fall back to the
/// legacy source-to-target pipeline for the surrounding body.
/// </summary>
public sealed record IrUnsupportedExpression(string Kind) : IrExpression;
