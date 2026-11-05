namespace Metano.Compiler.IR;

/// <summary>
/// A type's identity across compilations: the name of the assembly that
/// declares it and its metadata name (<c>Shop.Money</c>, <c>Shop.Box`1</c>,
/// <c>Shop.Outer+Inner</c>), which a target looks the type up by.
/// </summary>
public sealed record IrTypeKey(string AssemblyName, string MetadataName);
