using System.Globalization;
using Metano.Annotations;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Converts target-agnostic <see cref="IrExpression"/> nodes into TypeScript AST
/// expressions. Handles the subset of IR the current extractors produce; unsupported
/// kinds fall through to a <see cref="TsIdentifier"/> carrying a <c>/* TODO */</c>
/// comment so the generated file remains syntactically valid.
/// </summary>
public static class IrToTsExpressionBridge
{
    /// <summary>
    /// Maps an IR expression to TS. When <paramref name="bclRegistry"/> is provided,
    /// member accesses and calls consult <see cref="IrToTsBclMapper"/> first and
    /// fall back to the raw lowering when no entry matches. Without a registry the
    /// bridge emits raw <c>receiver.member</c> / <c>receiver.method(args)</c>.
    /// </summary>
    public static TsExpression Map(
        IrExpression expression,
        DeclarativeMappingRegistry? bclRegistry = null
    ) =>
        expression switch
        {
            IrLiteral lit => MapLiteral(lit),
            IrIdentifier id => new TsIdentifier(TypeScriptNaming.ToCamelCase(id.Name)),
            IrTypeReference tr => new TsIdentifier(IrToTsTypeMapper.ResolveAliasedName(tr.Name)),
            IrThisExpression => new TsIdentifier("this"),
            IrBaseExpression => new TsIdentifier("super"),
            IrMemberAccess ma => MapMemberAccess(ma, bclRegistry),
            IrElementAccess ea => MapElementAccess(ea, bclRegistry),
            IrCallExpression call => MapCall(call, bclRegistry),
            IrNewExpression ne => MapNewExpression(ne, bclRegistry),
            IrBinaryExpression bin => MapBinary(bin, bclRegistry),
            IrUnaryExpression un when un.IsPrefix => new TsUnaryExpression(
                PrefixOperatorToken(un.Operator),
                Map(un.Operand, bclRegistry)
            ),
            IrUnaryExpression un => new TsPostfixUnaryExpression(
                Map(un.Operand, bclRegistry),
                PostfixOperatorToken(un.Operator)
            ),
            IrConditionalExpression cond => new TsConditionalExpression(
                Map(cond.Condition, bclRegistry),
                Map(cond.WhenTrue, bclRegistry),
                Map(cond.WhenFalse, bclRegistry)
            ),
            IrAwaitExpression aw => new TsAwaitExpression(Map(aw.Expression, bclRegistry)),
            IrCastExpression cast => Map(cast.Expression, bclRegistry), // JS has no runtime cast
            IrLambdaExpression lambda => MapLambda(lambda, bclRegistry),
            IrStringInterpolation interp => MapStringInterpolation(interp, bclRegistry),
            IrIsPatternExpression isPattern => MapIsPattern(isPattern, bclRegistry),
            IrSwitchExpression sw => MapSwitchExpression(sw, bclRegistry),
            IrWithExpression w => MapWithExpression(w, bclRegistry),
            IrArrayLiteral arr => new TsArrayLiteral(
                arr.Elements.Select(e => Map(e, bclRegistry)).ToList()
            ),
            IrTemplateExpression tpl => new TsTemplate(
                tpl.Template,
                tpl.Receiver is null ? null : Map(tpl.Receiver, bclRegistry),
                tpl.Arguments.Select(a => Map(a, bclRegistry)).ToList()
            )
            {
                TypeArgumentNames = tpl.TypeArguments is { Count: > 0 } tArgs
                    ? tArgs.Select(t => TsTypeArgName(t) ?? "unknown").ToList()
                    : [],
                RuntimeImports = tpl.RequiredImports ?? [],
                ExternalImports = tpl.ExternalImports ?? [],
            },
            IrYieldExpression ye => new TsIdentifier(
                ye.Value is not null ? $"yield {FormatInner(ye.Value)}" : "yield"
            ),
            IrOptionalChain chain => MapOptionalChain(chain, bclRegistry),
            IrObjectLiteral obj => new TsObjectLiteral(
                obj.Properties.Select(p => new TsObjectProperty(p.Name, Map(p.Value, bclRegistry)))
                    .ToList()
            ),
            IrThrowExpression th => new TsCallExpression(
                new TsParenthesized(
                    new TsArrowFunction([], [new TsThrowStatement(Map(th.Expression, bclRegistry))])
                ),
                Array.Empty<TsExpression>()
            ),
            IrLinqChain chain => MapLinqChain(chain, bclRegistry),
            IrLetExpression let => MapLetExpression(let, bclRegistry),
            IrUnsupportedExpression u => new TsIdentifier(
                $"/* TODO: unsupported IR expression {u.Kind} */"
            ),
            _ => new TsIdentifier($"/* TODO: {expression.GetType().Name} */"),
        };

    /// <summary>
    /// Binary expressions route through the generic lowering, with one
    /// special case: <c>dict[key] = value</c> on a
    /// <see cref="IrMapTypeRef"/> receiver must become
    /// <c>dict.set(key, value)</c> — JS <c>Map</c> has no indexer.
    /// Reads (<c>dict[key]</c>) are rewritten elsewhere to
    /// <c>dict.get(key)</c>.
    /// </summary>
    private static TsExpression MapBinary(
        IrBinaryExpression bin,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (
            bin.Operator is IrBinaryOp.Assign
            && bin.Left is IrElementAccess { TargetType: IrMapTypeRef } ea
        )
        {
            var target = Map(ea.Target, bclRegistry);
            var key = Map(ea.Index, bclRegistry);
            var value = Map(bin.Right, bclRegistry);
            return new TsCallExpression(new TsPropertyAccess(target, "set"), [key, value]);
        }
        var left = WrapForPrecedence(
            Map(bin.Left, bclRegistry),
            bin.Left,
            bin.Operator,
            isLeft: true
        );
        var right = WrapForPrecedence(
            Map(bin.Right, bclRegistry),
            bin.Right,
            bin.Operator,
            isLeft: false
        );
        return new TsBinaryExpression(
            left,
            BinaryOperatorToken(bin.Operator, bin.Left, bin.Right),
            right
        );
    }

    /// <summary>
    /// JS doesn't auto-parenthesize lower-precedence sub-expressions, so the
    /// bridge wraps a binary operand whose own operator has lower precedence
    /// than the outer one — preserving the source-side grouping
    /// (<c>(a - 1) * b</c> stays <c>(a - 1) * b</c> instead of collapsing to
    /// <c>a - 1 * b</c>).
    /// </summary>
    private static TsExpression WrapForPrecedence(
        TsExpression lowered,
        IrExpression source,
        IrBinaryOp outerOp,
        bool isLeft
    )
    {
        if (source is not IrBinaryExpression inner)
            return lowered;
        var innerPrec = OperatorPrecedence(inner.Operator);
        var outerPrec = OperatorPrecedence(outerOp);
        if (innerPrec < outerPrec)
            return new TsParenthesized(lowered);
        // Same precedence on the right side of a non-associative or
        // left-associative operator also needs parentheses.
        if (innerPrec == outerPrec && !isLeft && !IsRightAssociative(outerOp))
            return new TsParenthesized(lowered);
        return lowered;
    }

