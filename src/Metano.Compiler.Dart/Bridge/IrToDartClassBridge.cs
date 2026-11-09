using Metano.Compiler.Analysis;
using Metano.Compiler.Dart.AST;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Converts an <see cref="IrClassDeclaration"/> into a Dart <see cref="DartClass"/>.
/// Renders the class <em>shape</em> only — fields, getters, method signatures, constructor
/// parameter list. Bodies are emitted as <c>=&gt; throw UnimplementedError();</c> stubs so
/// the consuming Flutter app type-checks while Phase 5 is still in progress.
/// <para>
/// Promoted constructor parameters become <c>final</c> fields plus <c>this.x</c> initializers,
/// matching the Dart idiom for immutable data-carrying classes.
/// </para>
/// <para>
/// Dart has no overloading, so a dispatched overload set is declared under the names the
/// class's claim pass gave it (spec 013 FR-022, DECIDED-14): one named constructor per
/// constructor overload (<c>Dimension.fromSize</c>; a <c>: this(...)</c> chain redirects to
/// its target's) and one fast path per method overload (<c>addXY</c>), with no dispatcher and
/// no selector. An overloaded operator group and the overloads of a keep-today's-emission
/// shape (<see cref="DartTypeShapes.NamesOverloadMembers"/>) keep the primary declaration.
/// </para>
/// </summary>
public static class IrToDartClassBridge
{
    public static void Convert(
        IrClassDeclaration ir,
        DartOverloadClaims claims,
        List<DartTopLevel> statements,
        DartIrRewriter rewriter
    )
    {
        var namesOverloadMembers = DartTypeShapes.NamesOverloadMembers(ir);
        var name = IrToDartNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
        var modifier = ResolveClassModifier(ir);
        var typeParameters = IrToDartTypeParameterMapper.Map(ir.TypeParameters);

        var extendsType = ir.BaseType is not null ? IrToDartTypeMapper.Map(ir.BaseType) : null;
        // Filter out C# BCL interfaces (IEquatable, IComparable, ...) that records and
        // structs implicitly implement — they have no Dart equivalent and would produce
        // unresolvable references. User-defined interfaces pass through untouched.
        var filteredInterfaces = ir.Interfaces?.Where(IsUserDefinedInterface).ToList();
        var implementsList = filteredInterfaces is { Count: > 0 } ifs
            ? (IReadOnlyList<DartType>)ifs.Select(IrToDartTypeMapper.Map).ToList()
            : null;

        var promotedParamNames = ir.Constructor is not null
            ? ir
                .Constructor.Parameters.Where(p => p.Promotion != IrParameterPromotion.None)
                .Select(p => p.Parameter.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        var members = new List<DartClassMember>();
        var emittedFastPaths = new HashSet<string>(StringComparer.Ordinal);
        AddPromotedFields(ir.Constructor, members);
        if (ir.Members is not null)
        {
            foreach (var m in ir.Members)
            {
                switch (m)
                {
                    case IrMethodDeclaration method
                        when namesOverloadMembers && DartClassMemberNames.IsFastPathGroup(method):
                        AddFastPaths(method, claims, members, emittedFastPaths, rewriter);
                        break;
                    case IrFieldDeclaration field:
                        members.Add(ConvertField(field, rewriter));
                        break;
                    case IrPropertyDeclaration prop when !promotedParamNames.Contains(prop.Name):
                        members.Add(ConvertProperty(prop, rewriter));
                        break;
                    case IrMethodDeclaration method
                        when IrObjectContract.IsHashCodeOverride(method):
                        members.Add(ConvertHashCodeOverride(method, rewriter));
                        break;
                    case IrMethodDeclaration method:
                        AddMethod(
                            method,
                            IrToDartNamingPolicy.ToMemberName(method.Name, method.Attributes),
                            members,
                            rewriter
                        );
                        break;
                }
            }
        }

        var constructors = ConvertConstructors(ir, name, claims, namesOverloadMembers, rewriter);

        // Record types get value-equality semantics synthesized into the class:
        // an `operator ==`, a `hashCode` getter, and a `copyWith` method
        // (Dart's idiomatic equivalent of C#'s `with` expression). The
        // synthesis is skipped for [PlainObject] records — those are emitted
        // as plain data carriers without behavior.
        if (ShouldSynthesizeRecordMembers(ir))
            SynthesizeRecordMembers(
                ir,
                name,
                PrimaryConstructorNameOf(ir, claims, namesOverloadMembers),
                members
            );

        statements.Add(
            new DartClass(
                name,
                Modifier: modifier,
                TypeParameters: typeParameters,
                ExtendsType: extendsType,
                Implements: implementsList,
                Constructors: constructors,
                Members: members.Count > 0 ? members : null
            )
        );
    }

    // ── Record synthesis ───────────────────────────────────────────────────

    private static bool ShouldSynthesizeRecordMembers(IrClassDeclaration ir) =>
        ir.Semantics.IsRecord && !ir.Semantics.IsPlainObject && ir.Constructor is not null;

    /// <summary>
    /// Synthesizes <c>operator ==</c>, <c>hashCode</c>, and <c>copyWith</c> for
    /// a record-like class. The fields to compare/hash/rebuild are the
    /// constructor's primary parameters (promoted or not) — the same set C# uses
    /// for its synthesized value equality.
    /// <para>
    /// A record that declares its own equality members
    /// (<see cref="IrRecordEqualityOverrides"/>) keeps them: a
    /// <c>GetHashCode</c> override already became the <c>hashCode</c> getter,
    /// so none is synthesized, and with a user <c>Equals(R? other)</c> the
    /// synthesized <c>operator ==</c> delegates to it, as C#'s synthesized
    /// <c>Equals(object)</c> does.
    /// </para>
    /// </summary>
    private static void SynthesizeRecordMembers(
        IrClassDeclaration ir,
        string className,
        string? primaryConstructorName,
        List<DartClassMember> members
    )
    {
        var fields = ir.Constructor!.Parameters.Select(p =>
                (
                    Name: IrToDartNamingPolicy.ToParameterName(p.Parameter.Name),
                    Type: IrToDartTypeMapper.Map(p.Parameter.Type)
                )
            )
            .ToList();

        members.Add(
            IrRecordEqualityOverrides.DeclaresEquals(ir)
                ? BuildEqualsOperatorOverUserEquals(className)
                : BuildEqualsOperator(className, fields)
        );

        if (!IrRecordEqualityOverrides.DeclaresHashCode(ir))
            members.Add(BuildHashCodeGetter(fields));

        members.Add(BuildCopyWithMethod(className, primaryConstructorName, fields));
    }

    /// <summary>
    /// <c>bool operator ==(Object other) =&gt; other is T &amp;&amp; equals(other)</c>:
    /// the record's own <c>Equals(R? other)</c> decides, for any <c>R</c>.
    /// </summary>
    private static DartMethodSignature BuildEqualsOperatorOverUserEquals(string className) =>
        BuildEqualsOperatorReturning(
            new IrBinaryExpression(
                new IrIsPatternExpression(
                    new IrIdentifier("other"),
                    new IrTypePattern(new IrNamedTypeRef(className))
                ),
                IrBinaryOp.LogicalAnd,
                CallUserEquals(IrToDartNamingPolicy.ToMemberName(nameof(object.Equals), null))
            )
        );

    /// <summary>
    /// <c>bool operator ==(Object other) =&gt; equals(other)</c> next to a
    /// user <c>Equals(object)</c> override, which stays a method so the
    /// <c>x.Equals(y)</c> call sites keep resolving: Dart's <c>==</c> (and
    /// every collection) then uses the user's equality.
    /// </summary>
    private static DartMethodSignature BuildEqualsOperatorDelegatingTo(string equalsName) =>
        BuildEqualsOperatorReturning(CallUserEquals(equalsName));

    private static IrCallExpression CallUserEquals(string emittedName) =>
        new(
            new IrMemberAccess(new IrThisExpression(), emittedName),
            [new IrArgument(new IrIdentifier("other"))]
        );

    /// <summary>
    /// <c>@override bool operator ==(Object other) =&gt; result</c>. Dart's
    /// analyzer warns when a class overrides <c>Object.==</c> without
    /// <c>@override</c>, so it is always tagged.
    /// </summary>
    private static DartMethodSignature BuildEqualsOperatorReturning(IrExpression result) =>
        new(
            Name: "==",
            Parameters: [new DartParameter("other", new DartNamedType("Object"))],
            ReturnType: new DartNamedType("bool"),
            Body: [new IrReturnStatement(result)],
            OperatorSymbol: "==",
            IsOverride: true
        );

    /// <summary>
    /// A C# <c>GetHashCode</c> override as Dart's <c>hashCode</c> getter, the
    /// member every <c>x.GetHashCode()</c> call site reads and every Dart
    /// collection hashes with.
    /// </summary>
    private static DartGetter ConvertHashCodeOverride(
        IrMethodDeclaration method,
        DartIrRewriter rewriter
    ) =>
        new(
            Name: "hashCode",
            ReturnType: new DartNamedType("int"),
            Body: rewriter.Rewrite(method.Body),
            IsOverride: true
        );

    /// <summary>
    /// Emits <c>bool operator ==(Object other) =&gt; other is T &amp;&amp; other.runtimeType
    /// == runtimeType &amp;&amp; other.a == a &amp;&amp; …</c>.
    /// <para>
    /// The <c>is T</c> test narrows <c>other</c>'s static type so we can access
    /// its fields; the <c>runtimeType ==</c> test enforces exact-type semantics
    /// so <c>Base(1) == Derived(1, 2)</c> is <c>false</c>. Without the runtime-
    /// type check, base records in a hierarchy would treat themselves equal to
    /// any derived instance that shares their primary fields — breaking both C#
    /// record semantics and Dart's <c>==</c> contract (which requires symmetry).
    /// </para>
    /// </summary>
    private static DartMethodSignature BuildEqualsOperator(
        string className,
        IReadOnlyList<(string Name, DartType Type)> fields
    )
    {
        IrExpression body = new IrIsPatternExpression(
            new IrIdentifier("other"),
            new IrTypePattern(new IrNamedTypeRef(className))
        );
        // Exact-type comparison (runtimeType). Placed after the `is T` narrowing
        // so downstream field accesses still see `other` as T.
        body = new IrBinaryExpression(
            body,
            IrBinaryOp.LogicalAnd,
            new IrBinaryExpression(
                new IrMemberAccess(new IrIdentifier("other"), "runtimeType"),
                IrBinaryOp.Equal,
                new IrMemberAccess(new IrThisExpression(), "runtimeType")
            )
        );
        foreach (var (name, _) in fields)
        {
            body = new IrBinaryExpression(
                body,
                IrBinaryOp.LogicalAnd,
                new IrBinaryExpression(
                    new IrMemberAccess(new IrIdentifier("other"), name),
                    IrBinaryOp.Equal,
                    new IrMemberAccess(new IrThisExpression(), name)
                )
            );
        }
        return BuildEqualsOperatorReturning(body);
    }

    /// <summary>
    /// Emits a <c>hashCode</c> getter that combines every field through the
    /// runtime <c>HashCode</c> helper:
    /// <list type="bullet">
    ///   <item>0 fields → literal <c>0</c>.</item>
    ///   <item>1-4 fields → <c>HashCode.combine[N](this.a, …)</c>.</item>
    ///   <item>5+ fields → <c>var hc = HashCode(); hc.add(this.a); …; return hc.toHashCode();</c>.</item>
    /// </list>
    /// Going through <c>HashCode</c> instead of Dart's built-in <c>Object.hash</c>
    /// keeps the hash deterministic and bit-identical to the TypeScript runtime,
    /// so values sharing a record type across stacks share a hash.
    /// </summary>
    private static DartClassMember BuildHashCodeGetter(
        IReadOnlyList<(string Name, DartType Type)> fields
    )
    {
        IReadOnlyList<IrStatement> body =
            fields.Count == 0 ? [new IrReturnStatement(new IrLiteral(0, IrLiteralKind.Int32))]
            : fields.Count <= HashCodeMaxCombineArity
                ? [new IrReturnStatement(BuildCombineExpression(fields))]
            : BuildHashCodeBuilderBody(fields);

        return new DartGetter(
            Name: "hashCode",
            ReturnType: new DartNamedType("int"),
            Body: body,
            IsOverride: true
        );
    }

    // The metano_runtime HashCode helper exposes static `combine`, `combine2`,
    // `combine3`, and `combine4` for narrow records; anything wider goes through
    // the builder API (`HashCode()..add(...)`...`toHashCode()`).
    private const int HashCodeMaxCombineArity = 4;

    private static IrCallExpression BuildCombineExpression(
        IReadOnlyList<(string Name, DartType Type)> fields
    )
    {
        var combineMethod = fields.Count == 1 ? "combine" : $"combine{fields.Count}";
        var args = fields
            .Select(f => new IrArgument(new IrMemberAccess(new IrThisExpression(), f.Name)))
            .ToList();
        return new IrCallExpression(
            new IrMemberAccess(new IrTypeReference("HashCode"), combineMethod),
            args
        );
    }

    private static IReadOnlyList<IrStatement> BuildHashCodeBuilderBody(
        IReadOnlyList<(string Name, DartType Type)> fields
    )
    {
        var statements = new List<IrStatement>(fields.Count + 2)
        {
            new IrVariableDeclaration(
                Name: "hc",
                Type: null,
                Initializer: new IrNewExpression(new IrNamedTypeRef("HashCode"), [])
            ),
        };
        foreach (var (name, _) in fields)
        {
            statements.Add(
                new IrExpressionStatement(
                    new IrCallExpression(
                        new IrMemberAccess(new IrIdentifier("hc"), "add"),
                        [new IrArgument(new IrMemberAccess(new IrThisExpression(), name))]
                    )
                )
            );
        }
        statements.Add(
            new IrReturnStatement(
                new IrCallExpression(new IrMemberAccess(new IrIdentifier("hc"), "toHashCode"), [])
            )
        );
        return statements;
    }

    /// <summary>
    /// Emits a <c>copyWith</c> method — Dart's idiomatic stand-in for C#'s
    /// <c>with</c> expression. Each field becomes an optional named parameter
    /// whose type is made nullable so callers can omit it; the body falls back
    /// to the current instance value with <c>name ?? this.name</c>. A field that
    /// is already nullable can't use <c>??</c> to distinguish "not supplied" from
    /// "supplied null" — for now we accept the same ambiguity C#'s
    /// <c>with</c> has at the Dart call site (null always overwrites), with a
    /// follow-up tracking the sentinel-object workaround.
    /// </summary>
    private static DartMethodSignature BuildCopyWithMethod(
        string className,
        string? primaryConstructorName,
        IReadOnlyList<(string Name, DartType Type)> fields
    )
    {
        var parameters = fields
            .Select(f => new DartParameter(
                f.Name,
                f.Type is DartNullableType ? f.Type : new DartNullableType(f.Type),
                IsNamed: true
            ))
            .ToList();

        // Body: return ClassName(name1 ?? this.name1, name2 ?? this.name2, …)
        var callArgs = fields
            .Select(f => new IrArgument(
                new IrBinaryExpression(
                    new IrIdentifier(f.Name),
                    IrBinaryOp.NullCoalescing,
                    new IrMemberAccess(new IrThisExpression(), f.Name)
                )
            ))
            .ToList();

        IrExpression body = primaryConstructorName is null
            ? new IrNewExpression(new IrNamedTypeRef(className), callArgs)
            : new IrCallExpression(
                new IrMemberAccess(new IrTypeReference(className), primaryConstructorName),
                callArgs
            );
        return new DartMethodSignature(
            Name: "copyWith",
            Parameters: parameters,
            ReturnType: new DartNamedType(className),
            Body: [new IrReturnStatement(body)]
        );
    }

    // ── Constructor + promoted fields ─────────────────────────────────────

    private static void AddPromotedFields(IrConstructorDeclaration? ctor, List<DartClassMember> acc)
    {
        if (ctor is null)
            return;
        foreach (var p in ctor.Parameters)
        {
            if (p.Promotion == IrParameterPromotion.None)
                continue;
            var isFinal = p.Promotion == IrParameterPromotion.ReadonlyProperty;
            acc.Add(
                new DartField(
                    IrToDartNamingPolicy.ToParameterName(p.Parameter.Name),
                    IrToDartTypeMapper.Map(p.Parameter.Type),
                    IsFinal: isFinal
                )
            );
        }
    }

    /// <summary>
    /// The class's constructors: one named constructor per overload of a
    /// dispatched set, in declaration order, or today's unnamed one.
    /// </summary>
    private static IReadOnlyList<DartConstructor>? ConvertConstructors(
        IrClassDeclaration ir,
        string className,
        DartOverloadClaims claims,
        bool namesOverloadMembers,
        DartIrRewriter rewriter
    )
    {
        if (ir.Constructor is not { } constructor)
            return null;

        if (
            !namesOverloadMembers
            || constructor is not { Overloads.Count: > 0, OverloadName: not null }
        )
            return [ConvertConstructor(constructor, className, name: null, rewriter)];

        return constructor
            .DeclarationsInOrder.Select(overload =>
                ConvertNamedConstructor(overload, className, claims, rewriter)
            )
            .ToList();
    }

    /// <summary>
    /// The name of the constructor <c>copyWith</c> rebuilds a record through:
    /// the primary's named constructor when the set is dispatched, else
    /// <c>null</c> (the unnamed one).
    /// </summary>
    private static string? PrimaryConstructorNameOf(
        IrClassDeclaration ir,
        DartOverloadClaims claims,
        bool namesOverloadMembers
    ) =>
        namesOverloadMembers && ir.Constructor is { Overloads.Count: > 0 } primary
            ? claims.ConstructorNameAt(primary.DeclarationOrdinal)
            : null;

    /// <summary>
    /// One overload of a dispatched set as its named constructor. A
    /// <c>: this(...)</c> chain becomes a redirect to the target's named
    /// constructor; Dart allows a redirecting constructor no body, so a chain
    /// with a body of its own keeps the body and reports that the chain is
    /// dropped.
    /// </summary>
    private static DartConstructor ConvertNamedConstructor(
        IrConstructorDeclaration overload,
        string className,
        DartOverloadClaims claims,
        DartIrRewriter rewriter
    )
    {
        var name = claims.ConstructorNameAt(overload.DeclarationOrdinal);
        var constructor = ConvertConstructor(overload, className, name, rewriter);

        if (overload.ThisChain is not { } chain)
            return constructor;

        if (overload.Body is { Count: > 0 })
        {
            DartUnsupported.Report(
                $"Dart target: the constructor '{className}.{name}' chains through "
                    + ": this(...) and has a body; Dart's redirecting constructors have none, so "
                    + "the chain is not emitted."
            );
            return constructor;
        }

        var redirect = new DartConstructorRedirect(
            claims.ConstructorNameAt(chain.Target.DeclarationOrdinal),
            DartConstructorChainArguments.Of(chain, rewriter)
        );

        // A redirecting constructor initializes nothing itself: Dart rejects
        // `this.x` parameters there, so promoted ones become plain parameters.
        return constructor with
        {
            Parameters = ConvertParameters(overload, rewriter, initializesFields: false),
            Body = null,
            Redirect = redirect,
        };
    }

    private static DartConstructor ConvertConstructor(
        IrConstructorDeclaration ctor,
        string className,
        string? name,
        DartIrRewriter rewriter
    ) =>
        new(
            className,
            ConvertParameters(ctor, rewriter, initializesFields: true),
            Body: rewriter.Rewrite(ctor.Body),
            Name: name
        );

    /// <summary>
    /// The constructor's parameters; a promoted one is a <c>this.x</c> field
    /// initializer when <paramref name="initializesFields"/> holds.
    /// </summary>
    private static IReadOnlyList<DartConstructorParameter> ConvertParameters(
        IrConstructorDeclaration ctor,
        DartIrRewriter rewriter,
        bool initializesFields
    ) =>
        ctor
            .Parameters.Select(p =>
            {
                var isFieldInitializer =
                    initializesFields && p.Promotion != IrParameterPromotion.None;

                return new DartConstructorParameter(
                    Name: IrToDartNamingPolicy.ToParameterName(p.Parameter.Name),
                    Type: isFieldInitializer ? null : IrToDartTypeMapper.Map(p.Parameter.Type),
                    IsFieldInitializer: isFieldInitializer,
                    IsRequired: !p.Parameter.HasDefaultValue,
                    DefaultValue: rewriter.Rewrite(p.Parameter.DefaultValue)
                );
            })
            .ToList();

    // ── Members ────────────────────────────────────────────────────────────

    private static DartField ConvertField(IrFieldDeclaration field, DartIrRewriter rewriter) =>
        new(
            IrToDartNamingPolicy.ToParameterName(field.Name),
            IrToDartTypeMapper.Map(field.Type),
            IsFinal: field.IsReadonly,
            IsStatic: field.IsStatic,
            IsLate: NeedsLate(field.Type, field.Initializer is not null),
            Initializer: rewriter.Rewrite(field.Initializer)
        );

    private static bool IsNullable(IrTypeRef type) => type is IrNullableTypeRef;

    /// <summary>
    /// Dart requires non-nullable fields to be initialized at declaration or in
    /// the constructor. <c>late</c> is the safe fallback when neither holds — an
    /// initializer satisfies the field at construction time without the modifier.
    /// Applies equally to static fields: an uninitialized <c>static int count;</c>
    /// is invalid without <c>late</c>.
    /// <para>
    /// Property-backed slots add their own <c>!IsStatic</c> guard at the call
    /// site: Dart forbids <c>late</c> on static properties, but accepts it on
    /// static fields. The asymmetry stays out of this helper because the field
    /// path never needs the extra gate.
    /// </para>
    /// </summary>
    private static bool NeedsLate(IrTypeRef type, bool hasInitializer) =>
        !IsNullable(type) && !hasInitializer;

    /// <summary>
    /// Filters out C# BCL interfaces (IEquatable, IComparable, IFormattable, ...) that
    /// records and structs implement implicitly. These have no Dart counterpart and
    /// should not surface as <c>implements</c> clauses.
    /// </summary>
    private static bool IsUserDefinedInterface(IrTypeRef typeRef) =>
        typeRef is IrNamedTypeRef named
        && !(named.Namespace == "System" && IsBclInterfaceName(named.Name));

    private static bool IsBclInterfaceName(string name) =>
        name
            is "IEquatable"
                or "IComparable"
                or "IFormattable"
                or "ISpanFormattable"
                or "IParsable"
                or "ISpanParsable"
                or "IUtf8SpanFormattable"
                or "IDisposable"
                or "IAsyncDisposable";

    private static DartClassMember ConvertProperty(
        IrPropertyDeclaration prop,
        DartIrRewriter rewriter
    )
    {
        // Plain auto-property → treated as a field (Dart idiom for data members).
        // Computed getters (HasGetterBody) emit as abstract getters until body extraction lands.
        var hasGetterBody = prop.Semantics?.HasGetterBody ?? false;
        if (!hasGetterBody)
        {
            var isFinal =
                prop.Accessors is IrPropertyAccessors.GetOnly or IrPropertyAccessors.GetInit;
            // Auto-property initializers (`public int X { get; } = 42;`) must
            // surface as Dart field initializers — dropping them would change
            // runtime semantics and, for non-nullable fields, leave a `late`
            // declaration pointing at a value that was never written.
            return new DartField(
                IrToDartNamingPolicy.ToMemberName(prop.Name, prop.Attributes),
                IrToDartTypeMapper.Map(prop.Type),
                IsFinal: isFinal,
                IsStatic: prop.IsStatic,
                IsLate: !prop.IsStatic && NeedsLate(prop.Type, prop.Initializer is not null),
                Initializer: rewriter.Rewrite(prop.Initializer)
            );
        }

        return new DartGetter(
            IrToDartNamingPolicy.ToMemberName(prop.Name, prop.Attributes),
            IrToDartTypeMapper.Map(prop.Type),
            IsStatic: prop.IsStatic,
            IsAbstract: false,
            Body: rewriter.Rewrite(prop.GetterBody)
        );
    }

    /// <summary>
    /// The fast path of each overload of a dispatched method group, under the
    /// name the claim pass gave it. Two implementations of a multiply
    /// constructed interface root share its name (<c>MS0069</c>): the first
    /// is declared.
    /// </summary>
    private static void AddFastPaths(
        IrMethodDeclaration primary,
        DartOverloadClaims claims,
        List<DartClassMember> members,
        HashSet<string> emittedFastPaths,
        DartIrRewriter rewriter
    )
    {
        var overloads = IrMethodGroup
            .Of(primary)
            .Members.OrderBy(overload => overload.DeclarationOrdinal);

        foreach (var overload in overloads)
        {
            var name =
                claims.FastPathAt(overload.Name, overload.DeclarationOrdinal)
                ?? IrToDartNamingPolicy.ToFastPathName(
                    overload.Name,
                    overload.OverloadName!.Suffix
                );

            if (emittedFastPaths.Add(name))
                AddMethod(overload, name, members, rewriter);
        }
    }

    private static void AddMethod(
        IrMethodDeclaration method,
        string name,
        List<DartClassMember> members,
        DartIrRewriter rewriter
    )
    {
        members.Add(ConvertMethod(method, name, rewriter));

        if (IrObjectContract.IsEqualsOverride(method))
            members.Add(BuildEqualsOperatorDelegatingTo(name));
    }

    private static DartMethodSignature ConvertMethod(
        IrMethodDeclaration method,
        string name,
        DartIrRewriter rewriter
    ) =>
        new(
            Name: name,
            Parameters: method
                .Parameters.Select(p => new DartParameter(
                    IrToDartNamingPolicy.ToParameterName(p.Name),
                    IrToDartTypeMapper.Map(p.Type)
                ))
                .ToList(),
            ReturnType: IrToDartTypeMapper.Map(method.ReturnType),
            TypeParameters: IrToDartTypeParameterMapper.Map(method.TypeParameters),
            IsStatic: method.IsStatic,
            IsAbstract: method.Semantics.IsAbstract,
            IsAsync: method.Semantics.IsAsync,
            Body: rewriter.Rewrite(method.Body),
            OperatorSymbol: method.Semantics.IsOperator
                ? MapOperatorToDart(method.Semantics.OperatorKind)
                : null
        );

    /// <summary>
    /// Maps C#'s <c>IrMethodSemantics.OperatorKind</c> (e.g. "Addition",
    /// "Equality", "LessThan") to the Dart operator glyph. Returns <c>null</c> for
    /// operator kinds Dart does not support natively — those fall back to a
    /// plain method name (the caller keeps <see cref="DartMethodSignature.Name"/>
    /// so the output is still valid Dart).
    /// </summary>
    private static string? MapOperatorToDart(string? kind) =>
        kind switch
        {
            "Addition" => "+",
            "Subtraction" => "-",
            "Multiply" => "*",
            "Division" => "/",
            "Modulus" => "%",
            "Equality" => "==",
            "LessThan" => "<",
            "LessThanOrEqual" => "<=",
            "GreaterThan" => ">",
            "GreaterThanOrEqual" => ">=",
            "BitwiseAnd" => "&",
            "BitwiseOr" => "|",
            "ExclusiveOr" => "^",
            "LeftShift" => "<<",
            "RightShift" => ">>",
            "UnaryNegation" => "unary-",
            "OnesComplement" => "~",
            _ => null,
        };

    // ── Helpers ────────────────────────────────────────────────────────────

    private static DartClassModifier ResolveClassModifier(IrClassDeclaration ir)
    {
        if (ir.Semantics.IsAbstract)
            return DartClassModifier.Abstract;
        if (ir.Semantics.IsSealed)
            return DartClassModifier.Final;
        return DartClassModifier.None;
    }
}
