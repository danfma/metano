using System.Globalization;
using Metano.Annotations;
using Metano.Compiler.Analysis;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Parenthesization.Templates;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Converts target-agnostic <see cref="IrExpression"/> nodes into TypeScript AST
/// expressions. Handles the subset of IR the current extractors produce; unsupported
/// kinds fall through to a <see cref="TsIdentifier"/> carrying a <c>/* TODO */</c>
/// comment so the generated file remains syntactically valid.
/// </summary>
public static partial class IrToTsExpressionBridge
{
    /// <summary>The <c>metano-runtime</c> helper backing C# integer division.</summary>
    private static readonly string IntegerDivisionHelper =
        IrRuntimeRequirementToTsImport.ImportedNameOf("IntegerDivision");

    /// <summary>
    /// Maps an IR expression to TS. When <paramref name="bclRegistry"/> is provided,
    /// member accesses and calls consult <see cref="IrToTsBclMapper"/> first and
    /// fall back to the raw lowering when no entry matches. Without a registry the
    /// bridge emits raw <c>receiver.member</c> / <c>receiver.method(args)</c>.
    /// </summary>
    public static TsExpression Map(
        IrExpression expression,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        // JSX-producing expressions (a JSX-renderable `new`, a `Solid.For(...)`
        // helper, or a `Text(...)` builder) lower through the single JSX-producer
        // entry point regardless of position. This catches a bare
        // `Render() => Solid.For(items, …)` return that never passes through
        // LowerChild — without it the call would emit as a raw function call.
        if (IrToTsJsxBridge.TryLowerJsxProducer(expression, bclRegistry) is { } jsx)
            return jsx;

        return expression switch
        {
            IrLiteral lit => MapLiteral(lit),
            IrIdentifier id => new TsIdentifier(TypeScriptNaming.ToCamelCase(id.Name)),
            IrTypeReference tr => MapTypeReference(tr),
            IrThisExpression => new TsIdentifier("this"),
            IrBaseExpression => new TsIdentifier("super"),
            IrMemberAccess ma => MapMemberAccess(ma, bclRegistry),
            IrElementAccess ea => MapElementAccess(ea, bclRegistry),
            IrSliceExpression slice => MapSlice(slice, bclRegistry),
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
            IrCastExpression { TargetType: OutResultTypeRef } cast => MapOutResultAssertion(
                cast,
                bclRegistry
            ),
            IrCastExpression cast => MapCast(cast, bclRegistry),
            IrLambdaExpression lambda => MapLambda(lambda, bclRegistry),
            IrStringInterpolation interp => MapStringInterpolation(interp, bclRegistry),
            IrIsPatternExpression isPattern => MapIsPattern(isPattern, bclRegistry),
            IrSwitchExpression sw => MapSwitchExpression(sw, bclRegistry),
            IrWithExpression w => MapWithExpression(w, bclRegistry),
            IrArrayLiteral arr => new TsArrayLiteral(
                arr.Elements.Select(e => Map(e, bclRegistry)).ToList()
            ),
            IrSpreadExpression spread => new TsSpreadExpression(
                Map(spread.Expression, bclRegistry)
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
            IrObjectLiteral obj => new TsObjectLiteral(
                obj.Properties.Select(p => new TsObjectProperty(p.Name, Map(p.Value, bclRegistry)))
                    .ToList()
            ),
            IrThrowExpression th => new TsCallExpression(
                new TsArrowFunction([], [new TsThrowStatement(Map(th.Expression, bclRegistry))]),
                Array.Empty<TsExpression>()
            ),
            IrLinqChain chain => MapLinqChain(chain, bclRegistry),
            IrLetExpression let => MapLetExpression(let, bclRegistry),
            IrSequenceExpression sequence => MapSequence(sequence, bclRegistry),
            IrOutArgumentCall outCall => MapOutArgumentCall(outCall, bclRegistry),
            IrNullConditionalAccess access => MapNullConditionalAccess(access, bclRegistry),
            IrConditionalReceiver => CurrentConditionalReceiver(),
            IrUnsupportedExpression u => MapUnsupported(u),
            _ => new TsIdentifier($"/* TODO: {expression.GetType().Name} */"),
        };
    }

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

        if (bin.Operator is IrBinaryOp.IntegerDivide)
            return MapIntegerDivision(bin, bclRegistry);

        if (
            bin is { Operator: IrBinaryOp.Assign, Left: IrMemberAccess initOnlyMember }
            && IsInitOnlyInitializerWrite(initOnlyMember)
        )
            return MapInitOnlyInitializerWrite(initOnlyMember, bin.Right, bclRegistry);

        return new TsBinaryExpression(
            Map(bin.Left, bclRegistry),
            BinaryOperatorToken(bin.Operator, bin.Left, bin.Right),
            Map(bin.Right, bclRegistry)
        );
    }