    private static int OperatorPrecedence(IrBinaryOp op) =>
        op switch
        {
            IrBinaryOp.Multiply or IrBinaryOp.Divide or IrBinaryOp.Modulo => 12,
            IrBinaryOp.Add or IrBinaryOp.Subtract => 11,
            IrBinaryOp.LeftShift or IrBinaryOp.RightShift or IrBinaryOp.UnsignedRightShift => 10,
            IrBinaryOp.LessThan
            or IrBinaryOp.LessThanOrEqual
            or IrBinaryOp.GreaterThan
            or IrBinaryOp.GreaterThanOrEqual => 9,
            IrBinaryOp.Equal or IrBinaryOp.NotEqual => 8,
            IrBinaryOp.BitwiseAnd => 7,
            IrBinaryOp.BitwiseXor => 6,
            IrBinaryOp.BitwiseOr => 5,
            IrBinaryOp.LogicalAnd => 4,
            IrBinaryOp.LogicalOr => 3,
            IrBinaryOp.NullCoalescing => 3,
            _ => 1, // assignments are right-associative and lowest
        };

    private static bool IsRightAssociative(IrBinaryOp op) =>
        op
            is IrBinaryOp.Assign
                or IrBinaryOp.AddAssign
                or IrBinaryOp.SubtractAssign
                or IrBinaryOp.MultiplyAssign
                or IrBinaryOp.DivideAssign
                or IrBinaryOp.ModuloAssign
                or IrBinaryOp.BitwiseAndAssign
                or IrBinaryOp.BitwiseOrAssign
                or IrBinaryOp.BitwiseXorAssign
                or IrBinaryOp.LeftShiftAssign
                or IrBinaryOp.RightShiftAssign
                or IrBinaryOp.UnsignedRightShiftAssign
                or IrBinaryOp.NullCoalescingAssign
                or IrBinaryOp.NullCoalescing;

    private static TsExpression MapElementAccess(
        IrElementAccess ea,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var target = Map(ea.Target, bclRegistry);
        var index = Map(ea.Index, bclRegistry);
        if (ea.TargetType is IrMapTypeRef)
            return new TsCallExpression(new TsPropertyAccess(target, "get"), [index]);
        return new TsElementAccess(target, index);
    }

    private static TsExpression MapMemberAccess(
        IrMemberAccess ma,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var loweredTarget = Map(ma.Target, bclRegistry);
        if (bclRegistry is not null)
        {
            var mapped = IrToTsBclMapper.TryMapMemberAccess(ma, loweredTarget, bclRegistry);
            if (mapped is not null)
                return mapped;
        }
        // Enum members preserve source-casing — the TS backend emits enums as
        // `enum X { Backlog, Ready, … }` (numeric) or a string literal union
        // keyed by the original PascalCase; camelCasing here would produce
        // `X.backlog` that doesn't exist at runtime.
        //
        // `[Branded]` methods surface as namespace functions
        // (`namespace UserId { export function new_() {} }`), and namespace
        // function declarations DO require reserved-word escapes — so the
        // call site has to match by using the escaping camelCase variant.
        string memberName;
        // String enum members: `[Name("medium")]` renames the runtime
        // *value* (the string literal), not the property key. So
        // `IssuePriority.Medium` stays `Medium` at the access site even
        // when the enum field carries a [Name] override.
        if (ma.Origin?.IsStringEnumMember == true)
            memberName = ma.MemberName;
        // Numeric enum members: `[Name("InProgress")]` renames the
        // property key to the override (legacy behavior). Otherwise keep
        // the source PascalCase — the TS enum object exposes members by
        // their C# name verbatim.
        else if (ma.Origin?.IsEnumMember == true)
            memberName = ma.Origin.EmittedName ?? ma.MemberName;
        // `[Name("x")]` (target-aware) wins over any casing policy for
        // ordinary members — the emitted name comes through verbatim.
        else if (ma.Origin?.EmittedName is { } overridden)
            memberName = overridden;
        else if (ma.Origin?.IsBrandedMember == true)
            memberName = TypeScriptNaming.ToCamelCase(ma.MemberName);
        else
            memberName = TypeScriptNaming.ToCamelCaseMember(ma.MemberName);
        // `[External]` and `[NoContainer]` declaring types: the class
        // vanishes at the call site. `[External]` groups runtime
        // `[NoContainer]` marks a compile-time sugar container — its scope
        // vanishes at the call site, so `Constants.Pi` lowers to just
        // `Pi`. `[External]` no longer participates in this flatten
        // (per #106): an `[External]` static class stays
        // class-qualified at the call site so the emitted TS keeps the
        // namespace contract the runtime ambient declarations expose
        // (e.g. `Js.document` over the runtime's actual `document`
        // global). Instance access (ill-formed on a static class)
        // falls through to the normal property-access path.
        if (
            ma.Origin is { IsStatic: true, IsDeclaringTypeNoContainer: true }
            && ma.Target is IrTypeReference
        )
            return new TsIdentifier(memberName);
        return new TsPropertyAccess(loweredTarget, memberName);
    }

    private static TsExpression MapCall(
        IrCallExpression call,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        // TS doesn't support named arguments at the call site — the receiver's
        // parameter list defines positional order. At this layer we drop the
        // argument names and lower each value; reordering / default-filling
        // for call sites using `Name: value` is handled earlier (at the
        // dispatcher or object-literal pass).
        var loweredArgs = call.Arguments.Select(a => MapArgument(a, bclRegistry)).ToList();

        // `[PlainObject]` instance method call: rewrite `obj.Method(args)` to
        // `methodName(obj, args)` — the plain-object shape has no class
        // prototype, so the method is emitted as a standalone helper that
        // takes the receiver as its first parameter.
        if (
            call.Origin is { IsPlainObjectInstanceMethod: true } pooMeta
            && call.Target is IrMemberAccess pooAccess
        )
        {
            var receiver = Map(pooAccess.Target, bclRegistry);
            var helperName = pooMeta.EmittedName is { } ov
                ? ov
                : TypeScriptNaming.ToCamelCase(pooMeta.MemberName);
            var helperArgs = new List<TsExpression> { receiver };
            helperArgs.AddRange(loweredArgs);
            return new TsCallExpression(new TsIdentifier(helperName), helperArgs);
        }

        if (bclRegistry is not null && call.Origin is not null)
        {
            // Lower the receiver separately so the BCL mapper sees it as the raw
            // value (no .methodName suffix applied yet). For unqualified calls the
            // target is an IrIdentifier — pass no receiver.
            TsExpression? loweredReceiver = call.Target is IrMemberAccess memberTarget
                ? Map(memberTarget.Target, bclRegistry)
                : null;
            var typeArgNames = call
                .TypeArguments?.Select(TsTypeArgName)
                .Where(n => n is not null)
                .Cast<string>()
                .ToList();
            var mapped = IrToTsBclMapper.TryMapCall(
                call,
                loweredReceiver,
                loweredArgs,
                typeArgNames ?? [],
                bclRegistry
            );
            if (mapped is not null)
                return mapped;
        }

        return new TsCallExpression(Map(call.Target, bclRegistry), loweredArgs, call.IsOptional);
    }

