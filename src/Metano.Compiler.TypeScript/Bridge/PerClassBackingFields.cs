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
/// it, and a member initializer the constructor now runs (primary-constructor
/// initializer deferral) stores to the member's own storage whatever accessors a derived
/// class overrides: both lower to the backing field itself, found through
/// <see cref="EnterClass"/>. That scope is ambient because the expression bridge that maps
/// the assignment takes no per-class context.
/// </para>
/// <para>
/// <c>JSON.stringify</c> walks own enumerable properties, so it would expose the
/// mangled names. A class that gained a field therefore carries a <c>toJSON()</c>
/// (<see cref="BuildToJson"/>) that answers the public shape instead. The runtime
/// <c>publicJsonShape</c> recognizes a backing field by this naming scheme
/// (<c>&lt;property&gt;$&lt;Class&gt;_&lt;six base-36 characters&gt;</c>): change
/// <see cref="HashLength"/> or the separators together with its pattern.
/// </para>
/// </summary>
internal sealed class PerClassBackingFields
{
    private const string PublicJsonShapeFunction = "publicJsonShape";
    private const int HashLength = 6;
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    // 36^HashLength: the hash is reduced to HashLength base-36 characters.
    private const long Base36HashSpace = 36L * 36 * 36 * 36 * 36 * 36;

    private static readonly AsyncLocal<IReadOnlyDictionary<
        string,
        BackedProperty
    >?> ClassProperties = new();

    private readonly string _suffix;

    private PerClassBackingFields(string suffix) => _suffix = suffix;

    /// <summary>The backing fields of <paramref name="type"/>'s own auto-properties.</summary>
    public static PerClassBackingFields For(INamedTypeSymbol type) =>
        new($"{type.Name}_{StableHash(Identity(type))}");

    /// <summary>The property is emitted as a backing field plus accessors.</summary>
    public static bool DeclaresBackingField(IrPropertyDeclaration property) =>
        property is { Semantics.OwnsPerClassStorage: true }
        && property.Visibility is not IrVisibility.PrivateProtected;

    /// <summary>The field behind <paramref name="property"/>.</summary>
    public string FieldOf(IrPropertyDeclaration property) =>
        $"{IrToTsNamingPolicy.ToDeclaredDataMemberName(property.Name, property.Attributes)}${_suffix}";

    /// <summary><c>this.&lt;field&gt;</c>.</summary>
    public static TsPropertyAccess StorageAccess(string field) =>
        new(new TsIdentifier("this"), field);

    /// <summary>
    /// Scopes the emission of one class: while the returned handle is alive, the writes
    /// <see cref="TryBackingFieldWrite"/> recognizes lower to the backing fields of
    /// <paramref name="ir"/>'s properties.
    /// </summary>
    public IDisposable EnterClass(IrClassDeclaration ir)
    {
        var scope = new ClassScope(ClassProperties.Value);

        ClassProperties.Value = (ir.Members ?? [])
            .OfType<IrPropertyDeclaration>()
            .Where(DeclaresBackingField)
            .ToDictionary(
                property => property.Name,
                property => new BackedProperty(
                    FieldOf(property),
                    property.Accessors is IrPropertyAccessors.GetOnly
                ),
                StringComparer.Ordinal
            );

        return scope;
    }

    /// <summary>
    /// The backing field an assignment to <c>this.P</c> stores to inside the class being
    /// emitted: always for a get-only property (only the declaring constructor can assign
    /// it, and it has no setter), and for a writable one when the assignment is a moved
    /// member initializer (<see cref="IrMemberOrigin.IsInitializerWrite"/>). An explicit
    /// <c>P = x</c> on a writable property stays a virtual setter call, as in C#. <c>null</c>
    /// for every other access.
    /// </summary>
    public static string? TryBackingFieldWrite(IrMemberAccess access) =>
        access.Target is IrThisExpression
        && ClassProperties.Value is { } properties
        && properties.TryGetValue(access.MemberName, out var property)
        && (property.GetOnly || access.Origin is { IsInitializerWrite: true })
            ? property.Field
            : null;

    /// <summary>
    /// <c>toJSON(): unknown { return publicJsonShape(this); }</c> for a class that declares at
    /// least one backing field, <c>null</c> for any other, or for one that declares, or
    /// inherits one from an ancestor the compilation declares. The runtime function keeps the keys and values
    /// <c>JSON.stringify</c> produced while the property was a field (every own property of
    /// the instance, base classes included), renaming a backing field back to its property
    /// and reading the value through the accessor. The method overrides the one of an
    /// ancestor that declares a backing field, when the class extends it
    /// (<paramref name="extendsBase"/>).
    /// </summary>
    public static TsMethodMember? BuildToJson(IrClassDeclaration ir, bool extendsBase)
    {
        var members = ir.Members ?? [];

        if (!members.OfType<IrPropertyDeclaration>().Any(DeclaresBackingField))
            return null;

        if (ir.Semantics.DeclaresToJson)
            return null;

        return new TsMethodMember(
            "toJSON",
            [],
            new TsNamedType("unknown"),
            [
                new TsReturnStatement(
                    new TsTemplate(
                        $"{PublicJsonShapeFunction}($0)",
                        Receiver: null,
                        [new TsIdentifier("this")]
                    )
                    {
                        RuntimeImports = [PublicJsonShapeFunction],
                    }
                ),
            ],
            Override: extendsBase && ir.Semantics.InheritsPerClassStorage
        );
    }

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

    private sealed record BackedProperty(string Field, bool GetOnly);

    private sealed class ClassScope(IReadOnlyDictionary<string, BackedProperty>? previous)
        : IDisposable
    {
        public void Dispose() => ClassProperties.Value = previous;
    }
}
