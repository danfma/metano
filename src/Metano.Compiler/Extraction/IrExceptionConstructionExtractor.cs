using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

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
/// construction (verified against the runtime); <c>System.Exception</c>
/// itself reports <c>"Exception of type '&lt;runtime type&gt;' was thrown."</c>,
/// where the runtime type is the most-derived class being constructed.
/// </para>
/// </summary>
public static class IrExceptionConstructionExtractor
{
    private const string SystemExceptionName = "System.Exception";

    private static readonly IReadOnlyDictionary<string, string> DefaultMessages = new Dictionary<
        string,
        string
    >
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
    /// <param name="runtimeType">The most-derived type being constructed (the
    /// exception itself for <c>new</c>, the derived user class for a base
    /// call); only <c>System.Exception</c>'s default message depends on it.</param>
    /// <param name="target">The active backend, for target-scoped <c>[Ignore]</c>.</param>
    public static IrExceptionConstruction? Extract(
        IMethodSymbol? constructor,
        IReadOnlyList<IrArgument> arguments,
        INamedTypeSymbol runtimeType,
        Metano.Annotations.TargetLanguage? target
    )
    {
        if (constructor is null || !LacksTargetDeclaration(constructor.ContainingType, target))
            return null;

        var roles = arguments
            .Select((argument, index) => RoleOf(BoundParameter(constructor, argument, index)))
            .ToList();

        return new IrExceptionConstruction(
            roles,
            DefaultMessageOf(constructor.ContainingType, runtimeType)
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
            "message" => IrExceptionArgumentRole.Message,
            "innerException" => IrExceptionArgumentRole.InnerException,
            "paramName" => IrExceptionArgumentRole.ParamName,
            "actualValue" => IrExceptionArgumentRole.ActualValue,
            _ => IrExceptionArgumentRole.Unsupported,
        };

    private static string? DefaultMessageOf(
        INamedTypeSymbol exceptionType,
        INamedTypeSymbol runtimeType
    )
    {
        var exceptionName = exceptionType.ToDisplayString();

        if (exceptionName == SystemExceptionName)
            return $"Exception of type '{runtimeType.ToDisplayString()}' was thrown.";

        return DefaultMessages.GetValueOrDefault(exceptionName);
    }
}
