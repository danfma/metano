using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The one answer to "which JavaScript class does this BCL exception lower to"
/// (spec 011 FR-014), read by the type position, <c>extends</c>, construction,
/// the type tests and the extraction port
/// (<see cref="Metano.Compiler.Extraction.IrExtractionContext.HasExceptionRuntimeClass"/>).
/// <para>
/// It is fed by the <c>[ExportFromBcl]</c> entries in scope
/// (<c>Metano/Runtime/Exceptions.cs</c> ships one per <c>metano-runtime</c>
/// exception class): an exception type with an entry is <em>mapped</em> to the
/// entry's class, imported from its <c>FromPackage</c> module specifier. A BCL
/// exception without one is <em>unmapped</em>: it lowers to the class of
/// <c>System.Exception</c> (the <see cref="Root"/>), so a construction
/// still throws an <c>Exception</c>, and its type tests are reported (MS0045).
/// When nothing maps <c>System.Exception</c> either, the root is the built-in
/// <c>Error</c>.
/// </para>
/// <para>
/// The constructor roles of the runtime classes mirror .NET's parameter order:
/// <c>(message, innerException)</c>, except for the argument and disposal
/// families, whose own roles come first.
/// </para>
/// </summary>
public sealed class BclExceptionClasses
{
    /// <summary>The C# display name of the root of the exception hierarchy.</summary>
    public const string RootExceptionTypeName = "System.Exception";

    private static readonly IReadOnlyList<IrExceptionArgumentRole> MessageAndInnerException =
    [
        IrExceptionArgumentRole.Message,
        IrExceptionArgumentRole.InnerException,
    ];

    private static readonly Dictionary<
        string,
        IReadOnlyList<IrExceptionArgumentRole>
    > ConstructorRolesByClass = new(StringComparer.Ordinal)
    {
        ["ArgumentException"] =
        [
            IrExceptionArgumentRole.Message,
            IrExceptionArgumentRole.ParamName,
            IrExceptionArgumentRole.InnerException,
        ],
        ["ArgumentNullException"] =
        [
            IrExceptionArgumentRole.ParamName,
            IrExceptionArgumentRole.Message,
            IrExceptionArgumentRole.InnerException,
        ],
        ["ArgumentOutOfRangeException"] =
        [
            IrExceptionArgumentRole.ParamName,
            IrExceptionArgumentRole.ActualValue,
            IrExceptionArgumentRole.Message,
            IrExceptionArgumentRole.InnerException,
        ],
        ["ObjectDisposedException"] =
        [
            IrExceptionArgumentRole.ObjectName,
            IrExceptionArgumentRole.Message,
            IrExceptionArgumentRole.InnerException,
        ],
    };

    private readonly IReadOnlyDictionary<string, IrBclExport> _exports;

    public BclExceptionClasses(IReadOnlyDictionary<string, IrBclExport> exports)
    {
        _exports = exports;
        Root = Resolve(RootExceptionTypeName) ?? TsExceptionClass.BuiltInError;
    }

    /// <summary>
    /// The resolver with no mapping: every exception lowers to the built-in
    /// <c>Error</c> (a bridge driven outside a transform).
    /// </summary>
    public static BclExceptionClasses Empty { get; } = new(new Dictionary<string, IrBclExport>());

    /// <summary>
    /// The class of <c>System.Exception</c>, which unmapped exceptions lower to.
    /// </summary>
    public TsExceptionClass Root { get; }

    /// <summary>
    /// Whether <paramref name="type"/> is an exception without a TypeScript
    /// declaration of its own (<c>System.Exception</c> and its BCL or otherwise
    /// non-transpilable subclasses): the types this resolver answers for.
    /// </summary>
    public static bool IsBclException(IrNamedTypeRef type) =>
        type.Semantics is { Kind: IrNamedTypeKind.Exception, IsTranspilable: false };

    /// <summary>
    /// The class a BCL exception type lowers to: its mapped class, or the
    /// <see cref="Root"/> when it is unmapped.
    /// </summary>
    public TsExceptionClass ClassOf(IrNamedTypeRef type) => ClassOf(DisplayName(type));

    /// <summary>
    /// The class of the exception type whose C# display name is
    /// <paramref name="exceptionTypeName"/>, or the <see cref="Root"/>.
    /// </summary>
    public TsExceptionClass ClassOf(string exceptionTypeName) => Resolve(exceptionTypeName) ?? Root;

    /// <summary>
    /// Whether the exception type named <paramref name="exceptionTypeName"/>
    /// has a mapped class of its own.
    /// </summary>
    public bool IsMapped(string exceptionTypeName) => Resolve(exceptionTypeName) is not null;

    /// <summary>
    /// The extraction port: whether a non-transpilable exception type has a
    /// mapped class of its own, so a type test can tell it apart.
    /// </summary>
    public bool HasRuntimeClass(INamedTypeSymbol type) =>
        IsMapped(type.OriginalDefinition.ToDisplayString());

    private TsExceptionClass? Resolve(string exceptionTypeName)
    {
        if (!_exports.TryGetValue(exceptionTypeName, out var export))
            return null;

        return new TsExceptionClass(
            export.ExportedName,
            OriginOf(export.FromPackage),
            ConstructorRolesByClass.GetValueOrDefault(export.ExportedName, MessageAndInnerException)
        );
    }

    /// <summary>
    /// The C# display name of a named type reference (<c>System.ArgumentException</c>),
    /// the key <c>[ExportFromBcl]</c> entries are read under.
    /// </summary>
    private static string DisplayName(IrNamedTypeRef type) =>
        type.Namespace is { Length: > 0 } ns ? $"{ns}.{type.Name}" : type.Name;

    /// <summary>
    /// The import origin of a class: its <c>FromPackage</c> module specifier
    /// as a whole (<c>metano-runtime/system/exceptions/exception</c>), as the
    /// type-origin resolver gives every <c>[ExportFromBcl]</c> type, so both
    /// paths name the same import; <c>null</c> when the entry names no module.
    /// </summary>
    private static TsTypeOrigin? OriginOf(string specifier) =>
        specifier.Length == 0 ? null : new TsTypeOrigin(specifier, SubPath: "");
}
