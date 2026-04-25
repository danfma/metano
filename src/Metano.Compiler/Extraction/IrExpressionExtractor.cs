using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
public sealed class IrExpressionExtractor
{
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

    public IrExpressionExtractor(
        SemanticModel semanticModel,
        IrTypeOriginResolver? originResolver = null,
        Metano.Annotations.TargetLanguage? target = null
    )
        : this(semanticModel, originResolver, target, inlineExpanding: null) { }

    internal IrExpressionExtractor(
        SemanticModel semanticModel,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target,
        HashSet<ISymbol>? inlineExpanding
    )
    {
        _semantic = semanticModel;
        _originResolver = originResolver;
        _target = target;
        _inlineExpanding = inlineExpanding ?? new HashSet<ISymbol>(SymbolEqualityComparer.Default);
    }

    internal HashSet<ISymbol> InlineExpandingSet => _inlineExpanding;

    public IrExpression Extract(ExpressionSyntax expression) =>
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
            MemberAccessExpressionSyntax member => ExtractMemberAccess(member),
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
            IsPatternExpressionSyntax isPattern => new IrIsPatternExpression(
                Extract(isPattern.Expression),
                ExtractPattern(isPattern.Pattern)
            ),
            SwitchExpressionSyntax switchExpr => ExtractSwitchExpression(switchExpr),
            WithExpressionSyntax withExpr => ExtractWithExpression(withExpr),
            CollectionExpressionSyntax coll => ExtractCollectionExpression(coll),
            ConditionalAccessExpressionSyntax cond => ExtractConditionalAccess(cond),
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
            _ => new IrUnsupportedExpression(expression.Kind().ToString()),
        };

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
    /// Lowers <c>value?.member</c> (and <c>value?.member(args)</c>) to the
    /// IR optional-chain / call nodes. Deeper chains
    /// (<c>a?.b?.c</c>, <c>a?.b.c</c>) recurse on the right-hand binding.
    /// </summary>
    private IrExpression ExtractConditionalAccess(ConditionalAccessExpressionSyntax cond)
    {
        var target = Extract(cond.Expression);
        return LowerConditionalBinding(target, cond.WhenNotNull);
    }

    private IrExpression LowerConditionalBinding(
        IrExpression receiver,
        ExpressionSyntax whenNotNull
    )
    {
        switch (whenNotNull)
        {
            case MemberBindingExpressionSyntax mb:
                return new IrOptionalChain(receiver, mb.Name.Identifier.ValueText);

            case InvocationExpressionSyntax inv
                when inv.Expression is MemberBindingExpressionSyntax mbCall:
                var methodSymbol = _semantic.GetSymbolInfo(inv).Symbol;
                var args = inv.ArgumentList.Arguments.Select(ExtractArgument).ToList();
                var chainTarget = new IrOptionalChain(receiver, mbCall.Name.Identifier.ValueText);

                return new IrCallExpression(chainTarget, args, Origin: BuildOrigin(methodSymbol));

            case ConditionalAccessExpressionSyntax nested:
                var head = LowerConditionalBinding(receiver, nested.Expression);

                return LowerConditionalBinding(head, nested.WhenNotNull);

            case AssignmentExpressionSyntax assign
                when assign.Left is MemberBindingExpressionSyntax memberBinding:
                // `a?.b = c` lowers to `a != null && (a.b = c)`.
                // TypeScript has no optional-chained assignment, so
                // the null-guard becomes an inline short-circuit
                // expression. The receiver is evaluated only once
                // when it is a simple identifier / member access (the
                // overwhelming majority of real call sites); a
                // side-effecting receiver would re-evaluate, which is
                // documented as a known edge case until the IR
                // grows a let-binding shape that can host the temp.
                var assignedValue = Extract(assign.Right);
                var memberName = memberBinding.Name.Identifier.ValueText;
                var memberOrigin = BuildOrigin(_semantic.GetSymbolInfo(memberBinding).Symbol);
                var memberWrite = new IrBinaryExpression(
                    new IrMemberAccess(receiver, memberName, memberOrigin),
                    MapAssignmentOp(assign.Kind()),
                    assignedValue
                );
                return new IrBinaryExpression(
                    new IrBinaryExpression(
                        receiver,
                        IrBinaryOp.NotEqual,
                        new IrLiteral(null, IrLiteralKind.Null)
                    ),
                    IrBinaryOp.LogicalAnd,
                    memberWrite
                );

            default:
                return new IrUnsupportedExpression($"ConditionalAccess({whenNotNull.Kind()})");
        }
    }

    private IrExpression ExtractCollectionExpression(CollectionExpressionSyntax coll)
    {
        var elements = coll
            .Elements.OfType<ExpressionElementSyntax>()
            .Select(e => Extract(e.Expression))
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
            assignments.Add(new IrWithAssignment(memberName, Extract(assign.Right)));
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
            ConstantPatternSyntax constant => new IrConstantPattern(Extract(constant.Expression)),
            DeclarationPatternSyntax decl => new IrTypePattern(
                ResolvePatternType(decl.Type),
                ResolveDesignatorName(decl.Designation)
            ),
            TypePatternSyntax typeOnly => new IrTypePattern(ResolvePatternType(typeOnly.Type)),
            VarPatternSyntax varPat when varPat.Designation is SingleVariableDesignationSyntax v =>
                new IrVarPattern(v.Identifier.ValueText),
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

    private IrRelationalPattern ExtractRelationalPattern(RelationalPatternSyntax rel)
    {
        var op = rel.OperatorToken.Text switch
        {
            "<" => IrRelationalOp.LessThan,
            "<=" => IrRelationalOp.LessThanOrEqual,
            ">" => IrRelationalOp.GreaterThan,
            ">=" => IrRelationalOp.GreaterThanOrEqual,
            _ => IrRelationalOp.LessThan,
        };
        return new IrRelationalPattern(op, Extract(rel.Expression));
    }

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
        var designator = ResolveDesignatorName(recursive.Designation);

        // Positional pattern — `(x, y)` or `Point(x, y)` — decomposes a
        // tuple or calls Deconstruct on a record. Property patterns land on
        // the same syntax node but use PropertyPatternClause instead.
        if (recursive.PositionalPatternClause is not null)
        {
            var elements = recursive
                .PositionalPatternClause.Subpatterns.Select(s => ExtractPattern(s.Pattern))
                .ToList();
            return new IrPositionalPattern(type, elements, designator);
        }

        var subs = recursive
            .PropertyPatternClause?.Subpatterns.Where(s => s.NameColon is not null)
            .Select(s => new IrPropertySubpattern(
                s.NameColon!.Name.Identifier.ValueText,
                ExtractPattern(s.Pattern)
            ))
            .ToList();
        return new IrPropertyPattern(
            type,
            subs ?? (IReadOnlyList<IrPropertySubpattern>)[],
            designator
        );
    }

    private IrTypeRef ResolvePatternType(TypeSyntax typeSyntax)
    {
        var resolved = _semantic.GetTypeInfo(typeSyntax).Type;
        return resolved is not null
            ? IrTypeRefMapper.Map(resolved, _originResolver, _target)
            : new IrUnknownTypeRef();
    }

    private static string? ResolveDesignatorName(VariableDesignationSyntax? designation) =>
        designation is SingleVariableDesignationSyntax single ? single.Identifier.ValueText : null;

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
            return new IrTypeReference(generic.Identifier.ValueText);

        if (
            symbol
                is { IsStatic: false }
                    and (IPropertySymbol or IFieldSymbol or IEventSymbol or IMethodSymbol)
            && symbol.ContainingType is not null
            && !IsLocalLikeSymbol(symbol)
        )
            return WrapMethodGroupForThisDelegate(
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
            return WrapMethodGroupForThisDelegate(
                generic,
                symbol,
                new IrMemberAccess(
                    new IrTypeReference(symbol.ContainingType.Name),
                    generic.Identifier.ValueText,
                    BuildOrigin(symbol)
                )
            );

        return WrapMethodGroupForThisDelegate(
            generic,
            symbol,
            new IrIdentifier(generic.Identifier.ValueText)
        );
    }

    private IrExpression ExtractIdentifierName(IdentifierNameSyntax id)
    {
        var symbol = _semantic.GetSymbolInfo(id).Symbol;
        if (symbol is ITypeSymbol or INamespaceSymbol)
            return new IrTypeReference(id.Identifier.ValueText);

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
            return WrapMethodGroupForThisDelegate(id, symbol, memberAccess);
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
                new IrTypeReference(symbol.ContainingType.Name),
                id.Identifier.ValueText,
                BuildOrigin(symbol)
            );
            return WrapMethodGroupForThisDelegate(id, symbol, staticAccess);
        }

        return WrapMethodGroupForThisDelegate(
            id,
            symbol,
            new IrIdentifier(id.Identifier.ValueText)
        );
    }

    private static bool IsLocalLikeSymbol(ISymbol symbol) =>
        symbol is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol;

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
    /// Mirrors the legacy <see cref="Metano.Transformation.LiteralHandler"/>
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
        type.NullableAnnotation == NullableAnnotation.Annotated
        || (
            type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
        );

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
        {
            return new IrIsPatternExpression(
                Extract(bin.Left),
                new IrTypePattern(ResolvePatternType(typeRhs))
            );
        }

        // decimal arithmetic needs a special lowering: legacy emits
        // `a.plus(b)` / `.times(b)` / `.div(b)` / etc. on the decimal.js
        // Decimal class rather than raw `+` / `*` / `/`. Detect via
        // SemanticModel and normalize to an IrCallExpression so every
        // backend that supports BCL-style dispatch produces the same
        // shape without re-walking types later.
        if (
            MapDecimalBinaryMethod(bin.Kind()) is { } decimalMethod
            && IsDecimalOperand(bin.Left)
            && IsDecimalOperand(bin.Right)
        )
        {
            return BuildDecimalBinaryCall(bin, decimalMethod);
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
            && MapRelationalOp(bin.Kind()) is { } relOp
            && GetTemporalTypeName(bin.Left, bin.Right) is { } temporalTypeName
        )
        {
            return BuildTemporalCompareCall(bin, relOp, temporalTypeName);
        }

        var op = MapBinaryOp(bin.Kind());
        return new IrBinaryExpression(Extract(bin.Left), op, Extract(bin.Right));
    }

    /// <summary>
    /// Returns the target Temporal subtype name
    /// (<c>Temporal.PlainDate</c>, <c>Temporal.PlainDateTime</c>, …)
    /// when either operand of a binary expression is one of the
    /// Temporal-backed BCL types. Returns <c>null</c> otherwise so
    /// the caller falls through to the regular operator lowering.
    /// </summary>
    private string? GetTemporalTypeName(ExpressionSyntax left, ExpressionSyntax right) =>
        GetTemporalTypeName(left) ?? GetTemporalTypeName(right);

    private string? GetTemporalTypeName(ExpressionSyntax expression)
    {
        var info = _semantic.GetTypeInfo(expression);
        var type = info.ConvertedType ?? info.Type;
        if (type is null)
            return null;

        // Nullable-value wrappers (`DateOnly?`, `TimeSpan?`, …) still
        // trip the same runtime error when the inner value is used in
        // a relational operator. Peel `System.Nullable<T>` so the
        // underlying Temporal-backed type resolves against the map
        // below.
        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                TypeArguments: [{ } inner],
            }
        )
            type = inner;

        // BCL types map to Temporal subtypes. `DateTime` / `TimeSpan`
        // carry `SpecialType`s so `OriginalDefinition.ToDisplayString`
        // is the reliable key here rather than the `SpecialType.None`
        // fast path.
        return type.OriginalDefinition.ToDisplayString() switch
        {
            "System.DateTime" => "Temporal.PlainDateTime",
            "System.DateTimeOffset" => "Temporal.ZonedDateTime",
            "System.DateOnly" => "Temporal.PlainDate",
            "System.TimeOnly" => "Temporal.PlainTime",
            "System.TimeSpan" => "Temporal.Duration",
            _ => null,
        };
    }

    private static IrBinaryOp? MapRelationalOp(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.GreaterThanExpression => IrBinaryOp.GreaterThan,
            SyntaxKind.GreaterThanOrEqualExpression => IrBinaryOp.GreaterThanOrEqual,
            SyntaxKind.LessThanExpression => IrBinaryOp.LessThan,
            SyntaxKind.LessThanOrEqualExpression => IrBinaryOp.LessThanOrEqual,
            _ => null,
        };

    private IrExpression BuildTemporalCompareCall(
        BinaryExpressionSyntax bin,
        IrBinaryOp op,
        string temporalTypeName
    )
    {
        // Emit the qualified Temporal type name as an IrTypeReference
        // so the bridge preserves the original PascalCase (type
        // references bypass the camelCase member-access policy that
        // would otherwise turn `.PlainDate` into `.plainDate`).
        var call = new IrCallExpression(
            new IrMemberAccess(new IrTypeReference(temporalTypeName), "compare"),
            [new IrArgument(Extract(bin.Left)), new IrArgument(Extract(bin.Right))]
        );
        return new IrBinaryExpression(call, op, new IrLiteral(0, IrLiteralKind.Int32));
    }

    private bool IsDecimalOperand(ExpressionSyntax expr)
    {
        var info = _semantic.GetTypeInfo(expr);
        var t = info.ConvertedType ?? info.Type;
        return t?.SpecialType == SpecialType.System_Decimal;
    }

    /// <summary>
    /// C# binary operator → decimal.js method name. Comparison forms that
    /// need a logical negation (<c>!=</c>) carry a leading <c>"!"</c> so the
    /// builder wraps the call in <c>IrUnaryExpression(LogicalNot, …)</c>.
    /// </summary>
    private static string? MapDecimalBinaryMethod(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.AddExpression => "plus",
            SyntaxKind.SubtractExpression => "minus",
            SyntaxKind.MultiplyExpression => "times",
            SyntaxKind.DivideExpression => "div",
            SyntaxKind.ModuloExpression => "mod",
            SyntaxKind.EqualsExpression => "eq",
            SyntaxKind.NotEqualsExpression => "!eq",
            SyntaxKind.LessThanExpression => "lt",
            SyntaxKind.GreaterThanExpression => "gt",
            SyntaxKind.LessThanOrEqualExpression => "lte",
            SyntaxKind.GreaterThanOrEqualExpression => "gte",
            _ => null,
        };

    private IrExpression BuildDecimalBinaryCall(BinaryExpressionSyntax bin, string method)
    {
        var negate = method.StartsWith('!');
        if (negate)
            method = method[1..];
        var left = Extract(bin.Left);
        var right = Extract(bin.Right);
        IrExpression call = new IrCallExpression(
            new IrMemberAccess(left, method),
            [new IrArgument(right)]
        );
        if (negate)
            call = new IrUnaryExpression(IrUnaryOp.LogicalNot, call);
        return call;
    }

    private IrExpression ExtractAssignment(AssignmentExpressionSyntax assign)
    {
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
        return new IrBinaryExpression(
            Extract(assign.Left),
            MapAssignmentOp(assign.Kind()),
            Extract(assign.Right)
        );
    }

    private IrExpression ExtractUnary(ExpressionSyntax node, bool isPrefix)
    {
        var (op, operand) = node switch
        {
            PrefixUnaryExpressionSyntax pre => (MapUnaryOp(pre.Kind()), pre.Operand),
            PostfixUnaryExpressionSyntax post => (MapUnaryOp(post.Kind()), post.Operand),
            _ => throw new ArgumentException("Not a unary expression", nameof(node)),
        };
        // Decimal negation: `-x` on a decimal.js receiver isn't a JS unary op —
        // the value is a Decimal instance, so rewrite to `x.neg()`.
        if (op is IrUnaryOp.Negate && isPrefix)
        {
            var operandType = _semantic.GetTypeInfo(operand).Type;
            if (operandType?.SpecialType == SpecialType.System_Decimal)
                return new IrCallExpression(new IrMemberAccess(Extract(operand), "neg"), []);
        }
        return new IrUnaryExpression(op, Extract(operand), isPrefix);
    }

    private static IrBinaryOp MapBinaryOp(SyntaxKind kind) =>
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
            _ => IrBinaryOp.Add, // fallback; unsupported kinds land here but are rare
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
            SyntaxKind.CoalesceAssignmentExpression => IrBinaryOp.NullCoalescingAssign,
            _ => IrBinaryOp.Assign,
        };

    private static IrUnaryOp MapUnaryOp(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.UnaryMinusExpression => IrUnaryOp.Negate,
            SyntaxKind.LogicalNotExpression => IrUnaryOp.LogicalNot,
            SyntaxKind.BitwiseNotExpression => IrUnaryOp.BitwiseNot,
            SyntaxKind.PreIncrementExpression or SyntaxKind.PostIncrementExpression =>
                IrUnaryOp.Increment,
            SyntaxKind.PreDecrementExpression or SyntaxKind.PostDecrementExpression =>
                IrUnaryOp.Decrement,
            _ => IrUnaryOp.Negate,
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

    private IrExpression ExtractMemberAccess(MemberAccessExpressionSyntax member)
    {
        // `decimal.Zero` / `.One` / `.MinusOne` — no static counterpart in
        // decimal.js, so synthesize `new Decimal(N)` matching the legacy
        // BclMapper behavior. Similarly `decimal.Parse(s)` rewrites to
        // `new Decimal(s)`.
        var symbol = _semantic.GetSymbolInfo(member).Symbol;
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

        var target = Extract(member.Expression);
        var name = member.Name.Identifier.ValueText;

        // `Nullable<T>.Value` is a no-op at the TS level — `T?` nullable
        // values are the value itself. Elide the `.Value` suffix so
        // `patch.Priority.Value` lowers to `patch.priority` instead of
        // dereferencing a property that doesn't exist at runtime.
        if (name == "Value")
        {
            var receiverType = _semantic.GetTypeInfo(member.Expression).Type;
            if (
                receiverType is INamedTypeSymbol
                {
                    OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                }
            )
                return target;
        }

        // `[Inline]` fields and properties substitute their initializer
        // (or getter body) at every access site. When expansion
        // succeeds, the member access is replaced by the extracted
        // initializer expression; otherwise fall through to the normal
        // access path so diagnostics still surface at validation time.
        if (TryExpandInlineAccess(symbol) is { } inlined)
            return inlined;

        var origin = BuildOrigin(_semantic.GetSymbolInfo(member).Symbol);
        var memberAccess = new IrMemberAccess(target, name, origin);
        return WrapMethodGroupForThisDelegate(member, symbol, memberAccess);
    }

    /// <summary>
    /// Method-group conversion to a <c>[This]</c>-bearing delegate: a
    /// reference like <c>button.OnClick = handler</c> (or the static
    /// equivalent) implicitly funnels the dispatcher's JS <c>this</c>
    /// into the method's first parameter at every invocation. The
    /// extractor wraps the method reference in a runtime
    /// <c>bindReceiver(...)</c> call so the resulting TS function
    /// rebinds <c>this</c> the same way a <c>[This]</c> lambda does.
    /// Plain identifier references (calls or non-delegate uses)
    /// fall through unchanged.
    /// <para>
    /// Instance method-group references additionally
    /// <c>.bind(receiver)</c> the inner reference before handing it
    /// to <c>bindReceiver</c>. C# method groups capture the instance
    /// as the call-time receiver, so a subsequent <c>fn(args)</c>
    /// dispatch inside <c>bindReceiver</c>'s trampoline must already
    /// know which object owns the method — otherwise the body's own
    /// <c>this</c> would be lost. Static method groups skip the
    /// <c>.bind</c> step (no instance to capture).
    /// </para>
    /// </summary>
    private IrExpression WrapMethodGroupForThisDelegate(
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
        if (invoke.Parameters.Length == 0 || !SymbolHelper.HasThis(invoke.Parameters[0]))
            return reference;

        var inner = reference;
        if (
            !method.IsStatic
            && reference is IrMemberAccess { Target: { } receiver } memberAccess
            && receiver is not IrTypeReference
        )
        {
            inner = new IrCallExpression(
                new IrMemberAccess(memberAccess, "bind"),
                [new IrArgument(receiver)]
            );
        }

        return new IrCallExpression(new IrIdentifier("bindReceiver"), [new IrArgument(inner)]);
    }

    /// <summary>
    /// If <paramref name="symbol"/> carries <c>[Inline]</c> and resolves
    /// to a supported shape (<c>static readonly</c> field with
    /// initializer, or <c>static</c> property with an expression-bodied
    /// getter), returns the extracted initializer expression.
    /// Guards against recursion by tracking in-progress symbols in
    /// <see cref="_inlineExpanding"/>. Unsupported shapes and cycles
    /// return <c>null</c> so the caller falls back to a regular
    /// access; the validator surfaces the diagnostic separately.
    /// </summary>
    private IrExpression? TryExpandInlineAccess(ISymbol? symbol)
    {
        if (symbol is null || !SymbolHelper.HasInline(symbol))
            return null;
        if (!_inlineExpanding.Add(symbol))
            return null;
        try
        {
            var initializer = TryFindInlineInitializer(symbol);
            if (initializer is null)
                return null;

            // Cross-assembly guard: when the `[Inline]` member lives
            // in a referenced assembly, its SyntaxTree belongs to the
            // declaring compilation, not ours. Calling
            // `GetSemanticModel` with a tree the current compilation
            // does not own throws `ArgumentException` ("SyntaxTree is
            // not part of the compilation"). Cross-assembly inline
            // substitution is tracked as a follow-up — for now the
            // caller falls through to a regular member access so the
            // transpiler does not crash.
            if (!_semantic.Compilation.ContainsSyntaxTree(initializer.SyntaxTree))
                return null;

            // Reuse the declaring syntax tree's SemanticModel so
            // constant folding + symbol resolution inside the
            // initializer reflect the declaration site, not the call
            // site. The cycle-tracking set is shared with the nested
            // extractor so a transitive reference back to the
            // original member bails out instead of recursing.
            var extractor = new IrExpressionExtractor(
                _semantic.Compilation.GetSemanticModel(initializer.SyntaxTree),
                _originResolver,
                _target,
                _inlineExpanding
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

    private IrExpression ExtractInvocation(InvocationExpressionSyntax inv)
    {
        var symbol = _semantic.GetSymbolInfo(inv).Symbol as IMethodSymbol;

        // `decimal.Parse(s)` → `new Decimal(s)`.
        if (
            symbol is { IsStatic: true, Name: "Parse" }
            && symbol.ContainingType?.ToDisplayString() == "decimal"
            && inv.ArgumentList.Arguments.Count >= 1
        )
        {
            var arg = Extract(inv.ArgumentList.Arguments[0].Expression);
            return new IrNewExpression(
                new IrPrimitiveTypeRef(IrPrimitive.Decimal),
                [new IrArgument(arg)]
            );
        }

        // `[Emit("…$0…")]` rewrites the call into an inline template
        // expansion. The template author owns every placeholder — for
        // instance methods `$0` typically references the receiver, for
        // static methods the first argument. We carry the raw template in
        // the IR and let backends decide how to expand it.
        if (symbol is not null)
        {
            var emitTemplate = GetEmitTemplate(symbol);
            if (emitTemplate is not null)
            {
                // For an instance-method `[Emit]`, synthesize the receiver
                // as the first arg so backends don't have to rediscover
                // the elision: `value.DoIt(x)` becomes a template call
                // with args [value, x].
                var templateArgs = new List<IrExpression>();
                if (!symbol.IsStatic && inv.Expression is MemberAccessExpressionSyntax memberAccess)
                    templateArgs.Add(Extract(memberAccess.Expression));
                templateArgs.AddRange(
                    inv.ArgumentList.Arguments.Select(a => Extract(a.Expression))
                );
                return new IrTemplateExpression(emitTemplate, Receiver: null, templateArgs);
            }
        }

        // `Math.Round(decimal)` / `Math.Floor(decimal)` / `Math.Ceiling(decimal)` /
        // `Math.Abs(decimal)` have no number-only equivalent in decimal.js — each
        // Decimal instance carries its own instance method. Rewrite to the
        // receiver's method call so `Math.Round(amount)` becomes
        // `amount.round()` on the TS side, matching the legacy
        // InvocationHandler.TryRewriteMathDecimal behavior.
        if (TryRewriteMathDecimalCall(inv, symbol) is { } mathRewrite)
            return mathRewrite;

        var target = Extract(inv.Expression);
        var args = inv.ArgumentList.Arguments.Select(ExtractArgument).ToList();
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

        return new IrCallExpression(target, args, typeArguments, BuildOrigin(symbol));
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

    private IrExpression? TryRewriteMathDecimalCall(
        InvocationExpressionSyntax inv,
        IMethodSymbol? symbol
    )
    {
        if (
            symbol is null
            || symbol.ContainingType?.ToDisplayString() != "System.Math"
            || inv.ArgumentList.Arguments.Count != 1
        )
            return null;

        var argSyntax = inv.ArgumentList.Arguments[0].Expression;
        var argType = _semantic.GetTypeInfo(argSyntax).Type;
        if (argType?.SpecialType != SpecialType.System_Decimal)
            return null;

        var methodName = symbol.Name switch
        {
            "Round" => "round",
            "Floor" => "floor",
            "Ceiling" => "ceil",
            "Abs" => "abs",
            _ => null,
        };
        if (methodName is null)
            return null;

        return new IrCallExpression(new IrMemberAccess(Extract(argSyntax), methodName), []);
    }

    /// <summary>
    /// Reads the raw template string from a symbol's <c>[Emit("…")]</c>
    /// attribute. Returns <c>null</c> when the attribute is absent so the
    /// caller falls back to the ordinary call lowering.
    /// </summary>
    private static string? GetEmitTemplate(ISymbol symbol) =>
        symbol
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name is "EmitAttribute" or "Emit")
            ?.ConstructorArguments.FirstOrDefault()
            .Value as string;

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
        var isInlineWrapperMember =
            symbol is IMethodSymbol && SymbolHelper.HasInlineWrapper(symbol.ContainingType);
        var isPlainObjectInstanceMethod =
            symbol is IMethodSymbol { IsStatic: false, MethodKind: MethodKind.Ordinary }
            && SymbolHelper.HasPlainObject(symbol.ContainingType);
        // `[Name("x")]` (target-aware) is resolved once here so backends
        // consult the emitted name instead of re-scanning attributes.
        var emittedName = SymbolHelper.GetNameOverride(symbol, _target);
        // `[External]` (TS-specific) and `[Erasable]`
        // (cross-target) both cause static member access to flatten
        // to a bare identifier at the call site, but they express
        // different intents (runtime-provided stub vs. compile-time
        // sugar container). The flags stay distinct so later slices
        // can diverge their lowering paths without churn; today's
        // bridge honors either to drop the enclosing type reference.
        var isDeclaringTypeExternal = SymbolHelper.HasExternal(symbol.ContainingType);
        var isDeclaringTypeErasable = SymbolHelper.HasErasable(symbol.ContainingType);
        return new IrMemberOrigin(
            declaringTypeName,
            symbol.Name,
            symbol.IsStatic,
            isEnumMember,
            isInlineWrapperMember,
            EmittedName: emittedName,
            IsPlainObjectInstanceMethod: isPlainObjectInstanceMethod,
            IsStringEnumMember: isStringEnumMember,
            IsDeclaringTypeExternal: isDeclaringTypeExternal,
            IsDeclaringTypeErasable: isDeclaringTypeErasable
        );
    }

    private IrExpression ExtractElementAccess(ElementAccessExpressionSyntax elem)
    {
        var target = Extract(elem.Expression);
        // Treat the first argument as the index; multi-arg indexers are uncommon.
        var index =
            elem.ArgumentList.Arguments.Count > 0
                ? Extract(elem.ArgumentList.Arguments[0].Expression)
                : new IrLiteral(0, IrLiteralKind.Int32);
        var receiverType = _semantic.GetTypeInfo(elem.Expression).Type;
        var mappedType = receiverType is not null
            ? IrTypeRefMapper.Map(receiverType, _originResolver, _target)
            : null;
        return new IrElementAccess(target, index, mappedType);
    }

    // ── Lambdas ──────────────────────────────────────────────────────────

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
            ReturnType: null,
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
            ReturnType: null,
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
            ExpressionSyntax expr => [new IrReturnStatement(Extract(expr))],
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
                    parts.Add(new IrInterpolationText(text.TextToken.ValueText));
                    break;
                case InterpolationSyntax expr:
                    var formatSpec = expr.FormatClause?.FormatStringToken.ValueText;
                    parts.Add(new IrInterpolationExpression(Extract(expr.Expression), formatSpec));
                    break;
            }
        }
        return new IrStringInterpolation(parts);
    }

    // ── Object creation ──────────────────────────────────────────────────

    /// <summary>
    /// Turns one Roslyn argument node into an <see cref="IrArgument"/>,
    /// capturing its source-side name when the caller used the
    /// <c>Name: value</c> shorthand so backends that care (Dart's named
    /// arguments, the TS dispatcher's reordering pass) can reconstruct the
    /// original intent.
    /// </summary>
    private IrArgument ExtractArgument(ArgumentSyntax argument) =>
        new(Extract(argument.Expression), argument.NameColon?.Name.Identifier.ValueText);

    private IrExpression ExtractObjectCreation(ObjectCreationExpressionSyntax oc)
    {
        var args = oc.ArgumentList?.Arguments.Select(ExtractArgument).ToList() ?? [];
        var typeSymbol = _semantic.GetTypeInfo(oc).Type;
        var type = typeSymbol is not null
            ? IrTypeRefMapper.Map(typeSymbol, _originResolver, _target)
            : new IrUnknownTypeRef();
        return BuildNewExpression(type, args, typeSymbol, oc);
    }

    private IrExpression ExtractImplicitObjectCreation(ImplicitObjectCreationExpressionSyntax ioc)
    {
        // `new(args)` — type comes from the target context; Roslyn resolves it for us.
        var args = ioc.ArgumentList.Arguments.Select(ExtractArgument).ToList();
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
    private IrNewExpression BuildNewExpression(
        IrTypeRef type,
        IReadOnlyList<IrArgument> args,
        ITypeSymbol? typeSymbol,
        ExpressionSyntax creationSyntax
    )
    {
        var isPlainObject =
            typeSymbol is INamedTypeSymbol named && SymbolHelper.HasPlainObject(named);
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

        return new IrNewExpression(type, args, isPlainObject, parameterNames);
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

    private static IrExpression BuildDefaultArgument(IParameterSymbol parameter)
    {
        if (parameter.HasExplicitDefaultValue)
            return BuildLiteralForDefault(parameter.ExplicitDefaultValue, parameter.Type);
        // No explicit default — surface an IrLiteral(Default) so the printer
        // emits a visible marker (`undefined`) rather than producing invalid
        // code. Roslyn should never let us reach this branch for a well-formed
        // call, but we stay defensive.
        return new IrLiteral(null, IrLiteralKind.Default);
    }

    private static IrExpression BuildLiteralForDefault(object? value, ITypeSymbol type)
    {
        if (value is null)
            return new IrLiteral(null, IrLiteralKind.Null);
        // Enums surface their underlying numeric constant in
        // ExplicitDefaultValue. Translate back to a member access on the enum
        // type so the backend lowering prints `Priority.Medium` rather than a
        // raw `1`.
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            var member = enumType
                .GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, value));
            if (member is not null)
                return new IrMemberAccess(new IrTypeReference(enumType.Name), member.Name);
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
