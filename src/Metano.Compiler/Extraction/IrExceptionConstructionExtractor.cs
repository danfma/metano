using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Builds the <see cref="IrExceptionConstruction"/> fact for a call to the
/// constructor of an exception type that has no emitted target declaration
/// (a BCL exception, or one from another non-transpilable assembly). Each
/// argument is classified by the role of the constructor parameter it binds
/// to — named arguments by name, positional ones by index — so the target
/// lowering is independent of argument order and overload shape.
/// <para>
/// The default messages mirror what .NET 10 reports for a parameterless
/// construction (verified against the runtime). <c>System.Exception</c>
/// itself reports <c>"Exception of type 'X' was thrown."</c>, where .NET names
/// the runtime most-derived type; a static lowering names the class whose
/// constructor makes the call instead, formatted like .NET's
/// <c>Type.ToString()</c> (<c>Outer+Inner</c>, <c>Box`1[T]</c>).
/// </para>
/// </summary>
public static class IrExceptionConstructionExtractor
{
    private const string SystemExceptionName = "System.Exception";
    private const string MessageParameterName = "message";

    private static readonly Dictionary<string, string> DefaultMessages = new()
    {
        ["System.SystemException"] = "System error.",
        ["System.ApplicationException"] = "Error in the application.",
        ["System.ArgumentException"] = "Value does not fall within the expected range.",
        ["System.ArgumentNullException"] = "Value cannot be null.",
        ["System.ArgumentOutOfRangeException"] =
            "Specified argument was out of the range of valid values.",
        ["System.InvalidOperationException"] =
            "Operation is not valid due to the current state of the object.",
        ["System.NotSupportedException"] = "Specified method is not supported.",
        ["System.NotImplementedException"] = "The method or operation is not implemented.",
        ["System.FormatException"] = "One of the identified items was in an invalid format.",
        ["System.Collections.Generic.KeyNotFoundException"] =
            "The given key was not present in the dictionary.",
        ["System.DivideByZeroException"] = "Attempted to divide by zero.",
        ["System.NullReferenceException"] = "Object reference not set to an instance of an object.",
        ["System.TimeoutException"] = "The operation has timed out.",
        ["System.OperationCanceledException"] = "The operation was canceled.",
        ["System.IndexOutOfRangeException"] = "Index was outside the bounds of the array.",
        ["System.InvalidCastException"] = "Specified cast is not valid.",
        ["System.OverflowException"] = "Arithmetic operation resulted in an overflow.",
        ["System.ArithmeticException"] = "Overflow or underflow in the arithmetic operation.",
        ["System.UnauthorizedAccessException"] = "Attempted to perform an unauthorized operation.",
        ["System.ObjectDisposedException"] = "Cannot access a disposed object.",
    };

    /// <summary>
    /// Classifies <paramref name="arguments"/> passed to
    /// <paramref name="constructor"/>. Returns <c>null</c> when the
    /// constructor is unresolved or belongs to a type that is not an
    /// exception without a target declaration — those constructions keep
    /// their ordinary lowering.
    /// </summary>
    /// <param name="constructor">The exception constructor being invoked.</param>
    /// <param name="arguments">The arguments, positional or named, as they
    /// appear in the owning IR argument list.</param>
    /// <param name="callingType">The class whose code performs the
    /// construction: the exception itself for <c>new</c>, the derived user
    /// class for a base call. It names <c>System.Exception</c>'s default
    /// message and decides whether <c>Message</c> is overridden.</param>
    /// <param name="target">The active backend, for target-scoped <c>[Ignore]</c>.</param>
    /// <param name="sourceArguments">The source argument list, used with
    /// <paramref name="model"/> to read the message argument's nullable flow
    /// state; <c>null</c> for an implicit base call.</param>
    /// <param name="model">The semantic model of <paramref name="sourceArguments"/>.</param>
    public static IrExceptionConstruction? Extract(
        IMethodSymbol? constructor,
        IReadOnlyList<IrArgument> arguments,
        INamedTypeSymbol callingType,
        Metano.Annotations.TargetLanguage? target,
        ArgumentListSyntax? sourceArguments = null,
        SemanticModel? model = null
    )
    {
        if (constructor is null || !LacksTargetDeclaration(constructor.ContainingType, target))
            return null;

        var roles = arguments
            .Select((argument, index) => RoleOf(BoundParameter(constructor, argument, index)))
            .ToList();

        return new IrExceptionConstruction(
            roles,
            DefaultMessageOf(constructor.ContainingType, callingType),
            MessageMayBeNull: MessageMayBeNull(sourceArguments, model),
            IsMessageOverridden: OverridesMessage(callingType, target)
        );
    }

