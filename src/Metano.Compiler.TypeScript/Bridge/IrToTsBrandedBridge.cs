using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers an <see cref="IrClassDeclaration"/> whose
/// <see cref="IrTypeSemantics.IsBranded"/> is set into the canonical
/// TypeScript brand-type + companion-namespace shape:
/// <code>
/// export type UserId = string &amp; { readonly __brand: "UserId" };
/// export namespace UserId {
///     export function create(value: string): UserId { return value as UserId; }
///     // toString(value) only when the wrapped primitive isn't already string
///     // ...plus any user-declared static methods on the struct
/// }
/// </code>
/// Returns <c>false</c> when the IR doesn't describe a wrapper (no
/// <c>IsBranded</c> flag, or a missing <c>BrandedUnderlyingType</c>),
/// so the caller can fall through to the next per-shape handler.
/// </summary>
public static class IrToTsBrandedBridge
{
    /// <summary>The non-validating factory every branded companion synthesizes.</summary>
    internal const string CreateMember = "create";

    /// <summary>The string conversion a non-string branded companion synthesizes.</summary>
    internal const string ToStringMember = "toString";

    public static bool Convert(
        IrClassDeclaration ir,
        List<TsTopLevel> sink,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        // The brand-type pattern only makes sense over a primitive — anything
        // else can't be intersected with `{ readonly __brand: … }` and stay
        // assignable. Bail to the regular class/record path in that case. The
        // extractor gates the `id.Value` → `id` collapse on the same rule.
        if (CompanionPrimitiveOf(ir) is not { } primitiveType)
            return false;

        var tsTypeName = IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);

        // export type UserId = string & { readonly __brand: "UserId" };
        var brandType = new TsNamedType($"{{ readonly __brand: \"{tsTypeName}\" }}");
        sink.Add(new TsTypeAlias(tsTypeName, new TsIntersectionType([primitiveType, brandType])));

        var functions = new List<TsFunction>
        {
            // create(value: T): TypeName  →  return value as TypeName;
            new(
                CreateMember,
                [new TsParameter("value", primitiveType)],
                new TsNamedType(tsTypeName),
                [
                    new TsReturnStatement(
                        new TsCastExpression(new TsIdentifier("value"), new TsNamedType(tsTypeName))
                    ),
                ],
                Exported: true
            ),
        };

        // toString(value: TypeName): string — only when the wrapped primitive
        // isn't already a string (otherwise it'd just be the identity).
        if (primitiveType is not TsStringType)
        {
            functions.Add(
                new TsFunction(
                    ToStringMember,
                    [new TsParameter("value", new TsNamedType(tsTypeName))],
                    new TsStringType(),
                    [
                        new TsReturnStatement(
                            new TsCallExpression(
                                new TsIdentifier("String"),
                                [new TsIdentifier("value")]
                            )
                        ),
                    ],
                    Exported: true
                )
            );
        }

        // User-declared static methods on the wrapper struct. Visibility +
        // [Ignore] + [Emit] filtering already happened in the extractor; we
        // just emit each remaining static method (or overload group) as
        // exported functions in the companion namespace.
        var takenNames = TakenNamesOf(ir, primitiveType);

        foreach (var group in StaticGroupsOf(ir))
            functions.AddRange(ConvertStaticMethodGroup(group, takenNames, bclRegistry));

