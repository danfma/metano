namespace Metano.Compiler.IR;

/// <summary>
/// Target-agnostic fact attached to the construction of an exception type
/// that has no emitted declaration on the target side (a BCL exception such
/// as <c>ArgumentException</c>, or one from another non-transpilable
/// assembly): either a <c>new T(...)</c> expression or the base-constructor
/// call of a user exception deriving from such a type.
/// <para>
/// <see cref="ArgumentRoles"/> runs parallel to the argument list the fact is
/// attached to (<see cref="IrNewExpression.Arguments"/> or
/// <see cref="IrConstructorDeclaration.BaseArguments"/>): entry <c>i</c> is the
/// role of the constructor parameter argument <c>i</c> binds to, resolved
/// independently of whether it was passed positionally or by name, and of the
/// overload's parameter order (<c>ArgumentOutOfRangeException(paramName,
/// message)</c> vs <c>ArgumentException(message, paramName)</c>). Keeping only
/// the roles here — never a second copy of the argument expressions — lets IR
/// rewriters transform the arguments without leaving this fact stale.
/// </para>
/// <para>
/// Backends rebuild the target-native error construction from the roles,
/// emulating how .NET composes <c>Exception.Message</c>.
/// </para>
/// </summary>
/// <param name="ArgumentRoles">The role of each argument, aligned by index with
/// the owning argument list.</param>
/// <param name="DefaultMessage">The message .NET reports when no message (or a
/// <c>null</c> one) is supplied — e.g. <c>"Value cannot be null."</c> for
/// <c>ArgumentNullException</c>. <c>null</c> when the type's default message
/// is not known.</param>
/// <param name="MessageMayBeNull">Whether the supplied <c>message</c> argument
/// may be <c>null</c> at runtime according to C# nullable flow analysis; a
/// backend then falls back to <paramref name="DefaultMessage"/> at runtime,
/// as .NET does.</param>
/// <param name="IsMessageOverridden">Whether the class being constructed (or
/// one of its transpilable bases) overrides <c>Exception.Message</c>; a
/// backend must then leave the native message unset so the override is not
/// shadowed.</param>
public sealed record IrExceptionConstruction(
    IReadOnlyList<IrExceptionArgumentRole> ArgumentRoles,
    string? DefaultMessage = null,
    bool MessageMayBeNull = false,
    bool IsMessageOverridden = false
)
{
    /// <summary>
    /// The expression of the argument playing <paramref name="role"/> in
    /// <paramref name="arguments"/> (the list this fact is aligned with), or
    /// <c>null</c> when the construction does not supply that role.
    /// </summary>
    public IrExpression? ValueOf(IrExceptionArgumentRole role, IReadOnlyList<IrArgument> arguments)
    {
        for (var index = 0; index < ArgumentRoles.Count && index < arguments.Count; index++)
        {
            if (ArgumentRoles[index] == role)
                return arguments[index].Value;
        }

        return null;
    }
}

/// <summary>
/// The meaning of a BCL exception constructor parameter, keyed off the
/// parameter name the BCL uses consistently across the exception hierarchy.
/// </summary>
public enum IrExceptionArgumentRole
{
    /// <summary>A parameter with no emulated meaning (e.g.
    /// <c>FileNotFoundException.fileName</c>); backends drop its value.</summary>
    Unmapped,

    /// <summary><c>message</c>: the human-readable error message.</summary>
    Message,

    /// <summary><c>innerException</c>: the exception that caused this one.</summary>
    InnerException,

    /// <summary><c>paramName</c>: the offending parameter
    /// (<c>ArgumentException</c> family); .NET appends
    /// <c>" (Parameter 'name')"</c> to the message.</summary>
    ParamName,

    /// <summary><c>actualValue</c>: the rejected value
    /// (<c>ArgumentOutOfRangeException</c>); .NET appends
    /// <c>"Actual value was X."</c> on a new line.</summary>
    ActualValue,

    /// <summary><c>objectName</c>: the disposed object
    /// (<c>ObjectDisposedException</c>); .NET appends
    /// <c>"Object name: 'X'."</c> on a new line.</summary>
    ObjectName,
}
