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
/// Every exception type is keyed by <see cref="BclExportReader.ExportKey"/>,
/// the key the extraction port, <see cref="IrNamedTypeSemantics.ClrTypeName"/>
/// and <see cref="IrExceptionConstruction.ExceptionTypeName"/> share.
/// </para>
/// <para>
/// Constructor roles: a class from the runtime's exception modules
/// (<see cref="RuntimeExceptionModules"/>) takes the roles of the
/// <c>metano-runtime</c> constructor for its CLR type, which mirror .NET's
/// parameter order (<c>RuntimeExceptionConstructorTests</c> compares them with
/// the runtime sources); any other mapped class, a project's own, takes
/// <c>(message, innerException)</c>.
/// </para>
/// </summary>
public sealed class BclExceptionClasses
{
    /// <summary>The key of the root of the exception hierarchy.</summary>
    public const string RootExceptionTypeName = "System.Exception";

    /// <summary>
    /// The module-specifier prefix of the <c>metano-runtime</c> exception
    /// classes, whose constructors <see cref="RuntimeConstructorRoles"/> describes.
    /// </summary>
    public const string RuntimeExceptionModules = "metano-runtime/system/exceptions/";

    private static readonly IReadOnlyList<IrExceptionArgumentRole> MessageAndInnerException =
    [
        IrExceptionArgumentRole.Message,
        IrExceptionArgumentRole.InnerException,
    ];

    /// <summary>
    /// The constructor roles of the runtime classes whose constructor is not
    /// <c>(message, innerException)</c>, keyed by the CLR type they mirror.
    /// </summary>
    private static readonly Dictionary<
        string,
        IReadOnlyList<IrExceptionArgumentRole>
    > RuntimeConstructorRolesByType = new(StringComparer.Ordinal)
    {
        ["System.ArgumentException"] =
        [
            IrExceptionArgumentRole.Message,
            IrExceptionArgumentRole.ParamName,
            IrExceptionArgumentRole.InnerException,
        ],
        ["System.ArgumentNullException"] =
        [
            IrExceptionArgumentRole.ParamName,
            IrExceptionArgumentRole.Message,
            IrExceptionArgumentRole.InnerException,
        ],
        ["System.ArgumentOutOfRangeException"] =
        [
            IrExceptionArgumentRole.ParamName,
            IrExceptionArgumentRole.ActualValue,
            IrExceptionArgumentRole.Message,
            IrExceptionArgumentRole.InnerException,
        ],
        ["System.Runtime.CompilerServices.SwitchExpressionException"] =
        [
            IrExceptionArgumentRole.UnmatchedValue,
            IrExceptionArgumentRole.Message,
            IrExceptionArgumentRole.InnerException,
        ],
        ["System.ObjectDisposedException"] =
        [
            IrExceptionArgumentRole.ObjectName,
            IrExceptionArgumentRole.Message,
            IrExceptionArgumentRole.InnerException,
        ],
        ["System.TypeInitializationException"] =
        [
            IrExceptionArgumentRole.TypeName,
            IrExceptionArgumentRole.InnerException,
        ],
    };

    private readonly IReadOnlyDictionary<string, IrBclExport> _exports;

    public BclExceptionClasses(IReadOnlyDictionary<string, IrBclExport> exports) =>
        _exports = exports;

    /// <summary>
    /// The class of <c>System.Exception</c>, which unmapped exceptions lower
    /// to. <c>Metano/Runtime/Exceptions.cs</c> maps it in every compilation
    /// that references the Metano annotations; its absence is an internal error.
    /// </summary>
    public TsExceptionClass Root =>
        Resolve(RootExceptionTypeName)
        ?? throw new InvalidOperationException(
            "Internal error: no [ExportFromBcl] maps System.Exception, so BCL exceptions have "
                + "no runtime class; the compilation must reference the Metano assembly."
        );

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
    public TsExceptionClass ClassOf(IrNamedTypeRef type) => ClassOf(KeyOf(type));