    private static string? TsTypeArgName(IrTypeRef type) =>
        IrToTsTypeMapper.Map(type) is TsNamedType n ? n.Name : null;

    public static TsExpression MapArgument(IrArgument arg, DeclarativeMappingRegistry? bclRegistry)
    {
        var value = Map(arg.Value, bclRegistry);
        return arg.IsSpread ? new TsSpreadExpression(value) : value;
    }

    /// <summary>
    /// `value?.member` becomes the TS optional chaining `value?.member` —
    /// the AST doesn't have a dedicated node, so we suffix the receiver
    /// text with `?` and let the printer emit it verbatim. Mirrors the
    /// legacy <see cref="Metano.Compiler.TypeScript.Transformation.OptionalChainingHandler"/>
    /// shape so call-sites stay byte-for-byte identical.
    /// </summary>
    private static TsExpression MapOptionalChain(
        IrOptionalChain chain,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var receiver = Map(chain.Target, bclRegistry);
        var receiverText = OptionalChainReceiverText(receiver);
        return new TsPropertyAccess(
            new TsIdentifier(receiverText + "?"),
            TypeScriptNaming.ToCamelCaseMember(chain.MemberName)
        );
    }

    private static string OptionalChainReceiverText(TsExpression expr) =>
        expr switch
        {
            TsIdentifier id => id.Name,
            TsPropertyAccess pa => OptionalChainReceiverText(pa.Object) + "." + pa.Property,
            _ => "unknown",
        };

    private static TsType? LowerLambdaParameterType(IrParameter p)
    {
        // [External] ambient shapes have no emitted declaration in the
        // consumer's tree, so the lambda parameter type annotation is
        // omitted and TypeScript infers it from the surrounding signature
        // (the real .d.ts of the npm package). [Ignore] should be barred
        // by MS0013 before reaching emission, but the same drop is kept
        // so a suppressed-diagnostic run still produces compilable TS.
        if (
            p.Type is IrNamedTypeRef named
            && named.Semantics is { } sem
            && (sem.IsIgnored || sem.IsExternal)
        )
            return null;
        return IrToTsTypeMapper.Map(p.Type);
    }

    /// <summary>
    /// Lowers an <see cref="IrLinqChain"/> to the pipe-form runtime call:
    /// <c>linq(source, op1(...), op2(...), opN(...))</c>. Each stage maps
    /// to a single function call against the corresponding pipe operator.
    /// The wrapping <c>linq</c> + each operator name are registered as
    /// runtime imports from <c>metano-runtime/system/linq</c> so the
    /// import collector picks them up alongside the existing helper
    /// imports (Enumerable, HashSet, …).
    /// </summary>
    private static TsExpression MapLinqChain(
        IrLinqChain chain,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var args = new List<TsExpression> { Map(chain.Source, bclRegistry) };
        var importedHelpers = new HashSet<string>(StringComparer.Ordinal) { "linq" };

        foreach (var stage in chain.Stages)
        {
            var emittedName = IrLinqMapping.EmittedName(stage.Operator);
            importedHelpers.Add(emittedName);
            var stageArgs = stage.Arguments.Select(a => Map(a, bclRegistry)).ToList();
            if (stage.Queryable is { } queryable)
                stageArgs.Add(MapQueryableMeta(queryable, bclRegistry));
            args.Add(new TsCallExpression(new TsIdentifier(emittedName), stageArgs));
        }

        return new TsLinqCall(args, importedHelpers);
    }

    /// <summary>
    /// Lowers an <see cref="IrQueryableMeta"/> into the runtime
    /// <c>QueryableMeta</c> object literal — <c>{ tree, captures? }</c>.
    /// The <c>tree</c> recursively materializes the
    /// <see cref="IrExprTreeNode"/> shape; <c>captures</c> emits a
    /// dictionary keyed by capture name whose values are the bridged
    /// IR expressions (e.g. closure locals, parameters).
    /// </summary>
    private static TsExpression MapQueryableMeta(
        IrQueryableMeta meta,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var props = new List<TsObjectProperty> { new("tree", MapExprTree(meta.Tree)) };
        if (meta.Captures is { Count: > 0 } captures)
        {
            var captureProps = captures
                .Select(c => new TsObjectProperty(c.Name, Map(c.Value, bclRegistry)))
                .ToList();
            props.Add(new TsObjectProperty("captures", new TsObjectLiteral(captureProps)));
        }
        return new TsObjectLiteral(props);
    }

