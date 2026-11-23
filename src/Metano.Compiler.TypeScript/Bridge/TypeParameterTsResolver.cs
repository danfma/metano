using System.Globalization;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Analysis;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Where a reified type parameter's runtime descriptor lives in TypeScript
/// (spec 011 W5-5(b)): a method's trailing <c>T$type</c> parameter, a class
/// instance's <c>this.$typeArguments$&lt;Class&gt;[i]</c> slot, or a
/// constructor/field-initializer's <c>$typeArgs?.[i] ?? UnknownType</c>
/// parameter read.
/// </summary>
internal enum TsTypeParameterScopeKind
{
    Method,
    ClassSlot,
    CtorParam,
}

/// <summary>
/// One type parameter binding in the active TypeScript scope.
/// </summary>
internal sealed record TsTypeParameterBinding(
    string Name,
    TsTypeParameterScopeKind Scope,
    int Position,
    string? HiddenParameterName = null,
    string? SlotName = null,
    string TypeArgsParameterName = IrToTsClassBridge.TypeArgsParameterName
);

/// <summary>
/// Active scope mapping C# type parameter names to their runtime descriptor
/// bindings in TypeScript (spec 011 W5-5(g)).
/// </summary>
internal sealed class TsTypeParameterScope
{
    private readonly IReadOnlyDictionary<string, TsTypeParameterBinding> _bindings;

    public static readonly TsTypeParameterScope Empty = new(
        new Dictionary<string, TsTypeParameterBinding>(StringComparer.Ordinal)
    );

    private TsTypeParameterScope(IReadOnlyDictionary<string, TsTypeParameterBinding> bindings) =>
        _bindings = bindings;

    /// <summary>
    /// Creates a scope from explicit <paramref name="bindings"/>; later entries
    /// shadow earlier ones with the same type parameter name.
    /// </summary>
    public static TsTypeParameterScope FromBindings(
        IEnumerable<TsTypeParameterBinding> bindings,
        TsTypeParameterScope? enclosing = null
    )
    {
        var map = enclosing is not null
            ? new Dictionary<string, TsTypeParameterBinding>(
                enclosing._bindings,
                StringComparer.Ordinal
            )
            : new Dictionary<string, TsTypeParameterBinding>(StringComparer.Ordinal);

        foreach (var binding in bindings)
            map[binding.Name] = binding;

        return map.Count == 0 ? Empty : new TsTypeParameterScope(map);
    }

    /// <summary>
    /// Binds a class's full-arity type parameters to its instance slot
    /// (<c>this.$typeArguments$&lt;Class&gt;[i]</c>).
    /// </summary>
    public static TsTypeParameterScope ForClassSlot(
        string slotName,
        IReadOnlyList<string> typeParameterNames
    ) =>
        FromBindings(
            typeParameterNames.Select(
                (name, index) =>
                    new TsTypeParameterBinding(
                        name,
                        TsTypeParameterScopeKind.ClassSlot,
                        index,
                        SlotName: slotName
                    )
            )
        );

    /// <summary>
    /// Binds a class's full-arity type parameters to the constructor's
    /// <c>$typeArgs</c> parameter (<c>$typeArgs?.[i] ?? UnknownType</c>).
    /// </summary>
    public static TsTypeParameterScope ForCtorParam(
        IReadOnlyList<string> typeParameterNames,
        string typeArgsParameterName = IrToTsClassBridge.TypeArgsParameterName
    ) =>
        FromBindings(
            typeParameterNames.Select(
                (name, index) =>
                    new TsTypeParameterBinding(
                        name,
                        TsTypeParameterScopeKind.CtorParam,
                        index,
                        TypeArgsParameterName: typeArgsParameterName
                    )
            )
        );

    /// <summary>
    /// Looks up the binding for <paramref name="typeParameterName"/>, or
    /// <c>null</c> when the type parameter is not reified in this scope.
    /// </summary>
    public TsTypeParameterBinding? TryGet(string typeParameterName) =>
        _bindings.TryGetValue(typeParameterName, out var binding) ? binding : null;
}