        sink.Add(new TsNamespaceDeclaration(tsTypeName, functions));
        return true;
    }

    /// <summary>
    /// The static overload groups of a branded companion that go through a
    /// dispatcher: two or more surviving methods, in member order. Empty for
    /// a type that does not lower to a companion namespace.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<IrMethodDeclaration>> DispatchedGroupsOf(
        IrClassDeclaration ir
    ) =>
        CompanionPrimitiveOf(ir) is null
            ? []
            : StaticGroupsOf(ir).Where(group => group.Count > 1).ToList();

    /// <summary>
    /// The fast paths a companion namespace exports for generated call sites
    /// (#569), by overload identity: every overload of a dispatched group
    /// whose dispatcher is ambiguous (<see cref="IsDirectlyBound"/>), named
    /// as <see cref="Convert"/> names it. A call C# binds to one of them
    /// calls it directly; every other group keeps the dispatcher call (spec
    /// 013 DECIDED-10).
    /// </summary>
    internal static IReadOnlyDictionary<TsMethodOverloadKey, string> DirectFastPathsOf(
        IrClassDeclaration ir
    )
    {
        var direct = new Dictionary<TsMethodOverloadKey, string>();

        if (CompanionPrimitiveOf(ir) is not { } primitiveType)
            return direct;

        var takenNames = TakenNamesOf(ir, primitiveType);

        foreach (var group in DispatchedGroupsOf(ir))
        {
            // Every group claims, so the names match the emission's order.
            var claimed = IrToTsOverloadDispatcherBridge.ClaimNamespaceFastPaths(
                group,
                FunctionName(group[0]),
                takenNames
            );

            if (!IsDirectlyBound(group))
                continue;

            foreach (var (overload, fastPath) in claimed)
            {
                var key = new TsMethodOverloadKey(overload.Name, overload.DeclarationOrdinal);

                direct[key] = fastPath;
            }
        }

        return direct;
    }

    /// <summary>
    /// The TS primitive a branded wrapper lowers over; <c>null</c> when the
    /// type is not branded or wraps something a brand cannot intersect, so it
    /// has no companion namespace.
    /// </summary>
    private static TsType? CompanionPrimitiveOf(IrClassDeclaration ir) =>
        ir.Semantics is { IsBranded: true, BrandedUnderlyingType: { } underlying }
        && underlying.IsBrandablePrimitive()
            ? IrToTsTypeMapper.Map(underlying)
            : null;

    /// <summary>
    /// Whether generated call sites address the group's overloads directly:
    /// the companion dispatcher has no per-overload entry a C# call could
    /// use, so a group it cannot tell apart at runtime (the <c>MS0063</c>
    /// groups) would run another overload than the one C# binds.
    /// </summary>
    private static bool IsDirectlyBound(IReadOnlyList<IrMethodDeclaration> group) =>
        FunctionOverloadShape.AreAmbiguous(group.Select(FunctionOverloadShape.Of).ToList());

    /// <summary>
    /// Each static method with the overload siblings folded onto
    /// <see cref="IrMethodDeclaration.Overloads"/>, in member order. Identity
    /// unwrap conversions are filtered (see <see cref="IsIdentityUnwrap"/>),
    /// which can leave a group empty.
    /// </summary>
    private static List<List<IrMethodDeclaration>> StaticGroupsOf(IrClassDeclaration ir) =>
        StaticMethodsOf(ir)
            .Select(primary =>
                new[] { primary }
                    .Concat(primary.Overloads ?? [])
                    .Where(method => !IsIdentityUnwrap(method, ir.Semantics))
                    .ToList()
            )
            .ToList();

    private static IEnumerable<IrMethodDeclaration> StaticMethodsOf(IrClassDeclaration ir) =>
        (ir.Members ?? []).OfType<IrMethodDeclaration>().Where(method => method.IsStatic);

    /// <summary>
    /// Every function name the companion namespace holds before a dispatcher
    /// claims its fast paths: the synthesized members and the static methods.
    /// </summary>
    private static HashSet<string> TakenNamesOf(IrClassDeclaration ir, TsType primitiveType)
    {
        // toString is synthesized unless the wrapped primitive is a string.
        IEnumerable<string> synthesized =
            primitiveType is TsStringType ? [CreateMember] : [CreateMember, ToStringMember];

        return synthesized
            .Concat(StaticMethodsOf(ir).Select(FunctionName))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Lowers one static group. A single surviving method becomes one
    /// function; two or more go through the shared overload dispatcher so no
    /// sibling is silently dropped.
    /// </summary>
    /// <param name="takenNames">
    /// Every function name already claimed in the companion namespace; the
    /// dispatcher's namespace-local fast paths are disambiguated against it
    /// and added to it.
    /// </param>
    private static IReadOnlyList<TsFunction> ConvertStaticMethodGroup(
        IReadOnlyList<IrMethodDeclaration> group,
        ISet<string> takenNames,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        group.Count switch
        {
            0 => [],
            1 => [ConvertStaticMethod(group[0], bclRegistry)],
            _ => IrToTsOverloadDispatcherBridge.BuildNamespaceFunctions(
                group,
                FunctionName(group[0]),
                takenNames,
                bclRegistry,
                exportsFastPaths: IsDirectlyBound(group)
            ),
        };

    /// <summary>
    /// A conversion operator from the wrapper to its underlying primitive
    /// (<c>implicit operator string(UserId self) => self.Value</c>, or its
    /// nullable-annotated <c>string?</c> form) is the identity once the
    /// wrapper lowers to <c>primitive &amp; { __brand }</c>, and call sites
    /// never invoke it — the companion skips it. Wrap-direction conversions
    /// (<c>explicit operator UserId(string raw)</c>) return the wrapper, not
    /// the primitive, so they are kept: they can carry validation.
    /// </summary>
    private static bool IsIdentityUnwrap(IrMethodDeclaration method, IrTypeSemantics semantics) =>
        method.Semantics is { IsOperator: true, OperatorKind: "Implicit" or "Explicit" }
        && StripNullable(method.ReturnType).Equals(semantics.BrandedUnderlyingType);

    private static IrTypeRef StripNullable(IrTypeRef type) =>
        type is IrNullableTypeRef nullable ? nullable.Inner : type;

    /// <summary>
    /// Inline wrapper helpers lower to <c>export namespace TypeName { export
    /// function methodName() { … } }</c>. Function declarations inside a TS
    /// namespace can NOT use reserved words (<c>function new() {}</c> is
    /// illegal even though <c>obj.new</c> is fine), so this stays on the
    /// escaping naming policy. The call-site bridge detects [Branded]
    /// receivers and matches the escape so the two halves stay in sync.
    /// </summary>
    private static string FunctionName(IrMethodDeclaration method) =>
        IrToTsNamingPolicy.ToFunctionName(method);

    private static TsFunction ConvertStaticMethod(
        IrMethodDeclaration method,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        new(
            FunctionName(method),
            IrToTsOverloadDispatcherBridge.MapParameters(method),
            IrToTsTypeMapper.Map(method.ReturnType),
            IrToTsBodyHelpers.LowerOrNotImplemented(
                method.Body,
                method.Name,
                IrToTsBridgeContext.For(bclRegistry)
            ),
            Exported: true,
            Async: method.Semantics.IsAsync
        );
}