    private static TsExpression MapExprTree(IrExprTreeNode node) =>
        node switch
        {
            IrExprParam p => TreeNode(
                "param",
                ("name", new TsStringLiteral(p.Name)),
                ("type", MapOptionalType(p.Type))
            ),
            IrExprCapture c => TreeNode(
                "capture",
                ("name", new TsStringLiteral(c.Name)),
                ("type", MapOptionalType(c.Type))
            ),
            IrExprLiteral lit => TreeNode(
                "literal",
                ("value", MapLiteralValue(lit.Value)),
                ("type", MapOptionalType(lit.Type))
            ),
            // Member access on the lowered TS object lines up with the
            // member-casing rule (no reserved-word escape) used elsewhere
            // in the bridge. Casing is routed through
            // IrExprTreeNamingPolicy so future targets (Dart) reuse the
            // same hook with their own TargetLanguage.
            IrExprMember m => TreeNode(
                "member",
                ("target", MapExprTree(m.Target)),
                (
                    "member",
                    new TsStringLiteral(
                        IrExprTreeNamingPolicy.MemberCasing(m.Member, TargetLanguage.TypeScript)
                    )
                )
            ),
            // Method names follow the same member-casing rule so the
            // tree's `method` matches the actual TS property emitted on
            // the lowered runtime surface; ToCamelCase would escape JS
            // reserved words (e.g. Delete -> delete_) which would
            // diverge from the call site.
            IrExprCall call => TreeNode(
                "call",
                ("target", call.Target is null ? new TsLiteral("null") : MapExprTree(call.Target)),
                (
                    "method",
                    new TsStringLiteral(
                        IrExprTreeNamingPolicy.MemberCasing(call.Method, TargetLanguage.TypeScript)
                    )
                ),
                ("args", new TsArrayLiteral(call.Args.Select(MapExprTree).ToList()))
            ),
            IrExprBinary bin => TreeNode(
                "binary",
                ("op", new TsStringLiteral(bin.Op)),
                ("left", MapExprTree(bin.Left)),
                ("right", MapExprTree(bin.Right))
            ),
            IrExprUnary un => TreeNode(
                "unary",
                ("op", new TsStringLiteral(un.Op)),
                ("operand", MapExprTree(un.Operand))
            ),
            IrExprConditional cond => TreeNode(
                "conditional",
                ("condition", MapExprTree(cond.Condition)),
                ("whenTrue", MapExprTree(cond.WhenTrue)),
                ("whenFalse", MapExprTree(cond.WhenFalse))
            ),
            IrExprNew newExpr => MapExprNew(newExpr),
            IrExprLambda lambda => TreeNode(
                "lambda",
                ("params", new TsArrayLiteral(lambda.Params.Select(MapLambdaParam).ToList())),
                ("body", MapExprTree(lambda.Body))
            ),
            // Hoisted common-subexpression wrapper produced by
            // IrExprTreeHoister (#209). The runtime visitor / evaluator
            // resolves the bindings then walks the body.
            IrExprLet let => TreeNode(
                "let",
                (
                    "bindings",
                    new TsArrayLiteral(
                        let.Bindings.Select(b =>
                                (TsExpression)
                                    new TsObjectLiteral(
                                        new List<TsObjectProperty>
                                        {
                                            new("name", new TsStringLiteral(b.Name)),
                                            new("value", MapExprTree(b.Value)),
                                        }
                                    )
                            )
                            .ToList()
                    )
                ),
                ("body", MapExprTree(let.Body))
            ),
            IrExprRef refNode => TreeNode("ref", ("name", new TsStringLiteral(refNode.Name))),
            _ => throw new InvalidOperationException(
                $"Unsupported IrExprTreeNode shape: {node.GetType().Name}"
            ),
        };

    /// <summary>
    /// Lowers an <see cref="IrExprNew"/> to the runtime <c>ExprNew</c>
    /// shape. Initializer member names follow the same camelCase rule
    /// as <see cref="IrExprMember"/> so the emitted <c>name</c> matches
    /// the lowered TS property the provider would dispatch on at
    /// runtime.
    /// </summary>
    private static TsExpression MapExprNew(IrExprNew newExpr)
    {
        var entries = new List<(string, TsExpression?)>
        {
            ("type", MapOptionalType(newExpr.Type)),
            ("args", new TsArrayLiteral(newExpr.Args.Select(MapExprTree).ToList())),
        };
        if (newExpr.Initializers is { Count: > 0 } initializers)
        {
            var initializerLiterals = initializers
                .Select(init =>
                    (TsExpression)
                        new TsObjectLiteral(
                            new List<TsObjectProperty>
                            {
                                new(
                                    "name",
                                    new TsStringLiteral(
                                        TypeScriptNaming.ToCamelCaseMember(init.Member)
                                    )
                                ),
                                new("value", MapExprTree(init.Value)),
                            }
                        )
                )
                .ToList();
            entries.Add(("initializers", new TsArrayLiteral(initializerLiterals)));
        }
        return TreeNode("new", entries.ToArray());
    }

    /// <summary>
    /// Lowers a nested-lambda parameter to an
    /// <c>ExprParam</c>-shaped literal — <c>{ kind: "param", name,
    /// type? }</c>. The runtime <c>ExprLambda.params</c> array is typed
    /// as <c>readonly ExprParam[]</c>; emitting the full discriminator
    /// keeps assignment compatibility with the union and lets visitors
    /// dispatch consistently with top-level params. Parameter names are
    /// emitted verbatim — the evaluator matches them against
    /// <see cref="IrExprParam"/> nodes by string identity, so any
    /// rename would have to flow through both sides in lockstep.
    /// </summary>
    private static TsExpression MapLambdaParam(IrExprLambdaParam param) =>
        TreeNode(
            "param",
            ("name", new TsStringLiteral(param.Name)),
            ("type", MapOptionalType(param.Type))
        );

    /// <summary>
    /// Builds an object-literal entry for <paramref name="type"/> when
    /// non-null. Returns <c>null</c> for skipped fields so
    /// <see cref="TreeNode"/>'s <c>(key, null)</c> filter keeps the
    /// emitted JS object minimal.
    /// </summary>
    /// <remarks>
    /// Emitted shape: <c>{ name: string; from?: string }</c>.
    /// <c>name</c> is the simple identifier the provider sees at
    /// runtime; <c>from</c> carries the cross-assembly package id
    /// (from <c>[EmitPackage]</c>) so providers can disambiguate
    /// same-named types defined in different packages. Local types
    /// and primitives omit <c>from</c>.
    /// </remarks>
    private static TsExpression? MapOptionalType(IrTypeRef? type)
    {
        if (type is null)
            return null;

        var typeRef = ResolveStructuredType(type);
        var props = new List<TsObjectProperty> { new("name", new TsStringLiteral(typeRef.Name)) };
        if (typeRef.PackageId is { Length: > 0 } pkg)
            props.Add(new TsObjectProperty("from", new TsStringLiteral(pkg)));
        return new TsObjectLiteral(props);
    }

    /// <summary>
    /// Resolved pair the queryable expression-tree emitter writes for
    /// a single param / capture / literal node. <see cref="Name"/> is
    /// the identifier the provider dispatches on;
    /// <see cref="PackageId"/> is the <c>[EmitPackage]</c> id when the
    /// type comes from another assembly (and <c>null</c> otherwise,
    /// for local types and primitives).
    /// </summary>
    private readonly record struct StructuredTypeRef(string Name, string? PackageId);

    /// <summary>
    /// Splits an <see cref="IrTypeRef"/> into the structured pair the
    /// queryable expression-tree emitter writes. Named types contribute
    /// their bare identifier plus <see cref="IrTypeOrigin.PackageId"/>
    /// when present; primitives and everything else fall back to the
    /// rendered TS surface name with no origin.
    /// <see cref="IrNullableTypeRef"/> wrappers are unwrapped so a
    /// nullable capture/literal still carries the inner type's origin —
    /// nullability is not part of the expression-tree contract today.
    /// </summary>
    private static StructuredTypeRef ResolveStructuredType(IrTypeRef type) =>
        type switch
        {
            IrNullableTypeRef nullable => ResolveStructuredType(nullable.Inner),
            IrNamedTypeRef named => new(named.Name, named.Origin?.PackageId),
            _ => new(Printer.RenderType(IrToTsTypeMapper.Map(type)), null),
        };