    /// <summary>
    /// The class of the exception type keyed <paramref name="exceptionTypeName"/>,
    /// or the <see cref="Root"/>.
    /// </summary>
    public TsExceptionClass ClassOf(string exceptionTypeName) => Resolve(exceptionTypeName) ?? Root;

    /// <summary>
    /// The runtime type test (<c>is</c>, <c>as</c>, a pattern, a dispatcher
    /// arm) of a mapped BCL exception: <c>instanceof</c> its class, or the
    /// built-in <c>Error</c> for <c>System.Exception</c> itself
    /// (<see cref="TsExceptionClass.InstanceofTest"/>). An unmapped type has no
    /// test (extraction reports MS0045, a dispatcher keeps its object check);
    /// reaching here with one is an internal error.
    /// </summary>
    public TsExpression InstanceofTest(TsExpression value, IrNamedTypeRef type) =>
        IsMapped(type)
            ? ClassOf(type).InstanceofTest(value)
            : throw new InvalidOperationException(
                $"Internal error: '{type.Name}' has no runtime class, so no type test can tell "
                    + "it apart; extraction must report MS0045 first."
            );

    /// <summary>Whether a BCL exception type has a mapped class of its own.</summary>
    public bool IsMapped(IrNamedTypeRef type) => IsMapped(KeyOf(type));

    /// <summary>
    /// Whether the exception type keyed <paramref name="exceptionTypeName"/>
    /// has a mapped class of its own.
    /// </summary>
    public bool IsMapped(string exceptionTypeName) => Resolve(exceptionTypeName) is not null;

    /// <summary>
    /// The extraction port: whether a non-transpilable exception type has a
    /// mapped class of its own, so a type test can tell it apart.
    /// </summary>
    public bool HasRuntimeClass(INamedTypeSymbol type) => IsMapped(BclExportReader.ExportKey(type));

    private TsExceptionClass? Resolve(string exceptionTypeName)
    {
        if (!_exports.TryGetValue(exceptionTypeName, out var export))
            return null;

        return new TsExceptionClass(
            export.ExportedName,
            OriginOf(export.FromPackage),
            ConstructorRolesOf(exceptionTypeName, export),
            IsRoot: exceptionTypeName == RootExceptionTypeName
        );
    }

    /// <summary>
    /// The roles of a mapped class's constructor: the runtime's for a class
    /// from its exception modules, <c>(message, innerException)</c> for any
    /// other class.
    /// </summary>
    private static IReadOnlyList<IrExceptionArgumentRole> ConstructorRolesOf(
        string exceptionTypeName,
        IrBclExport export
    ) =>
        export.FromPackage.StartsWith(RuntimeExceptionModules, StringComparison.Ordinal)
            ? RuntimeConstructorRoles(exceptionTypeName)
            : MessageAndInnerException;

    /// <summary>
    /// The roles the <c>metano-runtime</c> class mirroring the CLR type keyed
    /// <paramref name="exceptionTypeName"/> takes, in order.
    /// </summary>
    public static IReadOnlyList<IrExceptionArgumentRole> RuntimeConstructorRoles(
        string exceptionTypeName
    ) =>
        RuntimeConstructorRolesByType.GetValueOrDefault(
            exceptionTypeName,
            MessageAndInnerException
        );

    private static string KeyOf(IrNamedTypeRef type) =>
        type.Semantics?.ClrTypeName
        ?? throw new InvalidOperationException(
            $"Internal error: the exception type '{type.Name}' carries no export key."
        );

    /// <summary>
    /// The import origin of a class: its <c>FromPackage</c> module specifier
    /// as a whole (<c>metano-runtime/system/exceptions/exception</c>), as the
    /// type-origin resolver gives every <c>[ExportFromBcl]</c> type, so both
    /// paths name the same import; <c>null</c> when the entry names no module.
    /// </summary>
    private static TsTypeOrigin? OriginOf(string specifier) =>
        specifier.Length == 0 ? null : new TsTypeOrigin(specifier, SubPath: "");
}
