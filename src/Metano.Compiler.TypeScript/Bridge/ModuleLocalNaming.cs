using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript naming policy of a nested type declared at module level: a
/// <c>private</c> one (#412), whose merged namespace member could not be hidden from
/// the owner, and one its container hoists out of the IIFE a namespace compiles to
/// (spec 011 FR-040, <see cref="Transformation.NestedTypeHoisting"/>). It is declared
/// under one identifier — the path of its containers up to itself joined by
/// <see cref="Separator"/> (<c>Outer$Inner</c>). The core IR keeps the dotted path and
/// only records how many leading segments lead to a private nested type; this class
/// spells references from that fact and from the module-level nested types of the
/// file being emitted (<see cref="FileScope"/>) through <see cref="Flatten"/>, and the
/// declaration through <see cref="Apply"/>, so they always agree.
/// </summary>
internal static class ModuleLocalNaming
{
    /// <summary>
    /// <c>$</c> is a legal TypeScript identifier character that no C# type name contains.
    /// </summary>
    public const string Separator = "$";

    /// <summary>
    /// The dotted qualified names of the nested types the file being emitted declares
    /// at module level. A reference from the same file reaches such a type by its
    /// module-local identifier, never through its owner; another file reaches it as
    /// <c>Owner.Inner</c>. <see cref="AsyncLocal{T}"/> keeps the slot per execution
    /// context, like the other per-file scopes of the transform.
    /// </summary>
    private static readonly AsyncLocal<IReadOnlySet<string>?> _fileScope = new();

    internal static IReadOnlySet<string>? FileScope
    {
        get => _fileScope.Value;
        set => _fileScope.Value = value;
    }

    /// <summary>
    /// Spells a dotted IR reference name for TypeScript: its leading segments that name
    /// a module-level declaration — the first <paramref name="privateNestedSegments"/>,
    /// or the longest prefix the file declares at module level — collapse into the
    /// module-local identifier, the rest stay dotted (<c>Outer$Inner.Leaf</c>).
    /// <see cref="FileScope"/> is matched by the dotted name, not by symbol: a reference
    /// from the file to another package's type with the same qualified name would
    /// collapse too, but its root name then binds both an import and the file's own
    /// owner, the same-name clash <c>MS0070</c> already rejects.
    /// </summary>
    public static string Flatten(string dottedName, int privateNestedSegments)
    {
        var segments = dottedName.Split('.');
        var collapsing = Math.Max(privateNestedSegments, FileScopePrefixLength(segments));

        if (collapsing < 2 || segments.Length < collapsing)
            return dottedName;

        var collapsed = string.Join(Separator, segments.Take(collapsing));

        return string.Join(".", segments.Skip(collapsing).Prepend(collapsed));
    }

    /// <summary>
    /// True when a dotted IR reference names a <c>private</c> nested type itself, which
    /// is declared module-local — every one of its segments leads to the private type.
    /// </summary>
    public static bool IsDeclaredModuleLocal(string dottedName, int privateNestedSegments) =>
        privateNestedSegments > 1 && privateNestedSegments == dottedName.Split('.').Length;

    /// <summary>
    /// Renames the IR declaration of a nested type declared at module level to its
    /// module-local identifier <paramref name="moduleLevelName"/>; <c>null</c> keeps the
    /// declaration. The bridges read the declared name from
    /// <see cref="IrTypeDeclaration.Name"/> plus its <c>[Name]</c> override, so the
    /// rename is applied once on the IR before any bridge sees it.
    /// </summary>
    public static IrTypeDeclaration Apply(IrTypeDeclaration declaration, string? moduleLevelName)
    {
        if (moduleLevelName is not { } name)
            return declaration;

        var attributes = (declaration.Attributes ?? [])
            .Where(attribute => attribute.Name != IrAttribute.NameOverrideAttributeName)
            .Append(IrAttribute.NameOverride(name))
            .ToList();

        return declaration switch
        {
            IrClassDeclaration cls => cls with { Name = name, Attributes = attributes },
            IrInterfaceDeclaration iface => iface with { Name = name, Attributes = attributes },
            IrEnumDeclaration enumeration => enumeration with
            {
                Name = name,
                Attributes = attributes,
            },
            IrDelegateDeclaration del => del with { Name = name, Attributes = attributes },
            _ => declaration,
        };
    }

    /// <summary>
    /// The length of the longest prefix of <paramref name="segments"/> that names a
    /// nested type of <see cref="FileScope"/>; <c>0</c> when none does.
    /// </summary>
    private static int FileScopePrefixLength(string[] segments)
    {
        if (segments.Length < 2 || FileScope is not { Count: > 0 } names)
            return 0;

        for (var length = segments.Length; length >= 2; length--)
        {
            if (names.Contains(string.Join(".", segments.Take(length))))
                return length;
        }

        return 0;
    }
}