    private static TsObjectLiteral TreeNode(
        string kind,
        params (string Key, TsExpression? Value)[] fields
    )
    {
        var props = new List<TsObjectProperty>(fields.Length + 1)
        {
            new("kind", new TsStringLiteral(kind)),
        };
        foreach (var (key, value) in fields)
        {
            if (value is not null)
                props.Add(new TsObjectProperty(key, value));
        }
        return new TsObjectLiteral(props);
    }

    private static TsExpression MapLiteralValue(object? value) =>
        value switch
        {
            null => new TsLiteral("null"),
            bool b => new TsLiteral(b ? "true" : "false"),
            string s => new TsStringLiteral(s),
            char c => new TsStringLiteral(c.ToString()),
            Enum e => new TsStringLiteral(e.ToString()),
            IFormattable f => new TsLiteral(f.ToString(null, CultureInfo.InvariantCulture)),
            _ => new TsLiteral(value.ToString() ?? "null"),
        };

    private static TsExpression MapLambda(
        IrLambdaExpression lambda,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        // Lambda params drop the annotation when the inferred type is
        // `[Ignore]` (ambient types from declaration files have no TS
        // identifier to emit) — matching the legacy `LambdaHandler`
        // behavior. Otherwise the full inferred type is emitted so the
        // generated TS keeps call-site type information.
        var parameters = lambda
            .Parameters.Select(p => new TsParameter(
                TypeScriptNaming.ToCamelCase(p.Name),
                LowerLambdaParameterType(p)
            ))
            .ToList();
        var body = IrToTsStatementBridge.MapBody(lambda.Body, bclRegistry);
        // Named function expression: emitted when the lambda is the
        // lowered body of a directly-recursive [Inline] method (#194).
        // The internal name lets the body call itself without relying
        // on the erased declaration's identifier.
        if (lambda.Name is { Length: > 0 } fnName)
        {
            var returnType = lambda.ReturnType is null
                ? null
                : IrToTsTypeMapper.Map(lambda.ReturnType);
            return new TsNamedFunctionExpression(
                fnName,
                parameters,
                returnType,
                body,
                Async: lambda.IsAsync
            );
        }
        var arrow = new TsArrowFunction(parameters, body, Async: lambda.IsAsync);
        // Lambdas bound to a `[This]`-bearing delegate: wrap the arrow
        // in a runtime `bindReceiver(...)` call. The helper's
        // `function`-keyword trampoline picks up the runtime `this`
        // from the caller and forwards it as the first positional
        // argument to the arrow — the arrow itself stays
        // lexically-scoped so any `this` captured from the enclosing
        // C# class still resolves through ordinary closure
        // semantics. Zero body rewriting needed; the arrow's
        // receiver parameter (e.g. `self`) carries the runtime
        // `this`.
        if (lambda.UsesThis)
            return new TsCallExpression(new TsIdentifier("bindReceiver"), [arrow]);
        return arrow;
    }

    /// <summary>
    /// Lowers <c>expr is pattern</c> to a boolean TS expression. Binding patterns
    /// (type-with-designator, var) emit a TODO comment because expression-scoped
    /// variable introductions need a higher-level rewrite than a single
    /// expression can carry in TS. The common non-binding shapes — constant
    /// comparisons and bare type tests — lower cleanly.
    /// </summary>
    private static TsExpression MapIsPattern(
        IrIsPatternExpression node,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var lowered = Map(node.Expression, bclRegistry);
        switch (node.Pattern)
        {
            case IrConstantPattern constant:
                return new TsBinaryExpression(
                    lowered,
                    BinaryOperatorToken(IrBinaryOp.Equal, node.Expression, constant.Value),
                    Map(constant.Value, bclRegistry)
                );
            case IrTypePattern typePat when typePat.DesignatorName is null:
                return BuildTypeTest(lowered, typePat.Type);
            case IrDiscardPattern:
                return new TsLiteral("true");
            case IrPropertyPattern prop when prop.DesignatorName is null:
                return BuildPropertyPatternTest(lowered, prop, bclRegistry);
            case IrRelationalPattern:
            case IrLogicalPattern:
            case IrListPattern:
                return BuildPatternTest(lowered, node.Pattern, bclRegistry);
            case IrPositionalPattern pos when pos.DesignatorName is null:
                return BuildPositionalPatternTest(lowered, pos, bclRegistry);
            default:
                return new TsIdentifier(
                    $"/* TODO: unsupported is-pattern {node.Pattern.GetType().Name} */"
                );
        }
    }

    /// <summary>
    /// Lowers a C# <c>with</c> expression. Two shapes are possible:
    /// <list type="bullet">
    ///   <item>Regular records → <c>source.with({ x: expr, y: expr2 })</c>,
    ///   calling the synthesized <c>with</c> method on the source.</item>
    ///   <item><c>[PlainObject]</c> records → <c>{ ...source, x: expr }</c>,
    ///   since plain-object shapes have no class prototype to host a
    ///   <c>with</c> method.</item>
    /// </list>
    /// The <c>[PlainObject]</c> discriminator comes from the source's
    /// semantic kind — no runtime resolution needed.
    /// </summary>
    private static TsExpression MapWithExpression(
        IrWithExpression w,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var source = Map(w.Source, bclRegistry);
        var properties = w
            .Assignments.Select(a => new TsObjectProperty(
                TypeScriptNaming.ToCamelCase(a.MemberName),
                Map(a.Value, bclRegistry)
            ))
            .ToList();

        if (w.IsPlainObjectSource)
        {
            var spread = new List<TsObjectProperty>(properties.Count + 1)
            {
                new("", new TsSpreadExpression(source)),
            };
            spread.AddRange(properties);
            return new TsObjectLiteral(spread);
        }

        return new TsCallExpression(
            new TsPropertyAccess(source, "with"),
            [new TsObjectLiteral(properties)]
        );
    }

    private static TsExpression BuildTypeTest(TsExpression value, IrTypeRef targetType) =>
        IrToTsTypeMapper.Map(targetType) is TsNamedType named
            ? new TsBinaryExpression(value, "instanceof", new TsIdentifier(named.Name))
            : new TsIdentifier("/* TODO: non-named type pattern */");

    /// <summary>
    /// Lowers <see cref="IrLetExpression"/> to an IIFE: the binding becomes
    /// the arrow's lone parameter and the value supplies the single
    /// argument. The arrow's body is a one-statement <c>return</c> of the
    /// body expression so the let-binding stays a pure expression at the
    /// call site (compound-assignment, increment, null-conditional writes
    /// in expression position all need an expression, not a statement).
    /// </summary>
    private static TsExpression MapLetExpression(
        IrLetExpression let,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var arrow = new TsArrowFunction(
            [new TsParameter(let.Name, null)],
            [new TsReturnStatement(Map(let.Body, bclRegistry))]
        );
        return new TsCallExpression(new TsParenthesized(arrow), [Map(let.Value, bclRegistry)]);
    }

