using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// What the companion namespace of a <c>[Branded]</c> / <c>[InlineWrapper]</c>
/// type declares: the primitive it brands, its synthesized members and its
/// static method groups with the name each function takes (issue #569).
/// <para>
/// The claims registry (<see cref="TsOverloadClaimsRegistry"/>) owns the one
/// plan of a type, built from its shape — names, parameters, visibility,
/// never a body. Call sites read their fast paths from it, and the emission
/// binds the same plan to the methods it lowers (<see cref="Bind"/>), so the
/// namespace declares exactly the names the calls spell.
/// </para>
/// </summary>
internal sealed class TsCompanionPlan
{
    /// <summary>The non-validating factory every branded companion synthesizes.</summary>
    internal const string CreateMember = "create";

    /// <summary>The string conversion a non-string branded companion synthesizes.</summary>
    internal const string ToStringMember = "toString";

    private TsCompanionPlan(
        TsType primitiveType,
        IReadOnlyList<TsCompanionGroup> groups,
        IReadOnlyList<TsClaimFinding> findings
    )
    {
        PrimitiveType = primitiveType;
        Groups = groups;
        Findings = findings;
    }

    /// <summary>The TS primitive the brand intersects.</summary>
    public TsType PrimitiveType { get; }

    /// <summary>
    /// Whether the companion synthesizes <c>toString</c>: over a string it
    /// would be the identity.
    /// </summary>
    public bool HasToString => SynthesizesToString(PrimitiveType);

    /// <summary>The static method groups in member order.</summary>
    public IReadOnlyList<TsCompanionGroup> Groups { get; }

    /// <summary>
    /// The exported fast paths that took a <c>$</c> because their name was
    /// taken in the namespace (<c>MS0067</c>, spec 013 FR-004).
    /// </summary>
    public IReadOnlyList<TsClaimFinding> Findings { get; }

    /// <summary>
    /// Whether <paramref name="type"/> can have a companion namespace: a
    /// <c>[Branded]</c> / <c>[InlineWrapper]</c> struct. Its plan
    /// (<see cref="Of"/>) still answers <c>null</c> when it wraps something a
    /// brand cannot intersect.
    /// </summary>
    public static bool AppliesTo(INamedTypeSymbol type) =>
        SymbolHelper.HasBranded(type) && type.TypeKind == TypeKind.Struct;

    /// <summary>
    /// The TypeScript answer to
    /// <see cref="IrExtractionContext.EmitsBrandedFastPath"/>: an ordinary
    /// method of a branded type has a fast path a call can name when its
    /// group dispatches — a static one in the companion namespace of a
    /// primitive-backed wrapper, which declares no instance member, and any
    /// method of a wrapper that falls back to a class. Operators and
    /// conversions are not applied through the declared functions (#599).
    /// </summary>
    public static bool EmitsFastPathFor(IMethodSymbol method) =>
        method.MethodKind is MethodKind.Ordinary
        && (method.IsStatic || !SymbolHelper.IsPrimitiveBackedBranded(method.ContainingType));

    /// <summary>
    /// The plan of <paramref name="ir"/>; <c>null</c> when the type is not
    /// branded or wraps something a brand cannot intersect — it then lowers
    /// through the regular class path. The extractor gates the
    /// <c>id.Value</c> → <c>id</c> collapse on the same rule.
    /// </summary>
    public static TsCompanionPlan? Of(IrClassDeclaration ir)
    {
        if (ir.Semantics is not { IsBranded: true, BrandedUnderlyingType: { } underlying })
            return null;

        if (!underlying.IsBrandablePrimitive())
            return null;

        var primitiveType = IrToTsTypeMapper.Map(underlying);
        var statics = StaticMethodsOf(ir);
        var names = new NameClaims(ir.Name, SynthesizedNamesOf(primitiveType), statics);
        var groups = new List<TsCompanionGroup>();

        foreach (var primary in statics)
        {
            var overloads = SurvivingOverloadsOf(primary);

            if (primary.Semantics.ConversionName is not null)
                groups.AddRange(overloads.Select(ConversionFunctionOf));
            else if (overloads.Count > 0)
                groups.Add(GroupOf(overloads, names));
        }

        return new TsCompanionPlan(primitiveType, groups, names.Findings);
    }

    /// <summary>
    /// The fast path of the static overload declared under
    /// <paramref name="memberName"/> at <paramref name="ordinal"/>
    /// (<see cref="IrOverloadRef"/>) that a call site may name;
    /// <c>null</c> when the companion exports none for it.
    /// </summary>
    public string? FastPathAt(string memberName, int ordinal) =>
        Groups
            .OfType<TsCompanionDispatcher>()
            .SelectMany(dispatcher => dispatcher.FastPaths)
            .FirstOrDefault(fastPath => IsAt(fastPath.Method, memberName, ordinal))
            ?.Name;

