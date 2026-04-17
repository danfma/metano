using Metano.Compiler.IR;
using Metano.Dart.AST;

namespace Metano.Dart.Bridge;

/// <summary>
/// Converts an <see cref="IrClassDeclaration"/> into a Dart <see cref="DartClass"/>.
/// Renders the class <em>shape</em> only — fields, getters, method signatures, constructor
/// parameter list. Bodies are emitted as <c>=&gt; throw UnimplementedError();</c> stubs so
/// the consuming Flutter app type-checks while Phase 5 is still in progress.
/// <para>
/// Promoted constructor parameters become <c>final</c> fields plus <c>this.x</c> initializers,
/// matching the Dart idiom for immutable data-carrying classes.
/// </para>
/// </summary>
public static class IrToDartClassBridge
{
    public static void Convert(IrClassDeclaration ir, List<DartTopLevel> statements)
    {
        var name = IrToDartNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
        var modifier = ResolveClassModifier(ir);
        var typeParameters = ConvertTypeParameters(ir.TypeParameters);

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
        AddPromotedFields(ir.Constructor, members);
        if (ir.Members is not null)
        {
            foreach (var m in ir.Members)
            {
                switch (m)
                {
                    case IrFieldDeclaration field:
                        members.Add(ConvertField(field));
                        break;
                    case IrPropertyDeclaration prop when !promotedParamNames.Contains(prop.Name):
                        members.Add(ConvertProperty(prop));
                        break;
                    case IrMethodDeclaration method:
                        members.Add(ConvertMethod(method));
                        break;
                }
            }
        }

        var ctor = ir.Constructor is not null ? ConvertConstructor(ir.Constructor, name) : null;

        // Record types get value-equality semantics synthesized into the class:
        // an `operator ==`, a `hashCode` getter, and a `copyWith` method
        // (Dart's idiomatic equivalent of C#'s `with` expression). The
        // synthesis is skipped for [PlainObject] records — those are emitted
        // as plain data carriers without behavior.
        if (ir.Semantics.IsRecord && !ir.Semantics.IsPlainObject && ir.Constructor is not null)
            SynthesizeRecordMembers(ir, name, members);

        statements.Add(
            new DartClass(
                name,
                Modifier: modifier,
                TypeParameters: typeParameters,
                ExtendsType: extendsType,
                Implements: implementsList,
                Constructor: ctor,
                Members: members.Count > 0 ? members : null
            )
        );
    }

    // ── Record synthesis ───────────────────────────────────────────────────