    /// <summary>
    /// Lowers a C# <c>switch</c> expression. When the arm chain ends with a
    /// bare discard (<c>_ =&gt; value</c>, no when-clause) and every earlier
    /// arm has a pattern that can lower to a plain boolean test, we emit a
    /// nested ternary chain — matching the legacy output byte-for-byte.
    /// Non-exhaustive matches and patterns that need bindings fall back to
    /// a self-invoking arrow function with an <c>if</c>-chain and a
    /// trailing throw, mirroring C#'s non-exhaustive runtime failure.
    /// </summary>
    private static TsExpression MapSwitchExpression(
        IrSwitchExpression sw,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (TryLowerSwitchAsTernary(sw, bclRegistry) is { } ternary)
            return ternary;
        return LowerSwitchAsIife(sw, bclRegistry);
    }

    private static TsExpression? TryLowerSwitchAsTernary(
        IrSwitchExpression sw,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (sw.Arms.Count == 0)
            return null;
        var last = sw.Arms[^1];
        if (last.Pattern is not IrDiscardPattern || last.WhenClause is not null)
            return null;
        for (var i = 0; i < sw.Arms.Count - 1; i++)
        {
            if (!CanLowerPatternAsBooleanTest(sw.Arms[i].Pattern))
                return null;
        }

        var scrutinee = Map(sw.Scrutinee, bclRegistry);
        TsExpression result = Map(last.Result, bclRegistry);
        for (var i = sw.Arms.Count - 2; i >= 0; i--)
        {
            var arm = sw.Arms[i];
            var test = BuildPatternTest(scrutinee, arm.Pattern, bclRegistry);
            if (arm.WhenClause is not null)
                test = new TsBinaryExpression(test, "&&", Map(arm.WhenClause, bclRegistry));
            result = new TsConditionalExpression(test, Map(arm.Result, bclRegistry), result);
        }
        return result;
    }

    private static bool CanLowerPatternAsBooleanTest(IrPattern pattern) =>
        pattern switch
        {
            IrConstantPattern or IrDiscardPattern or IrRelationalPattern => true,
            IrTypePattern t => t.DesignatorName is null,
            IrPropertyPattern p => p.DesignatorName is null
                && p.Subpatterns.All(s => CanLowerPatternAsBooleanTest(s.Pattern)),
            IrLogicalPattern { Operator: IrLogicalOp.Not } n => CanLowerPatternAsBooleanTest(
                n.Left
            ),
            IrLogicalPattern l => CanLowerPatternAsBooleanTest(l.Left)
                && l.Right is not null
                && CanLowerPatternAsBooleanTest(l.Right),
            _ => false,
        };

    private static TsExpression LowerSwitchAsIife(
        IrSwitchExpression sw,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        const string scrutineeName = "$s";
        var body = new List<TsStatement>();
        foreach (var arm in sw.Arms)
        {
            var patternTest = BuildPatternTest(
                new TsIdentifier(scrutineeName),
                arm.Pattern,
                bclRegistry
            );
            var guard = arm.WhenClause is null
                ? patternTest
                : new TsBinaryExpression(patternTest, "&&", Map(arm.WhenClause, bclRegistry));
            body.Add(
                new TsIfStatement(guard, [new TsReturnStatement(Map(arm.Result, bclRegistry))])
            );
        }
        body.Add(
            new TsThrowStatement(
                new TsNewExpression(
                    new TsIdentifier("Error"),
                    [new TsStringLiteral("Non-exhaustive switch expression")]
                )
            )
        );

        var arrow = new TsArrowFunction([new TsParameter(scrutineeName, new TsAnyType())], body);
        return new TsCallExpression(new TsParenthesized(arrow), [Map(sw.Scrutinee, bclRegistry)]);
    }