    /// <summary>
    /// The groups of this plan over the methods of <paramref name="ir"/> —
    /// the same type extracted with its bodies — joined by the overload
    /// identity <see cref="FastPathAt"/> reads, so the emission lowers each
    /// method under the name the plan claimed for it.
    /// </summary>
    public IReadOnlyList<TsCompanionGroup> Bind(IrClassDeclaration ir)
    {
        var methods = StaticMethodsOf(ir)
            .SelectMany(primary => new[] { primary }.Concat(primary.Overloads ?? []))
            .ToList();

        return Groups.Select(group => group.Over(planned => BoundIn(methods, planned))).ToList();
    }

    /// <summary>The emitted method with the identity of a planned one.</summary>
    private static IrMethodDeclaration BoundIn(
        IReadOnlyList<IrMethodDeclaration> methods,
        IrMethodDeclaration planned
    ) =>
        methods.FirstOrDefault(method => IsAt(method, planned.Name, planned.DeclarationOrdinal))
        ?? throw new InvalidOperationException(
            $"The companion plan names '{planned.SignatureText ?? planned.Name}', which the "
                + "emitted type does not declare."
        );

    private static bool IsAt(IrMethodDeclaration method, string memberName, int ordinal) =>
        method.Name == memberName && method.DeclarationOrdinal == ordinal;

    private static bool SynthesizesToString(TsType primitiveType) =>
        primitiveType is not TsStringType;

    private static IReadOnlyList<string> SynthesizedNamesOf(TsType primitiveType) =>
        SynthesizesToString(primitiveType) ? [CreateMember, ToStringMember] : [CreateMember];

    private static List<IrMethodDeclaration> StaticMethodsOf(IrClassDeclaration ir) =>
        (ir.Members ?? []).OfType<IrMethodDeclaration>().Where(method => method.IsStatic).ToList();

    /// <summary>
    /// A static method with the overload siblings folded onto
    /// <see cref="IrMethodDeclaration.Overloads"/>, minus the identity unwrap
    /// conversions: a conversion that only returns the wrapped value
    /// (<c>implicit operator string(UserId self) =&gt; self.Value</c>) is the
    /// identity once the wrapper lowers to <c>primitive &amp; { __brand }</c>,
    /// so the companion skips it. The core decides it from the body
    /// (<see cref="IrMethodSemantics.ReturnsWrappedValue"/>, baseline FR-078):
    /// a conversion out of the wrapper with any other body is a function like
    /// any other.
    /// </summary>
    private static List<IrMethodDeclaration> SurvivingOverloadsOf(IrMethodDeclaration primary) =>
        new[] { primary with { Overloads = null } }
            .Concat(primary.Overloads ?? [])
            .Where(method => !method.Semantics.ReturnsWrappedValue)
            .ToList();

    /// <summary>
    /// A conversion is its own function, named like a class's conversion
    /// static (<c>__implicitFromInt32</c>, baseline FR-078): C# never
    /// dispatches conversions by their arguments.
    /// </summary>
    private static TsCompanionFunction ConversionFunctionOf(IrMethodDeclaration conversion) =>
        new(
            TsOperatorNaming.ConversionHelperName(conversion.Semantics.ConversionName!),
            conversion
        );

    /// <summary>
    /// A single surviving method is one function under its own name; two or
    /// more are a dispatcher under that name plus one exported fast path
    /// each, claimed in dispatch order (an operator group's included, baseline
    /// FR-078).
    /// </summary>
    private static TsCompanionGroup GroupOf(
        IReadOnlyList<IrMethodDeclaration> overloads,
        NameClaims names
    )
    {
        var functionName = FunctionNameOf(overloads[0]);

        if (overloads.Count == 1)
            return new TsCompanionFunction(functionName, overloads[0]);

        var fastPaths = new List<TsCompanionFastPath>();

        foreach (var overload in OverloadDispatchOrder.Sort(overloads, m => m.Parameters))
        {
            var candidate = OverloadFastPathName.Of(functionName, overload);
            var name = names.Claim(candidate, overload);

            fastPaths.Add(new TsCompanionFastPath(overload, name));
        }

        return new TsCompanionDispatcher(functionName, fastPaths);
    }

