namespace Metano.Compiler.IR;

/// <summary>
/// The <c>IDisposable</c> contract a class or struct takes on itself rather than inheriting
/// it from its base type.
/// </summary>
/// <param name="Dispose">The member that implements <c>Dispose()</c>, under the name and
/// attributes the type declares it with.</param>
public sealed record IrDisposableContract(IrContractMember Dispose);