    private static bool LacksTargetDeclaration(
        INamedTypeSymbol exceptionType,
        Metano.Annotations.TargetLanguage? target
    ) =>
        IrTypeRefMapper.IsExceptionType(exceptionType)
        && !IrTypeRefMapper.IsTranspilableType(exceptionType, target);

    private static IParameterSymbol? BoundParameter(
        IMethodSymbol constructor,
        IrArgument argument,
        int index
    )
    {
        if (argument.Name is { } name)
            return constructor.Parameters.FirstOrDefault(parameter => parameter.Name == name);

        return index < constructor.Parameters.Length ? constructor.Parameters[index] : null;
    }

    private static IrExceptionArgumentRole RoleOf(IParameterSymbol? parameter) =>
        parameter?.Name switch
        {
            MessageParameterName => IrExceptionArgumentRole.Message,
            "innerException" => IrExceptionArgumentRole.InnerException,
            "paramName" => IrExceptionArgumentRole.ParamName,
            "actualValue" => IrExceptionArgumentRole.ActualValue,
            "objectName" => IrExceptionArgumentRole.ObjectName,
            _ => IrExceptionArgumentRole.Unmapped,
        };

    private static bool MessageMayBeNull(ArgumentListSyntax? sourceArguments, SemanticModel? model)
    {
        if (sourceArguments is null || model is null)
            return false;

        var messageArgument = sourceArguments.Arguments.FirstOrDefault(argument =>
            model.GetOperation(argument)
                is IArgumentOperation { Parameter.Name: MessageParameterName }
        );

        return messageArgument is not null
            && model.GetTypeInfo(messageArgument.Expression).Nullability.FlowState
                is NullableFlowState.MaybeNull;
    }

    /// <summary>
    /// Whether <paramref name="callingType"/> or one of its transpilable bases
    /// overrides <c>Exception.Message</c>. Bases without a target declaration
    /// (the BCL) never count — their message is the native one.
    /// </summary>
    private static bool OverridesMessage(
        INamedTypeSymbol callingType,
        Metano.Annotations.TargetLanguage? target
    )
    {
        for (
            var current = callingType;
            current is not null && IrTypeRefMapper.IsTranspilableType(current, target);
            current = current.BaseType
        )
        {
            if (DeclaresMessageOverride(current))
                return true;
        }

        return false;
    }

    private static bool DeclaresMessageOverride(INamedTypeSymbol type) =>
        type.GetMembers("Message").OfType<IPropertySymbol>().Any(property => property.IsOverride);

    private static string? DefaultMessageOf(
        INamedTypeSymbol exceptionType,
        INamedTypeSymbol callingType
    )
    {
        var exceptionName = exceptionType.ToDisplayString();

        if (exceptionName == SystemExceptionName)
            return $"Exception of type '{DotNetTypeName(callingType)}' was thrown.";

        return DefaultMessages.GetValueOrDefault(exceptionName);
    }

    /// <summary>
    /// Formats a type the way .NET's <c>Type.ToString()</c> does: nested types
    /// joined with <c>+</c>, generic arity as <c>`N</c> followed by the type
    /// arguments in brackets.
    /// </summary>
    private static string DotNetTypeName(INamedTypeSymbol type)
    {
        var typeArguments =
            type.TypeArguments.Length > 0
                ? "[" + string.Join(",", type.TypeArguments.Select(t => t.ToDisplayString())) + "]"
                : "";

        return QualifiedMetadataName(type) + typeArguments;
    }

    private static string QualifiedMetadataName(INamedTypeSymbol type)
    {
        var prefix = type.ContainingType is { } containing
            ? QualifiedMetadataName(containing) + "+"
            : NamespacePrefix(type.ContainingNamespace);

        return prefix + type.MetadataName;
    }

    private static string NamespacePrefix(INamespaceSymbol? ns) =>
        ns is null || ns.IsGlobalNamespace ? "" : ns.ToDisplayString() + ".";
}
