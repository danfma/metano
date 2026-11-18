namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// A barrel export line to move to the top of its barrel: the module
/// <see cref="DerivedModule"/>, whose class extends a class of
/// <see cref="BaseModule"/> that constructs it from its type initializer. Both
/// are module paths relative to the output root, without extension.
/// </summary>
public sealed record BarrelHoist(string BaseModule, string DerivedModule);
