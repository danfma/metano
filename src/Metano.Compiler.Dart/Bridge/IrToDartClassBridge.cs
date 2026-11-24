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
/// shape (<see cref="DartOverloadShape.NamesMembers"/>) keep the primary declaration.
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
        var namesOverloadMembers = claims.Shape.NamesMembers;
        var name = IrToDartNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
        var modifier = ResolveClassModifier(ir);
        var typeParameters = IrToDartTypeParameterMapper.Map(ir.TypeParameters);

        var extendsType =
            ir.BaseType is not null && !IsUnextendableDartCollection(ir.BaseType)
                ? IrToDartTypeMapper.Map(ir.BaseType)
                : null;
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
        DartConstructorsBridge.AddPromotedFields(ir.Constructor, members);
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
                            DartClassMemberNames.MethodNameOf(claims.Shape, method),
                            members,
                            rewriter
                        );

                        break;
                }
            }
        }

        var constructors = DartConstructorsBridge.ConvertConstructors(
            ir,
            name,
            claims,
            namesOverloadMembers,
            rewriter
        );

        if (DartRecordMembersSynthesizer.ShouldSynthesize(ir))
            DartRecordMembersSynthesizer.Synthesize(
                ir,
                name,
                DartConstructorsBridge.PrimaryConstructorNameOf(ir, claims, namesOverloadMembers),
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

    internal static bool KeepsBodyWithoutChain(IrConstructorDeclaration overload) =>
        DartConstructorsBridge.KeepsBodyWithoutChain(overload);

    // ── Members ────────────────────────────────────────────────────────────

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

    private static DartField ConvertField(IrFieldDeclaration field, DartIrRewriter rewriter) =>
        new(
            IrToDartNamingPolicy.ToMemberName(field.Name, field.Attributes),
            IrToDartTypeMapper.Map(field.Type),
            IsFinal: field.IsReadonly,
            IsStatic: field.IsStatic,
            IsLate: NeedsLate(field.Type, !field.IsCapturedByCtor && field.Initializer is not null),
            Initializer: field.IsCapturedByCtor ? null : rewriter.Rewrite(field.Initializer)
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
            var name = claims.RequiredFastPathAt(overload.Name, overload.DeclarationOrdinal);

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
            members.Add(DartRecordMembersSynthesizer.BuildEqualsOperatorDelegatingTo(name));
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
                    IrToDartTypeMapper.Map(p.Type),
                    DefaultValue: rewriter.Rewrite(p.DefaultValue)
                ))
                .ToList(),
            ReturnType: IrToDartTypeMapper.Map(method.ReturnType),
            TypeParameters: IrToDartTypeParameterMapper.Map(method.TypeParameters),
            IsStatic: method.IsStatic,
            IsAbstract: method.Semantics.IsAbstract,
            IsAsync: method.Semantics.IsAsync,
            Body: rewriter.Rewrite(method.Body),
            OperatorSymbol: method.Semantics.IsOperator
                ? MapOperatorToDart(method.Semantics.OperatorKind?.Name)
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

    internal static bool IsUnextendableDartCollection(IrTypeRef? typeRef) =>
        (typeRef is IrCollectionTypeRef collection ? collection.LegacyForm : typeRef)
            is IrMapTypeRef
                or IrArrayTypeRef
                or IrSetTypeRef;

    private static DartClassModifier ResolveClassModifier(IrClassDeclaration ir)
    {
        if (ir.Semantics.IsAbstract)
            return DartClassModifier.Abstract;
        if (ir.Semantics.IsSealed)
            return DartClassModifier.Final;
        return DartClassModifier.None;
    }
}