    /// <summary>
    /// Lowers a pattern to a boolean test against <paramref name="value"/>.
    /// Mirrors the shape in <see cref="MapIsPattern"/> without the binding
    /// complication — switch-expression arms can't introduce bindings visible
    /// outside the arm today (property/var bindings are TODO).
    /// </summary>
    private static TsExpression BuildPatternTest(
        TsExpression value,
        IrPattern pattern,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        pattern switch
        {
            IrConstantPattern constant => new TsBinaryExpression(
                value,
                BinaryOperatorToken(IrBinaryOp.Equal, right: constant.Value),
                Map(constant.Value, bclRegistry)
            ),
            IrTypePattern typePat => BuildTypeTest(value, typePat.Type),
            IrDiscardPattern => new TsLiteral("true"),
            IrVarPattern => new TsLiteral("true"),
            IrPropertyPattern prop => BuildPropertyPatternTest(value, prop, bclRegistry),
            IrRelationalPattern rel => new TsBinaryExpression(
                value,
                RelationalOpToken(rel.Operator),
                Map(rel.Value, bclRegistry)
            ),
            IrLogicalPattern { Operator: IrLogicalOp.Not } notPat => new TsUnaryExpression(
                "!",
                new TsParenthesized(BuildPatternTest(value, notPat.Left, bclRegistry))
            ),
            IrLogicalPattern log => new TsBinaryExpression(
                BuildPatternTest(value, log.Left, bclRegistry),
                log.Operator is IrLogicalOp.And ? "&&" : "||",
                BuildPatternTest(value, log.Right!, bclRegistry)
            ),
            IrListPattern list => BuildListPatternTest(value, list, bclRegistry),
            IrPositionalPattern pos => BuildPositionalPatternTest(value, pos, bclRegistry),
            _ => new TsIdentifier($"/* TODO: unsupported pattern {pattern.GetType().Name} */"),
        };

    /// <summary>
    /// Lowers <c>[p0, p1, …]</c> to a length-gated conjunction of per-index
    /// tests. When the pattern carries a <c>..</c> slice we drop the exact
    /// length check (<c>&gt;=</c> instead of <c>===</c>) and reverse-index
    /// the tail sub-patterns with <c>arr.length - k</c>. Slice sub-pattern
    /// bindings (<c>.. var tail</c>) aren't modeled yet — the IR extractor
    /// drops them, so the tail is treated as `always match`.
    /// </summary>
    private static TsExpression BuildListPatternTest(
        TsExpression value,
        IrListPattern pattern,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var lengthAccess = new TsPropertyAccess(value, "length");
        TsExpression lengthCheck;
        if (pattern.SliceIndex is null)
            lengthCheck = new TsBinaryExpression(
                lengthAccess,
                "===",
                new TsLiteral(pattern.Elements.Count.ToString())
            );
        else
            lengthCheck = new TsBinaryExpression(
                lengthAccess,
                ">=",
                new TsLiteral(pattern.Elements.Count.ToString())
            );

        var result = lengthCheck;
        for (var i = 0; i < pattern.Elements.Count; i++)
        {
            TsExpression indexExpr;
            if (pattern.SliceIndex is not null && i >= pattern.SliceIndex)
            {
                // Tail element `k` positions past the slice — indexed from
                // the end of the array so `[a, .., b]` tests `arr[arr.length - 1]`.
                var tailOffset = pattern.Elements.Count - i;
                indexExpr = new TsElementAccess(
                    value,
                    new TsBinaryExpression(lengthAccess, "-", new TsLiteral(tailOffset.ToString()))
                );
            }
            else
            {
                indexExpr = new TsElementAccess(value, new TsLiteral(i.ToString()));
            }
            var subTest = BuildPatternTest(indexExpr, pattern.Elements[i], bclRegistry);
            result = new TsBinaryExpression(result, "&&", subTest);
        }
        return result;
    }

    /// <summary>
    /// Lowers <c>(p0, p1, …)</c> / <c>Point(p0, p1)</c>. For the tuple /
    /// deconstruction shape we don't yet have a Deconstruct call modeled in
    /// the IR, so we approximate with indexed access on the value
    /// (<c>value[0]</c>, <c>value[1]</c>, …) — which lines up with how TS
    /// tuples are represented in this pipeline. A type filter layers an
    /// <c>instanceof</c> guard in front.
    /// </summary>
    private static TsExpression BuildPositionalPatternTest(
        TsExpression value,
        IrPositionalPattern pattern,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        TsExpression? result = pattern.Type is not null ? BuildTypeTest(value, pattern.Type) : null;
        for (var i = 0; i < pattern.Elements.Count; i++)
        {
            var indexExpr = new TsElementAccess(value, new TsLiteral(i.ToString()));
            var subTest = BuildPatternTest(indexExpr, pattern.Elements[i], bclRegistry);
            result = result is null ? subTest : new TsBinaryExpression(result, "&&", subTest);
        }
        return result ?? new TsLiteral("true");
    }

    private static string RelationalOpToken(IrRelationalOp op) =>
        op switch
        {
            IrRelationalOp.LessThan => "<",
            IrRelationalOp.LessThanOrEqual => "<=",
            IrRelationalOp.GreaterThan => ">",
            IrRelationalOp.GreaterThanOrEqual => ">=",
            _ => "<",
        };

    /// <summary>
    /// Lowers a property pattern to a conjunction of sub-tests:
    /// <list type="bullet">
    ///   <item>When the pattern has a <see cref="IrPropertyPattern.Type"/> filter,
    ///   start with <c>value instanceof T</c>.</item>
    ///   <item>For each <c>{ MemberName: pattern }</c> entry, emit the inner
    ///   pattern test against <c>value.memberName</c> (camelCased to match the
    ///   TS backend's naming policy).</item>
    /// </list>
    /// An empty property-pattern with no type filter degenerates to <c>true</c>,
    /// matching C#'s semantics for <c>is { }</c>.
    /// </summary>
    private static TsExpression BuildPropertyPatternTest(
        TsExpression value,
        IrPropertyPattern pattern,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        TsExpression? result = pattern.Type is not null ? BuildTypeTest(value, pattern.Type) : null;
        foreach (var sub in pattern.Subpatterns)
        {
            var memberAccess = new TsPropertyAccess(
                value,
                TypeScriptNaming.ToCamelCaseMember(sub.MemberName)
            );
            var subTest = BuildPatternTest(memberAccess, sub.Pattern, bclRegistry);
            result = result is null ? subTest : new TsBinaryExpression(result, "&&", subTest);
        }
        return result ?? new TsLiteral("true");
    }

    private static TsExpression MapStringInterpolation(
        IrStringInterpolation interp,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        // Template literals alternate quasis (string text) with expressions; TS allows
        // an empty leading/trailing quasi so the invariant Quasis.Count == Expressions.Count + 1
        // holds even when the interpolation starts or ends with an expression.
        var quasis = new List<string>();
        var expressions = new List<TsExpression>();
        var pendingText = new System.Text.StringBuilder();
        foreach (var part in interp.Parts)
        {
            switch (part)
            {
                case IrInterpolationText text:
                    pendingText.Append(text.Text);
                    break;
                case IrInterpolationExpression expr:
                    quasis.Add(pendingText.ToString());
                    pendingText.Clear();
                    // Match the legacy ExpressionTransformer: defensively wrap
                    // conditional expressions inside an interpolation hole so
                    // the precedence stays obvious (`${(a ? b : c)}`) — the TS
                    // printer doesn't add grouping of its own.
                    var lowered = Map(expr.Expression, bclRegistry);
                    if (expr.Expression is IrConditionalExpression)
                        lowered = new TsParenthesized(lowered);
                    expressions.Add(lowered);
                    break;
            }
        }
        quasis.Add(pendingText.ToString());
        return new TsTemplateLiteral(quasis, expressions);
    }

    private static TsExpression MapNewTarget(IrTypeRef type) =>
        type switch
        {
            // The runtime class for Decimal is the decimal.js `Decimal`
            // constructor — the type mapper flattens it to `number` for
            // signature purposes, but `new` must reach the real class.
            IrPrimitiveTypeRef { Primitive: IrPrimitive.Decimal } => new TsIdentifier("Decimal"),
            // Dictionary / IDictionary / HashSet / ISet / List / IList lower
            // to their JS runtime equivalents; the type mapper collapses them
            // into IrMap/IrSet/IrArray refs, so we reconstruct the JS class
            // names here for `new` targets.
            IrMapTypeRef => new TsIdentifier("Map"),
            IrSetTypeRef => new TsIdentifier("HashSet"),
            _ => IrToTsTypeMapper.Map(type) switch
            {
                TsNamedType n => new TsIdentifier(n.Name),
                _ => new TsIdentifier("Object"),
            },
        };

    /// <summary>
    /// Lowers <c>new T(a, b, …)</c>. For <c>[PlainObject]</c> targets the
    /// legacy handler emits <c>{ p1: a, p2: b, … }</c> keyed by the ctor's
    /// parameter names (camelCased), not a real <c>new T(…)</c> call — the
    /// whole point of the attribute is that the class wrapper is erased and
    /// consumers interact with plain JS objects. The extractor already
    /// populated <see cref="IrNewExpression.IsPlainObject"/> and
    /// <see cref="IrNewExpression.ParameterNames"/> for this case.
    /// </summary>
    private static TsExpression MapNewExpression(
        IrNewExpression ne,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var args = ne.Arguments.Select(a => MapArgument(a, bclRegistry)).ToList();
        if (ne.IsObjectArgsCtor && ne.Type is IrNamedTypeRef objectArgsTarget)
        {
            IReadOnlyList<TsType>? typeArguments = objectArgsTarget.TypeArguments
                is { Count: > 0 } tArgs
                ? tArgs.Select(t => IrToTsTypeMapper.Map(t)).ToList()
                : null;
            return new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier(objectArgsTarget.Name), "create"),
                args,
                TypeArguments: typeArguments
            );
        }
        if (ne.IsPlainObject)
        {
            var properties = new List<TsObjectProperty>(args.Count);
            for (var i = 0; i < args.Count; i++)
            {
                var paramName =
                    ne.ParameterNames is { } names && i < names.Count ? names[i] : $"_{i}";
                properties.Add(
                    new TsObjectProperty(TypeScriptNaming.ToCamelCase(paramName), args[i])
                );
            }
            return new TsObjectLiteral(properties);
        }
        if (ne.Type is IrNamedTypeRef named && named.Semantics is { } s)
        {
            // BCL exception types (InvalidOperationException, …) have no
            // runtime class on the TS side — map them to the builtin `Error`.
            // Transpilable exception subclasses keep their generated identity.
            if (s.Kind is IrNamedTypeKind.Exception && !s.IsTranspilable)
                return new TsNewExpression(new TsIdentifier("Error"), args);

            // [Branded] structs compile to companion objects with a
            // `create` factory, not a runtime class. `new UserId(v)` therefore
            // lowers to `UserId.create(v)` on the TS side.
            if (s.Kind is IrNamedTypeKind.Branded)
                return new TsCallExpression(
                    new TsPropertyAccess(new TsIdentifier(named.Name), "create"),
                    args
                );
        }

