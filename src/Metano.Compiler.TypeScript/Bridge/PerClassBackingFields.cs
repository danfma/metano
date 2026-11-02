using System.Text;
using Metano.Annotations;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The private backing fields that keep an overridable auto-property's storage apart
/// per class (<see cref="IrPropertySemantics.OwnsPerClassStorage"/>). A plain class
/// field is one own instance property for the whole hierarchy, so a base field would
/// shadow a derived accessor (TS2611) and <c>super.p</c> could not reach the base
/// value; the property becomes an accessor over a field named
/// <c>&lt;property&gt;$&lt;Class&gt;_&lt;hash&gt;</c> instead. The hash is a stable
/// base36 FNV-1a of <c>&lt;package&gt;:&lt;namespace&gt;.&lt;class&gt;</c> (including the
/// generic arity), so two classes with the same name in different namespaces never
/// share a field, and the name is the same on every run and machine.
/// <para>
/// A get-only auto-property has no setter, yet C# lets the declaring constructor assign
/// it (and the primary-constructor initializer deferral does): those writes lower to the
/// field itself, found through <see cref="EnterClass"/> while the class is emitted.
/// </para>
/// <para>
/// <c>JSON.stringify</c> walks own enumerable properties, so it would expose the
/// mangled names. A class that gained a field therefore carries a <c>toJSON()</c>
/// (<see cref="BuildJsonShape"/>) that answers the public shape instead.
/// </para>
/// </summary>
internal sealed class PerClassBackingFields
{
    private const string JsonShapeHelper = "publicJsonShape";
    private const int HashLength = 6;
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;
    private const long Base36HashSpace = 2176782336;

    private static readonly AsyncLocal<Dictionary<string, string>> ReadOnlyWriteTargets = new();

    private readonly string _suffix;

    private PerClassBackingFields(string suffix) => _suffix = suffix;

    /// <summary>The backing fields of <paramref name="type"/>'s own auto-properties.</summary>
    public static PerClassBackingFields For(INamedTypeSymbol type) =>
        new($"{type.Name}_{StableHash(Identity(type))}");

    /// <summary>The field behind the property emitted as <paramref name="emittedPropertyName"/>.</summary>
    public string NameOf(string emittedPropertyName) => $"{emittedPropertyName}${_suffix}";

    /// <summary>
    /// Scopes the emission of one class: while the returned handle is alive, an assignment
    /// to a get-only property of <paramref name="ir"/> that owns per-class storage lowers
    /// to its backing field (see <see cref="TryReadOnlyWriteTarget"/>).
    /// </summary>
    public IDisposable EnterClass(IrClassDeclaration ir)
    {
        var scope = new ClassScope(ReadOnlyWriteTargets.Value);

        ReadOnlyWriteTargets.Value = ReadOnlyOwners(ir)
            .ToDictionary(
                property => property.Name,
                property =>
                    NameOf(
                        IrToTsNamingPolicy.ToDeclaredDataMemberName(
                            property.Name,
                            property.Attributes
                        )
                    ),
                StringComparer.Ordinal
            );

        return scope;
    }

    /// <summary>
    /// The backing field a <c>this.P = value</c> write inside the class being emitted
    /// stores to, when <c>P</c> is a get-only auto-property with per-class storage;
    /// <c>null</c> for every other access.
    /// </summary>
    public static string? TryReadOnlyWriteTarget(IrMemberAccess access) =>
        access.Target is IrThisExpression
        && ReadOnlyWriteTargets.Value is { } targets
        && targets.TryGetValue(access.MemberName, out var field)
            ? field
            : null;

    /// <summary>
    /// <c>toJSON(): unknown { return publicJsonShape(this); }</c> for a class that declares at
    /// least one backing field, <c>null</c> for any other. The runtime helper keeps the keys and
    /// values <c>JSON.stringify</c> produced while the property was a field (every own
    /// property of the instance, base classes included), renaming a backing field back to its
    /// property and reading the value through the accessor. A class whose ancestor already
    /// declares one overrides that ancestor's method.
    /// </summary>
    public static TsMethodMember? BuildJsonShape(IrClassDeclaration ir) =>
        (ir.Members ?? []).OfType<IrPropertyDeclaration>().Any(DeclaresBackingField)
            ? new TsMethodMember(
                "toJSON",
                [],
                new TsNamedType("unknown"),
                [
                    new TsReturnStatement(
                        new TsTemplate(
                            $"{JsonShapeHelper}($0)",
                            Receiver: null,
                            [new TsIdentifier("this")]
                        )
                        {
                            RuntimeImports = [JsonShapeHelper],
                        }
                    ),
                ],
                Override: ir.Semantics.InheritsPerClassStorage
            )
            : null;

    /// <summary>The property is emitted as a backing field plus accessors.</summary>
    public static bool DeclaresBackingField(IrPropertyDeclaration property) =>
        property is { Semantics.OwnsPerClassStorage: true }
        && property.Visibility is not IrVisibility.PrivateProtected;

    private static IEnumerable<IrPropertyDeclaration> ReadOnlyOwners(IrClassDeclaration ir) =>
        (ir.Members ?? [])
            .OfType<IrPropertyDeclaration>()
            .Where(property =>
                property.Accessors is IrPropertyAccessors.GetOnly
                && property.Semantics?.OwnsPerClassStorage == true
            );

    private static string Identity(INamedTypeSymbol type) =>
        $"{PackageIdentity(type.ContainingAssembly)}:{type.ContainingNamespace.ToDisplayString()}.{MetadataPath(type)}";

    private static string PackageIdentity(IAssemblySymbol assembly) =>
        SymbolHelper.GetEmitPackage(assembly, (int)EmitTarget.JavaScript) ?? assembly.Name;

    /// <summary>Outer-to-inner metadata names, whose backtick suffix carries the generic arity.</summary>
    private static string MetadataPath(INamedTypeSymbol type) =>
        type.ContainingType is { } outer
            ? $"{MetadataPath(outer)}.{type.MetadataName}"
            : type.MetadataName;

    private static string StableHash(string identity)
    {
        var hash = FnvOffsetBasis;

        foreach (var octet in Encoding.UTF8.GetBytes(identity))
            hash = unchecked((hash ^ octet) * FnvPrime);

        return ToBase36(hash % Base36HashSpace).PadLeft(HashLength, '0');
    }

    private static string ToBase36(long value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";

        if (value == 0)
            return "0";

        var result = new StringBuilder();

        for (; value > 0; value /= 36)
            result.Insert(0, digits[(int)(value % 36)]);

        return result.ToString();
    }

    private sealed class ClassScope(Dictionary<string, string>? previous) : IDisposable
    {
        public void Dispose() => ReadOnlyWriteTargets.Value = previous!;
    }
}