    /// <summary>
    /// The function names a static group declares before any fast path is
    /// claimed: its own name, or, for a conversion group, each conversion's.
    /// </summary>
    private static IEnumerable<string> DeclaredFunctionNamesOf(IrMethodDeclaration primary) =>
        primary.Semantics.ConversionName is null
            ? [FunctionNameOf(primary)]
            : new[] { primary }.Concat(primary.Overloads ?? []).Select(FunctionNameOf);

    /// <summary>
    /// Inline wrapper helpers lower to <c>export namespace TypeName { export
    /// function methodName() { … } }</c>. Function declarations inside a TS
    /// namespace can NOT use reserved words (<c>function new() {}</c> is
    /// illegal even though <c>obj.new</c> is fine), so this stays on the
    /// escaping naming policy. The call-site bridge detects [Branded]
    /// receivers and matches the escape so the two halves stay in sync.
    /// </summary>
    private static string FunctionNameOf(IrMethodDeclaration method)
    {
        if (method.Semantics.ConversionName is { } conversion)
            return TsOperatorNaming.ConversionHelperName(conversion);

        return TsOperatorNaming.OperatorNameOf(method) is { } operatorName
            ? TsOperatorNaming.StaticHelperName(operatorName)
            : IrToTsNamingPolicy.ToFunctionName(method);
    }

    /// <summary>
    /// The one scope the functions of a companion namespace share: the
    /// synthesized members and every static method's own name are taken
    /// before a fast path claims, and a fast path whose name is taken gets a
    /// <c>$</c> until it is free. A renamed fast path that is exported is
    /// reported (<c>MS0067</c>), as a class's is.
    /// </summary>
    private sealed class NameClaims
    {
        private readonly string _typeName;
        private readonly HashSet<string> _taken;
        private readonly List<TsClaimFinding> _findings = [];

        public NameClaims(
            string typeName,
            IEnumerable<string> synthesized,
            IEnumerable<IrMethodDeclaration> statics
        )
        {
            _typeName = typeName;

            _taken = synthesized
                .Concat(statics.SelectMany(DeclaredFunctionNamesOf))
                .ToHashSet(StringComparer.Ordinal);
        }

        public IReadOnlyList<TsClaimFinding> Findings => _findings;

        public string Claim(string candidate, IrMethodDeclaration overload)
        {
            var name = TsFreeName.FirstFree(candidate, _taken.Contains);

            _taken.Add(name);

            if (name != candidate)
                _findings.Add(RenameOf(candidate, name, overload));

            return name;
        }

        private TsClaimFinding RenameOf(
            string candidate,
            string claimed,
            IrMethodDeclaration overload
        )
        {
            var signature = DispatchedOverload.SignatureOf(overload);

            return new TsClaimFinding(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.OverloadNameShadowed,
                $"Fast path '{_typeName}.{candidate}' of '{signature}' is already taken in the "
                    + $"companion namespace; it is emitted as '{claimed}' (spec 013 FR-004).",
                new OverloadDeclarationSite(overload.Name, Signature: signature)
            );
        }
    }
}

/// <summary>
/// One static method group of a companion namespace, under the function name
/// its single method or its dispatcher takes.
/// </summary>
internal abstract record TsCompanionGroup(string FunctionName)
{
    /// <summary>The same group over the methods <paramref name="bound"/> maps its own to.</summary>
    public abstract TsCompanionGroup Over(Func<IrMethodDeclaration, IrMethodDeclaration> bound);
}

/// <summary>A group of one method: a single exported function.</summary>
internal sealed record TsCompanionFunction(string FunctionName, IrMethodDeclaration Method)
    : TsCompanionGroup(FunctionName)
{
    public override TsCompanionGroup Over(Func<IrMethodDeclaration, IrMethodDeclaration> bound) =>
        this with
        {
            Method = bound(Method),
        };
}

/// <summary>
/// A group of two or more overloads: an exported dispatcher plus one fast
/// path each, in dispatch order. Every fast path is exported, so a generated
/// call names the overload C# bound (an operator group's too, baseline FR-078).
/// </summary>
internal sealed record TsCompanionDispatcher(
    string FunctionName,
    IReadOnlyList<TsCompanionFastPath> FastPaths
) : TsCompanionGroup(FunctionName)
{
    /// <summary>The C# name the group's methods share.</summary>
    public string MemberName => FastPaths[0].Method.Name;

    public override TsCompanionGroup Over(Func<IrMethodDeclaration, IrMethodDeclaration> bound) =>
        this with
        {
            FastPaths = FastPaths
                .Select(fastPath => fastPath with { Method = bound(fastPath.Method) })
                .ToList(),
        };
}

/// <summary>One overload of a dispatched companion group and the name it claimed.</summary>
internal sealed record TsCompanionFastPath(IrMethodDeclaration Method, string Name);