    /// <summary>
    /// Synthesizes <c>operator ==</c>, <c>hashCode</c>, and <c>copyWith</c> for
    /// a record-like class. The fields to compare/hash/rebuild are the
    /// constructor's primary parameters (promoted or not) — the same set C# uses
    /// for its synthesized value equality.
    /// </summary>
    private static void SynthesizeRecordMembers(
        IrClassDeclaration ir,
        string className,
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

        members.Add(BuildEqualsOperator(className, fields));
        members.Add(BuildHashCodeGetter(fields));
        members.Add(BuildCopyWithMethod(className, fields));
    }

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
        return new DartMethodSignature(
            Name: "==",
            Parameters: [new DartParameter("other", new DartNamedType("Object"))],
            ReturnType: new DartNamedType("bool"),
            Body: [new IrReturnStatement(body)],
            OperatorSymbol: "==",
            // Dart's analyzer warns when a class overrides Object.== without
            // `@override`; records always do, so tag unconditionally.
            IsOverride: true
        );
    }

    // Dart's Object.hash caps at 20 positional arguments; wider records must fall
    // back to Object.hashAll(Iterable) instead. Source:
    // https://api.dart.dev/stable/dart-core/Object/hash.html
    private const int DartObjectHashMaxArity = 20;

    /// <summary>
    /// Emits a <c>hashCode</c> getter that combines every field. For zero-field
    /// records we fall back to a stable <c>0</c>; for up to 20 fields we use
    /// <c>Object.hash(a, b, …)</c>; beyond that we switch to
    /// <c>Object.hashAll([…])</c> because <c>Object.hash</c> only exposes
    /// positional arity up to 20.
    /// </summary>
    private static DartClassMember BuildHashCodeGetter(
        IReadOnlyList<(string Name, DartType Type)> fields
    )
    {
        IrExpression body;
        if (fields.Count == 0)
        {
            body = new IrLiteral(0, IrLiteralKind.Int32);
        }
        else
        {
            var args = fields
                .Select(f => new IrArgument(new IrMemberAccess(new IrThisExpression(), f.Name)))
                .ToList();
            body =
                fields.Count <= DartObjectHashMaxArity
                    ? new IrCallExpression(
                        new IrMemberAccess(new IrTypeReference("Object"), "hash"),
                        args
                    )
                    : new IrCallExpression(
                        new IrMemberAccess(new IrTypeReference("Object"), "hashAll"),
                        [new IrArgument(new IrArrayLiteral(args.Select(a => a.Value).ToList()))]
                    );
        }
        return new DartGetter(
            Name: "hashCode",
            ReturnType: new DartNamedType("int"),
            Body: [new IrReturnStatement(body)],
            // Object.hashCode is already defined, so this is an override — the
            // analyzer complains without the annotation.
            IsOverride: true
        );
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

        IrExpression body = new IrNewExpression(new IrNamedTypeRef(className), callArgs);
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

    private static DartConstructor ConvertConstructor(
        IrConstructorDeclaration ctor,
        string className
    )
    {
        var parameters = ctor
            .Parameters.Select(p => new DartConstructorParameter(
                Name: IrToDartNamingPolicy.ToParameterName(p.Parameter.Name),
                Type: p.Promotion == IrParameterPromotion.None
                    ? IrToDartTypeMapper.Map(p.Parameter.Type)
                    : null,
                IsFieldInitializer: p.Promotion != IrParameterPromotion.None,
                IsRequired: !p.Parameter.HasDefaultValue,
                DefaultValue: p.Parameter.DefaultValue
            ))
            .ToList();
        return new DartConstructor(className, parameters, Body: ctor.Body);
    }

    // ── Members ────────────────────────────────────────────────────────────

    private static DartField ConvertField(IrFieldDeclaration field) =>
        new(
            IrToDartNamingPolicy.ToParameterName(field.Name),
            IrToDartTypeMapper.Map(field.Type),
            IsFinal: field.IsReadonly,
            IsStatic: field.IsStatic,
            // Dart requires non-nullable fields to be initialized at declaration or in
            // the constructor. `late` is the safe fallback when we have neither — a
            // field initializer from the IR removes the need for it; a ctor body that
            // assigns the field (e.g. `_view = view`) keeps `late` so Dart accepts the
            // deferred initialization. Applies equally to static fields: an
            // uninitialized `static int count;` is invalid without `late`.
            IsLate: !IsNullable(field.Type) && field.Initializer is null,
            Initializer: field.Initializer
        );

    private static bool IsNullable(IrTypeRef type) => type is IrNullableTypeRef;

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

    private static DartClassMember ConvertProperty(IrPropertyDeclaration prop)
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
            // Non-nullable fields only need `late` when there's no initializer;
            // the presence of one makes the field satisfied at construction
            // time without the modifier.
            var hasInitializer = prop.Initializer is not null;
            return new DartField(
                IrToDartNamingPolicy.ToMemberName(prop.Name, prop.Attributes),
                IrToDartTypeMapper.Map(prop.Type),
                IsFinal: isFinal,
                IsStatic: prop.IsStatic,
                IsLate: !prop.IsStatic && !IsNullable(prop.Type) && !hasInitializer,
                Initializer: prop.Initializer
            );
        }

        return new DartGetter(
            IrToDartNamingPolicy.ToMemberName(prop.Name, prop.Attributes),
            IrToDartTypeMapper.Map(prop.Type),
            IsStatic: prop.IsStatic,
            IsAbstract: false,
            Body: prop.GetterBody
        );
    }

    private static DartMethodSignature ConvertMethod(IrMethodDeclaration method) =>
        new(
            Name: IrToDartNamingPolicy.ToMemberName(method.Name, method.Attributes),
            Parameters: method
                .Parameters.Select(p => new DartParameter(
                    IrToDartNamingPolicy.ToParameterName(p.Name),
                    IrToDartTypeMapper.Map(p.Type)
                ))
                .ToList(),
            ReturnType: IrToDartTypeMapper.Map(method.ReturnType),
            TypeParameters: ConvertTypeParameters(method.TypeParameters),
            IsStatic: method.IsStatic,
            IsAbstract: method.Semantics.IsAbstract,
            IsAsync: method.Semantics.IsAsync,
            Body: method.Body,
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

    private static IReadOnlyList<DartTypeParameter>? ConvertTypeParameters(
        IReadOnlyList<IrTypeParameter>? typeParameters
    )
    {
        if (typeParameters is null || typeParameters.Count == 0)
            return null;

        return typeParameters
            .Select(tp =>
            {
                var extends = tp.Constraints is { Count: > 0 } c
                    ? IrToDartTypeMapper.Map(c[0])
                    : null;
                return new DartTypeParameter(tp.Name, extends);
            })
            .ToList();
    }
}