        return new TsNewExpression(MapNewTarget(ne.Type), args);
    }

    private static TsExpression MapLiteral(IrLiteral lit) =>
        lit.Kind switch
        {
            IrLiteralKind.Null => new TsLiteral("null"),
            IrLiteralKind.Boolean => new TsLiteral((bool)lit.Value! ? "true" : "false"),
            IrLiteralKind.String => new TsStringLiteral((string)lit.Value!),
            IrLiteralKind.Char => new TsStringLiteral(lit.Value?.ToString() ?? ""),
            IrLiteralKind.Default => new TsLiteral("undefined"),
            // Decimal literals (1.5m / implicit-to-decimal conversions) must
            // wrap as decimal.js Decimal instances so downstream arithmetic
            // (already method-call shaped by the extractor) has the correct
            // receiver type. The extractor passes through the raw source
            // token text so precision is preserved.
            IrLiteralKind.Decimal => new TsNewExpression(
                new TsIdentifier("Decimal"),
                [new TsStringLiteral(FormatNumericValue(lit.Value))]
            ),
            // BigInteger literals use the native bigint suffix `n`.
            IrLiteralKind.BigInteger => new TsLiteral(FormatNumericValue(lit.Value) + "n"),
            _ => new TsLiteral(FormatNumericValue(lit.Value)),
        };

    private static string FormatNumericValue(object? value) =>
        value is IFormattable f
            ? f.ToString(null, CultureInfo.InvariantCulture)
            : value?.ToString() ?? "0";

    /// <summary>
    /// True when either side of a binary comparison is the IR null
    /// literal. Pairs with the loose <c>==</c> / <c>!=</c> emission path
    /// so <c>x == null</c> matches both <c>null</c> and <c>undefined</c>
    /// on the JS side — necessary for TS consumers that may produce
    /// <c>undefined</c> (absent optional property, uninitialized field)
    /// where the C# contract only knows about <c>null</c>.
    /// </summary>
    private static bool IsNullCompare(IrExpression? left, IrExpression? right) =>
        left is IrLiteral { Kind: IrLiteralKind.Null }
        || right is IrLiteral { Kind: IrLiteralKind.Null };

    /// <summary>
    /// Picks the JS operator token for an IR binary operator. Equal /
    /// NotEqual normally lower to strict <c>===</c> / <c>!==</c>, but
    /// when either operand is a null literal the bridge emits the loose
    /// <c>==</c> / <c>!=</c> form so that a TS consumer reading the
    /// compared expression cannot be tripped by an <c>undefined</c> that
    /// came across the boundary (Kotlin/JS uses the same strategy). C#
    /// does not expose <c>undefined</c>, so this relaxation is safe for
    /// round-trip semantics — only the JS-visible check broadens.
    /// </summary>
    private static string BinaryOperatorToken(
        IrBinaryOp op,
        IrExpression? left = null,
        IrExpression? right = null
    ) =>
        op switch
        {
            IrBinaryOp.Add => "+",
            IrBinaryOp.Subtract => "-",
            IrBinaryOp.Multiply => "*",
            IrBinaryOp.Divide => "/",
            IrBinaryOp.Modulo => "%",
            IrBinaryOp.Equal => IsNullCompare(left, right) ? "==" : "===",
            IrBinaryOp.NotEqual => IsNullCompare(left, right) ? "!=" : "!==",
            IrBinaryOp.LessThan => "<",
            IrBinaryOp.LessThanOrEqual => "<=",
            IrBinaryOp.GreaterThan => ">",
            IrBinaryOp.GreaterThanOrEqual => ">=",
            IrBinaryOp.LogicalAnd => "&&",
            IrBinaryOp.LogicalOr => "||",
            IrBinaryOp.BitwiseAnd => "&",
            IrBinaryOp.BitwiseOr => "|",
            IrBinaryOp.BitwiseXor => "^",
            IrBinaryOp.LeftShift => "<<",
            IrBinaryOp.RightShift => ">>",
            IrBinaryOp.UnsignedRightShift => ">>>",
            IrBinaryOp.NullCoalescing => "??",
            IrBinaryOp.Assign => "=",
            IrBinaryOp.AddAssign => "+=",
            IrBinaryOp.SubtractAssign => "-=",
            IrBinaryOp.MultiplyAssign => "*=",
            IrBinaryOp.DivideAssign => "/=",
            IrBinaryOp.ModuloAssign => "%=",
            IrBinaryOp.BitwiseAndAssign => "&=",
            IrBinaryOp.BitwiseOrAssign => "|=",
            IrBinaryOp.BitwiseXorAssign => "^=",
            IrBinaryOp.LeftShiftAssign => "<<=",
            IrBinaryOp.RightShiftAssign => ">>=",
            IrBinaryOp.UnsignedRightShiftAssign => ">>>=",
            IrBinaryOp.NullCoalescingAssign => "??=",
            _ => "?",
        };

    private static string PrefixOperatorToken(IrUnaryOp op) =>
        op switch
        {
            IrUnaryOp.Negate => "-",
            IrUnaryOp.LogicalNot => "!",
            IrUnaryOp.BitwiseNot => "~",
            IrUnaryOp.Increment => "++",
            IrUnaryOp.Decrement => "--",
            _ => "",
        };

    private static string PostfixOperatorToken(IrUnaryOp op) =>
        op switch
        {
            IrUnaryOp.Increment => "++",
            IrUnaryOp.Decrement => "--",
            _ => "",
        };

    private static string FormatInner(IrExpression expr) =>
        expr is IrIdentifier id ? id.Name : expr.GetType().Name;
}