    /// <summary>
    /// A write to an <c>init</c>-only property through a receiver other than
    /// <c>this</c> / <c>base</c>, which C# allows only in an object
    /// initializer (see <see cref="IrMemberOrigin.IsInitOnly"/>).
    /// </summary>
    private static bool IsInitOnlyInitializerWrite(IrMemberAccess member) =>
        member is { Origin.IsInitOnly: true, Target: not (IrThisExpression or IrBaseExpression) };

    /// <summary>
    /// <c>Object.assign(receiver, { member: value })</c>. An <c>init</c>-only
    /// property is declared <c>readonly</c>, which rejects a plain assignment
    /// outside the constructor; <c>Object.assign</c> performs the same
    /// <c>[[Set]]</c> (a setter still runs) and evaluates the receiver before
    /// the value, as the assignment would.
    /// </summary>
    private static TsExpression MapInitOnlyInitializerWrite(
        IrMemberAccess member,
        IrExpression value,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        new TsCallExpression(
            new TsPropertyAccess(new TsIdentifier("Object"), "assign"),
            [
                Map(member.Target, bclRegistry),
                new TsObjectLiteral([
                    new TsObjectProperty(AccessedMemberName(member), Map(value, bclRegistry)),
                ]),
            ]
        );

    /// <summary>
    /// JS has no integer division: <c>7 / 2</c> is <c>3.5</c>. The runtime
    /// <c>intDiv</c> helper truncates toward zero like C# and throws on a zero
    /// divisor, where a bare <c>Math.trunc(a / b)</c> would yield
    /// <c>Infinity</c> or <c>NaN</c>.
    /// </summary>
    private static TsExpression MapIntegerDivision(
        IrBinaryExpression bin,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        new TsTemplate(
            $"{IntegerDivisionHelper}($0, $1)",
            Receiver: null,
            [Map(bin.Left, bclRegistry), Map(bin.Right, bclRegistry)]
        )
        {
            RuntimeImports = [IntegerDivisionHelper],
        };

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

    /// <summary>
    /// <c>target.slice(start, end)</c> — the same half-open copy for JS arrays
    /// and strings. An absent start with a present end becomes <c>0</c>; an
    /// absent end is omitted so the slice runs to the end.
    /// </summary>
    private static TsExpression MapSlice(
        IrSliceExpression slice,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        new TsCallExpression(
            new TsPropertyAccess(Map(slice.Target, bclRegistry), "slice"),
            MapSliceBounds(slice, bclRegistry)
        );

    private static IReadOnlyList<TsExpression> MapSliceBounds(
        IrSliceExpression slice,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        (slice.Start, slice.End) switch
        {
            (null, null) => [],
            ({ } start, null) => [Map(start, bclRegistry)],
            (null, { } end) => [new TsLiteral("0"), Map(end, bclRegistry)],
            ({ } start, { } end) => [Map(start, bclRegistry), Map(end, bclRegistry)],
        };

    /// <summary>
    /// Placeholder for a construct the extractor already reported as MS0001:
    /// <c>(() =&gt; { throw new Error("…"); })()</c> parses in any expression
    /// position, type-checks as <c>never</c>, and fails loudly if reached.
    /// </summary>
    private static TsExpression MapUnsupported(IrUnsupportedExpression unsupported) =>
        new TsCallExpression(
            new TsArrowFunction(
                [],
                [
                    new TsThrowStatement(
                        new TsNewExpression(
                            new TsIdentifier("Error"),
                            [new TsStringLiteral($"Unsupported expression: {unsupported.Kind}")]
                        )
                    ),
                ]
            ),
            []
        );

    private static TsExpression MapMemberAccess(
        IrMemberAccess ma,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (IrToTsExceptionConstructionBridge.TryMapKeptBaseMessageRead(ma) is { } keptBaseMessage)
            return keptBaseMessage;

        var loweredTarget = Map(ma.Target, bclRegistry);

        // `[Branded]` value read: the companion lowers the wrapper to
        // `T = primitive & { __brand }`, so the receiver already IS the
        // wrapped value — `id.Value` → `id`, `id.Value.Length` → `id.length`
        // (ADR-0005).
        if (ma.Origin is { IsBrandedValueMember: true })
            return loweredTarget;

        // `[JsTuple]` positional member read: `value.First` / `value.Second`
        // lowers to array-index access `value[0]` / `value[1]`. The element
        // index is the member's positional declaration order, resolved by the
        // extractor against the record's primary constructor.
        if (ma.Origin is { IsJsTupleElement: true, TupleIndex: >= 0 } tupleOrigin)
            return new TsElementAccess(
                loweredTarget,
                // Numeric index literal — TsLiteral carries the raw token, and the
                // index is a non-negative int, so this prints as `value[0]`.
                new TsLiteral(tupleOrigin.TupleIndex.ToString())
            );

        if (bclRegistry is not null)
        {
            var mapped = IrToTsBclMapper.TryMapMemberAccess(ma, loweredTarget, bclRegistry);
            if (mapped is not null)
                return mapped;
        }

        var memberName = AccessedMemberName(ma);

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

    /// <summary>
    /// The TS name a member access reads, matching the name the member was
    /// declared with:
    /// <list type="bullet">
    ///   <item>string enum members keep the C# name — <c>[Name]</c> renames
    ///     the runtime string value, not the key;</item>
    ///   <item>numeric enum members keep the C# name unless <c>[Name]</c>
    ///     renames the key;</item>
    ///   <item>data members of an emitted type follow
    ///     <see cref="IrToTsNamingPolicy.ToDataMemberName"/>, so a reserved
    ///     word reads the escaped property it was declared as
    ///     (<c>r.Default</c> → <c>r.default_</c>);</item>
    ///   <item>any other <c>[Name]</c> override comes through verbatim;</item>
    ///   <item><c>[Branded]</c> methods are namespace functions, whose
    ///     declarations require reserved-word escapes;</item>
    ///   <item>everything else (methods, events, foreign members) is camelCased
    ///     without escaping — JS accepts reserved words after a dot.</item>
    /// </list>
    /// </summary>
    private static string AccessedMemberName(IrMemberAccess ma) =>
        ma.Origin switch
        {
            { IsStringEnumMember: true } => ma.MemberName,
            { IsEnumMember: true } origin => origin.EmittedName ?? ma.MemberName,
            { IsTranspiledDataMember: true } origin => IrToTsNamingPolicy.ToDataMemberName(
                ma.MemberName,
                origin.EmittedName
            ),
            { EmittedName: { } overridden } => overridden,
            { IsBrandedMember: true } => TypeScriptNaming.ToCamelCase(ma.MemberName),
            _ => TypeScriptNaming.ToCamelCaseMember(ma.MemberName),
        };

    /// <summary>
    /// The name a read of a data member uses: the declared, reserved-word
    /// escaped name (<see cref="IrToTsNamingPolicy.ToDataMemberName"/>) for a
    /// member the toolchain declares, otherwise the <c>[Name]</c> override or
    /// the unescaped camelCase name the foreign declaration carries.
    /// </summary>
    private static string ReadDataMemberName(
        string memberName,
        string? emittedName,
        bool isTranspiledDataMember
    ) =>
        isTranspiledDataMember
            ? IrToTsNamingPolicy.ToDataMemberName(memberName, emittedName)
            : emittedName ?? TypeScriptNaming.ToCamelCaseMember(memberName);

    /// <summary>
    /// Lowers a value-position type reference. A nested type arrives dotted
    /// (<c>Outer.Inner</c>) and lowers to a property-access chain rooted at the
    /// top-level container, so it reads through the companion namespace and the import
    /// collector sees the root identifier the file must import. Segments keep their
    /// source casing — they name types, not members.
    /// </summary>
    private static TsExpression MapTypeReference(IrTypeReference reference)
    {
        var segments = IrToTsTypeMapper.ResolveAliasedName(reference.Name).Split('.');
        var root = TypeValueReference(segments[0], IrToTsTypeMapper.TryResolveOrigin(reference));

        return segments
            .Skip(1)
            .Aggregate(root, (target, segment) => new TsPropertyAccess(target, segment));
    }

    /// <summary>
    /// The value-position reference to a type name. A cross-package type lowers to a
    /// <see cref="TsTypeReference"/> — printed exactly like the bare identifier — so
    /// the import collector sees the origin the file must import it from; every other
    /// type stays a plain <see cref="TsIdentifier"/>.
    /// </summary>
    private static TsExpression TypeValueReference(string name, TsTypeOrigin? origin) =>
        origin is null ? new TsIdentifier(name) : new TsTypeReference(name, origin);

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

        // `[JsCallable]` invoke: `recv.Invoke(args)` lowers to a direct call on
        // the receiver `recv(args)`. After destructuring, the receiver IS the
        // JS function — the `Invoke` member is erased. Overloaded `Invoke`
        // (value / updater / arbitrary arity) all map identically; only the
        // argument list differs. Map the receiver (the member-access target),
        // never the `.Invoke` access itself.
        if (
            call.Origin is { IsJsCallableInvoke: true }
            && call.Target is IrMemberAccess invokeAccess
        )
        {
            var receiver = Map(invokeAccess.Target, bclRegistry);
            return new TsCallExpression(receiver, loweredArgs, call.IsOptional);
        }

        // `[PlainObject]` instance method call: rewrite `obj.Method(args)` to
        // `methodName(obj, args)` — the plain-object shape has no class
        // prototype, so the method is emitted as a standalone helper that
        // takes the receiver as its first parameter.
        if (
            call.Origin is { IsPlainObjectInstanceMethod: true } pooMeta
            && call.Target is IrMemberAccess pooAccess
        )
            return MapPlainObjectHelperCall(pooMeta, pooAccess.Target, loweredArgs, bclRegistry);

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

    private static TsCallExpression MapPlainObjectHelperCall(
        IrMemberOrigin method,
        IrExpression receiver,
        IReadOnlyList<TsExpression> loweredArgs,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var helperName = method.EmittedName ?? TypeScriptNaming.ToCamelCase(method.MemberName);

        return new TsCallExpression(
            new TsIdentifier(helperName),
            [Map(receiver, bclRegistry), .. loweredArgs]
        );
    }

    private static string? TsTypeArgName(IrTypeRef type) =>
        IrToTsTypeMapper.Map(type) is TsNamedType n ? n.Name : null;

    public static TsExpression MapArgument(IrArgument arg, DeclarativeMappingRegistry? bclRegistry)
    {
        var value = Map(arg.Value, bclRegistry);
        return arg.IsSpread ? new TsSpreadExpression(value) : value;
    }

    private static TsType? LowerLambdaParameterType(IrParameter p)
    {
        // [External] ambient shapes have no emitted declaration in the
        // consumer's tree, so the lambda parameter type annotation is
        // omitted and TypeScript infers it from the surrounding signature
        // (the real .d.ts of the npm package). [Ignore] should be barred
        // by MS0013 before reaching emission, but the same drop is kept
        // so a suppressed-diagnostic run still produces compilable TS.
        //
        // A JSX-renderable type (native element, component, or imported
        // renderable) has no usable TS type name in value position: a
        // [JsxNativeElement] record lowers to an intrinsic tag (`<button>`),
        // never an emitted `Html.Button` type, so annotating a handler
        // parameter with it (`(_: Html.Button) => …`) references a name that
        // doesn't exist in the generated module. Omit the annotation and let
        // TS infer the parameter type from the surrounding JSX signature
        // (e.g. SolidJS's `onClick` handler type).
        if (
            p.Type is IrNamedTypeRef named
            && named.Semantics is { } sem
            && (sem.IsIgnored || sem.IsExternal || sem.RendersAsJsxElement)
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
        var mappedParameters = lambda
            .Parameters.Select(p => new TsParameter(
                TypeScriptNaming.ToCamelCase(p.Name),
                LowerLambdaParameterType(p)
            ))
            .ToList();
        var parameters = DeduplicateParameterNames(
            DropTrailingErasedDiscards(lambda.Parameters, mappedParameters)
        );
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
    /// Drops a trailing C# discard parameter (named <c>_</c>) ONLY when its TS type
    /// annotation was erased (<see cref="LowerLambdaParameterType"/> returned
    /// <see langword="null"/> — e.g. a JSX event handler whose <c>[JsxNativeElement]</c>
    /// delegate type has no emitted name). Keeping such a discard would emit an
    /// implicit-<c>any</c> parameter (<c>(_) =&gt; …</c>) that fails under
    /// <c>noImplicitAny</c>; the discard is never read, and these lambdas only appear
    /// in contextually-typed callback positions (JSX handlers, hoisted <c>const</c>
    /// handlers), so the idiomatic zero/short-arg form
    /// (<c>_ =&gt; …</c> → <c>() =&gt; …</c>) is safe.
    /// <para>
    /// A discard that DOES carry a TS type is KEPT (<c>(_: number) =&gt; …</c>): it has
    /// no implicit-<c>any</c> risk, and dropping it would change the lambda's arity —
    /// breaking direct call sites (<c>f(42)</c> against a now-zero-arg arrow, TS2554)
    /// and runtime <c>fn.length</c> overload dispatch. Only TRAILING erased discards
    /// are dropped — a discard followed by a kept parameter must stay to preserve
    /// positional binding.
    /// </para>
    /// </summary>
    private static List<TsParameter> DropTrailingErasedDiscards(
        IReadOnlyList<IrParameter> source,
        List<TsParameter> mapped
    )
    {
        var lastKept = mapped.Count - 1;
        while (
            lastKept >= 0
            && string.Equals(source[lastKept].Name, "_", StringComparison.Ordinal)
            && mapped[lastKept].Type is null
        )
            lastKept--;
        return lastKept == mapped.Count - 1 ? mapped : mapped.Take(lastKept + 1).ToList();
    }

    /// <summary>
    /// Renames colliding lambda parameter names so the emitted TS parameter
    /// list is syntactically valid. C# permits repeated discards
    /// (<c>(_, _) =&gt; …</c>) and shadowing across positions, but TS rejects a
    /// duplicate binding name in a single parameter list. The first occurrence
    /// keeps its name; each later collision is suffixed with its positional
    /// index (<c>_</c>, <c>_1</c>, <c>_2</c>), and the suffixed name is itself
    /// checked against the running set so a synthesized name never collides
    /// with an explicit one. Order is preserved.
    /// </summary>
    private static List<TsParameter> DeduplicateParameterNames(List<TsParameter> parameters)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<TsParameter>(parameters.Count);
        for (var i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (seen.Add(p.Name))
            {
                result.Add(p);
                continue;
            }
            var candidate = $"{p.Name}{i}";
            while (!seen.Add(candidate))
                candidate = $"{candidate}_";
            result.Add(p with { Name = candidate });
        }
        return result;
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
                IrToTsNamingPolicy.ToDataMemberName(a.MemberName, a.EmittedName),
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

    /// <summary>
    /// Lowers <see cref="IrLetExpression"/> to an IIFE (see
    /// <see cref="TsImmediateInvocation.Of"/>): the binding becomes the
    /// arrow's lone parameter and the value supplies the single argument.
    /// The arrow's body is a one-statement <c>return</c> of the body
    /// expression so the let-binding stays a pure expression at the call site
    /// (compound-assignment, increment, index-from-end and null-conditional
    /// lowerings all need an expression, not a statement). A body that
    /// awaits makes the IIFE async and awaited.
    /// </summary>
    private static TsExpression MapLetExpression(
        IrLetExpression let,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var value = Map(let.Value, bclRegistry);

        return TryFoldPlainObjectInitializer(let, value, bclRegistry)
            ?? TsImmediateInvocation.Of(
                [new TsImmediateBinding(let.Name, value)],
                ReturningBody(let.Body, bclRegistry),
                isAsync: IrEvaluationTraits.ContainsAwait(let.Body)
            );
    }

    /// <summary>
    /// Folds an object-initialized <c>[PlainObject]</c> construction —
    /// <c>let o = { a: x } in (o.b = y, o)</c>, the extractor's lowering of
    /// <c>new T(x) { B = y }</c> — into the single literal
    /// <c>{ a: x, b: y }</c>. Applies only when every effect assigns a member
    /// of the bound object and no key repeats (a repeated key is a TypeScript
    /// error); the literal evaluates its values in the same order. Returns
    /// <c>null</c> for any other let-binding.
    /// </summary>
    private static TsExpression? TryFoldPlainObjectInitializer(
        IrLetExpression let,
        TsExpression mappedValue,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (
            let
                is not {
                    Value: IrNewExpression { IsPlainObject: true },
                    Body: IrSequenceExpression { Result: IrIdentifier result } sequence,
                }
            || result.Name != let.Name
            || mappedValue is not TsObjectLiteral literal
        )
            return null;

        var writes = sequence
            .Effects.Select(effect => PlainObjectMemberWrite(effect, let.Name))
            .ToList();

        if (writes.Any(write => write is null))
            return null;

        var members = writes.Select(write => write!.Value).ToList();
        var keys = members.Select(write => AccessedMemberName(write.Member)).ToList();

        if (
            keys.Concat(literal.Properties.Select(property => property.Key)).Distinct().Count()
            != keys.Count + literal.Properties.Count
        )
            return null;

        return new TsObjectLiteral([
            .. literal.Properties,
            .. members.Select(
                (write, index) => new TsObjectProperty(keys[index], Map(write.Value, bclRegistry))
            ),
        ]);
    }

    /// <summary>
    /// The member and value of <c>instance.Member = value</c> written straight
    /// to the let-bound <paramref name="instanceName"/>; <c>null</c> for any
    /// other effect.
    /// </summary>
    private static (IrMemberAccess Member, IrExpression Value)? PlainObjectMemberWrite(
        IrExpression effect,
        string instanceName
    ) =>
        effect
            is IrBinaryExpression
            {
                Operator: IrBinaryOp.Assign,
                Left: IrMemberAccess { Target: IrIdentifier target } member,
                Right: var value,
            }
        && target.Name == instanceName
            ? (member, value)
            : null;

    /// <summary>
    /// Lowers <see cref="IrSequenceExpression"/> outside a let-binding to a
    /// parameterless IIFE whose body runs the effects and returns the result.
    /// </summary>
    private static TsExpression MapSequence(
        IrSequenceExpression sequence,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        TsImmediateInvocation.Of(
            [],
            ReturningBody(sequence, bclRegistry),
            isAsync: IrEvaluationTraits.ContainsAwait(sequence)
        );

    /// <summary>
    /// The statements of an IIFE body that yields <paramref name="body"/>: a
    /// <see cref="IrSequenceExpression"/> spreads into one statement per
    /// effect followed by the <c>return</c> of its result, anything else is a
    /// single <c>return</c>.
    /// </summary>
    private static IReadOnlyList<TsStatement> ReturningBody(
        IrExpression body,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        body is IrSequenceExpression sequence
            ?
            [
                .. sequence.Effects.Select(effect => new TsExpressionStatement(
                    Map(effect, bclRegistry)
                )),
                new TsReturnStatement(Map(sequence.Result, bclRegistry)),
            ]
            : [new TsReturnStatement(Map(body, bclRegistry))];

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
                    // A template-literal hole accepts any expression, so the
                    // hole needs no grouping of its own.
                    expressions.Add(Map(expr.Expression, bclRegistry));
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
                TsNamedType n => TypeValueReference(n.Name, n.Origin),
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
        // JSX-renderable constructions (native elements + component composition)
        // lower to a TsJsxElement, never a `new T(…)` call. Decided purely off
        // the constructed value's type semantics (FR-012), so this must run
        // before the plain-object / branded / exception branches below.
        if (ne.Type is IrNamedTypeRef { Semantics.RendersAsJsxElement: true })
            return IrToTsJsxBridge.Convert(ne, bclRegistry);

        var args = ne.Arguments.Select(a => MapArgument(a, bclRegistry)).ToList();
        if (ne.IsObjectArgsCtor && ne.Type is IrNamedTypeRef objectArgsTarget)
        {
            IReadOnlyList<TsType>? typeArguments = objectArgsTarget.TypeArguments
                is { Count: > 0 } tArgs
                ? tArgs.Select(t => IrToTsTypeMapper.Map(t)).ToList()
                : null;
            return new TsCallExpression(
                new TsPropertyAccess(
                    TypeValueReference(
                        objectArgsTarget.Name,
                        IrToTsTypeMapper.TryResolveOrigin(objectArgsTarget)
                    ),
                    "create"
                ),
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
        // `[JsTuple]` construction: `new T(a, b)` lowers to a positional array
        // literal `[a, b]`, the array-shape sibling of the `[PlainObject]`
        // object-literal lowering above. The class wrapper is erased; the
        // tuple IS the array.
        if (ne.IsJsTuple)
            return new TsArrayLiteral(args);
        if (ne.Type is IrNamedTypeRef named && named.Semantics is { } s)
        {
            // BCL exception types (InvalidOperationException, …) have no
            // runtime class on the TS side — map them to the builtin `Error`,
            // rebuilding its (message, { cause }) arguments from the
            // constructor-parameter roles. Transpilable exception subclasses
            // keep their generated identity.
            if (IrToTsTypeMapper.IsBclException(named))
            {
                return MapBclExceptionCreation(ne, args, bclRegistry);
            }

            // [Branded] structs compile to companion objects with a
            // `create` factory, not a runtime class. `new UserId(v)` therefore
            // lowers to `UserId.create(v)` on the TS side.
            if (s.Kind is IrNamedTypeKind.Branded)
                return new TsCallExpression(
                    new TsPropertyAccess(
                        TypeValueReference(named.Name, IrToTsTypeMapper.TryResolveOrigin(named)),
                        "create"
                    ),
                    args
                );
        }

        if (
            bclRegistry is not null
            && MapDeclarativeConstruction(ne, args, bclRegistry) is { } mapped
        )
            return mapped;

        return new TsNewExpression(MapNewTarget(ne.Type), args);
    }

    /// <summary>
    /// Lowers <c>new T(args)</c> through a declarative constructor mapping
    /// (<c>new List&lt;T&gt;(capacity)</c> → <c>[] as Array&lt;T&gt;</c>). A
    /// template may drop an argument with no JS counterpart (a capacity
    /// hint). When a dropped argument has side effects, every side-effecting
    /// argument is bound, in source order, to a parameter of an immediately
    /// invoked arrow and the template reads the parameters, so each argument
    /// is still evaluated exactly once and in C# order.
    /// </summary>
    private static TsExpression? MapDeclarativeConstruction(
        IrNewExpression ne,
        IReadOnlyList<TsExpression> args,
        DeclarativeMappingRegistry bclRegistry
    )
    {
        var typeArguments = (ne.TypeArguments ?? [])
            .Select(typeArgument => IrToTsTypeMapper.Map(typeArgument))
            .ToList();
        var template = IrToTsBclMapper.TryMapConstruction(ne, args, typeArguments, bclRegistry);

        if (template is null)
            return null;

        var referencedArgumentIndices = TsTemplatePlaceholder.ReferencedArgumentIndices(
            template.Template
        );

        return DropsASideEffect(ne.Arguments, referencedArgumentIndices)
            ? BindSideEffectsInSourceOrder(template, ne.Arguments)
            : template;
    }

    private static bool DropsASideEffect(
        IReadOnlyList<IrArgument> arguments,
        IReadOnlySet<int> referencedArgumentIndices
    ) =>
        arguments
            .Where((_, index) => !referencedArgumentIndices.Contains(index))
            .Any(argument => !IrEvaluationTraits.IsSideEffectFree(argument.Value));

    /// <summary>
    /// <c>(($arg0, $arg1) =&gt; template($arg0, $arg1, pure2))(read0, effect1)</c>.
    /// Every argument up to the last side-effecting one is bound, in source
    /// order, unless it is a literal: a side-effect-free read (a field, a
    /// local) placed before an effect must still observe the value from
    /// before that effect. Arguments after the last effect stay inline, as
    /// C# evaluates them after it too. The parameter names contain a
    /// <c>$</c>, which no C# identifier can, so they never shadow a user name;
    /// the arrow body only reads parameters and values evaluated after every
    /// effect, so it needs no <c>async</c> even when a bound argument awaits.
    /// The template arguments are the lowered <paramref name="arguments"/>,
    /// index for index.
    /// </summary>
    private static TsExpression BindSideEffectsInSourceOrder(
        TsTemplate template,
        IReadOnlyList<IrArgument> arguments
    )
    {
        var bindings = new List<TsImmediateBinding>();
        var boundArguments = new List<TsExpression>(template.Arguments.Count);
        var lastEffectIndex = LastSideEffectIndex(arguments);

        for (var index = 0; index < template.Arguments.Count; index++)
        {
            if (index > lastEffectIndex || arguments[index].Value is IrLiteral)
            {
                boundArguments.Add(template.Arguments[index]);

                continue;
            }

            var binding = new TsImmediateBinding($"$arg{index}", template.Arguments[index]);
            bindings.Add(binding);
            boundArguments.Add(new TsIdentifier(binding.Name));
        }

        var body = template with { Arguments = boundArguments };

        return TsImmediateInvocation.Of(bindings, [new TsReturnStatement(body)], isAsync: false);
    }

    private static int LastSideEffectIndex(IReadOnlyList<IrArgument> arguments)
    {
        for (var index = arguments.Count - 1; index >= 0; index--)
        {
            if (!IrEvaluationTraits.IsSideEffectFree(arguments[index].Value))
                return index;
        }

        return -1;
    }

    private static TsExpression MapBclExceptionCreation(
        IrNewExpression ne,
        IReadOnlyList<TsExpression> positionalArgs,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        ne.ExceptionConstruction is { } construction
            ? IrToTsExceptionConstructionBridge.MapNewError(ne, construction, bclRegistry)
            : new TsNewExpression(new TsIdentifier("Error"), positionalArgs);

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
            IrBinaryOp.IntegerDivide => throw new System.Diagnostics.UnreachableException(
                "Integer division has no JS operator; it lowers through MapIntegerDivision."
            ),
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