/// <summary>
/// Single scope-aware <c>TypeParameter -&gt; TS</c> resolver (spec 011 W5-5(g)).
/// Lowers a reified type parameter reference according to its scope token
/// (<see cref="TsTypeParameterScopeKind"/>) and resolves nested type
/// references containing reified type parameters (e.g. <c>List&lt;T&gt;</c>,
/// <c>Box&lt;T&gt;</c>).
/// </summary>
internal static class TypeParameterTsResolver
{
    /// <summary>
    /// Resolves <paramref name="typeParameterName"/> in <paramref name="scope"/>,
    /// returning <c>null</c> when the type parameter is not reified in scope.
    /// </summary>
    public static TsExpression? ResolveParameter(
        string typeParameterName,
        TsTypeParameterScope? scope
    )
    {
        if (scope?.TryGet(typeParameterName) is not { } binding)
            return null;

        return binding.Scope switch
        {
            TsTypeParameterScopeKind.Method => new TsIdentifier(
                binding.HiddenParameterName ?? $"{binding.Name}$type"
            ),
            TsTypeParameterScopeKind.ClassSlot => new TsElementAccess(
                new TsPropertyAccess(
                    new TsIdentifier("this"),
                    binding.SlotName
                        ?? throw new InvalidOperationException(
                            $"ClassSlot binding for '{binding.Name}' is missing a slot name."
                        )
                ),
                new TsLiteral(binding.Position.ToString(CultureInfo.InvariantCulture))
            ),
            TsTypeParameterScopeKind.CtorParam => DefaultingTypeArg(
                binding.Position,
                binding.TypeArgsParameterName
            ),
            _ => null,
        };
    }

    /// <summary>
    /// Resolves <paramref name="typeParameter"/> using <paramref name="scope"/>
    /// when present, or the IR scope metadata carried on the reference itself,
    /// returning <c>null</c> when the type parameter is not reified.
    /// </summary>
    public static TsExpression? Resolve(
        IrTypeParameterRef typeParameter,
        TsTypeParameterScope? scope = null
    )
    {
        if (ResolveParameter(typeParameter.Name, scope) is { } scoped)
            return scoped;

        return typeParameter.Scope switch
        {
            IrTypeParameterScope.Method => new TsIdentifier($"{typeParameter.Name}$type"),
            IrTypeParameterScope.ClassSlot => new TsElementAccess(
                new TsPropertyAccess(
                    new TsIdentifier("this"),
                    typeParameter.DeclaringTypeName
                        ?? throw new InvalidOperationException(
                            $"ClassSlot reference for '{typeParameter.Name}' is missing a slot name."
                        )
                ),
                new TsLiteral(typeParameter.SlotIndex.ToString(CultureInfo.InvariantCulture))
            ),
            IrTypeParameterScope.CtorParam => DefaultingTypeArg(typeParameter.SlotIndex),
            _ => null,
        };
    }

    /// <summary>
    /// Resolves <paramref name="type"/> to its TypeScript runtime descriptor
    /// expression in <paramref name="scope"/>, or <c>null</c> when
    /// <paramref name="type"/> has no descriptor (an un-reified type parameter
    /// or an undescribable type).
    /// </summary>
    public static TsExpression? ResolveTypeRef(
        IrTypeRef type,
        string site,
        RuntimeTypeReachabilityResult reachability,
        TsTypeParameterScope? scope = null
    ) => IrToTsRuntimeTypeReferences.Of(type, site, reachability, scope);

    /// <summary>
    /// Resolves <paramref name="type"/> as a generic/slot argument descriptor in
    /// <paramref name="scope"/>, falling back to <c>UnknownType</c> when it
    /// has no descriptor.
    /// </summary>
    public static TsExpression ResolveArgument(
        IrTypeRef type,
        string site,
        RuntimeTypeReachabilityResult reachability,
        TsTypeParameterScope? scope = null
    ) => IrToTsRuntimeTypeReferences.ReifiedArgumentOf(type, site, reachability, scope);

    /// <summary>
    /// <c>($typeArgs?.[i] ?? UnknownType)</c>: reads a constructor's optional
    /// <c>$typeArgs</c> tuple element with <c>UnknownType</c> as the fallback
    /// when omitted (spec 011 W5-5(i)).
    /// </summary>
    public static TsExpression DefaultingTypeArg(
        int index,
        string typeArgsParameterName = IrToTsClassBridge.TypeArgsParameterName
    ) =>
        new TsParenthesized(
            new TsBinaryExpression(
                new TsElementAccess(
                    new TsIdentifier(typeArgsParameterName),
                    new TsLiteral(index.ToString(CultureInfo.InvariantCulture)),
                    Optional: true
                ),
                "??",
                new TsRuntimeHelperReference("UnknownType")
            )
        );
}
