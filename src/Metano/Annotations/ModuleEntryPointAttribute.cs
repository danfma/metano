namespace Metano.Annotations;

/// <summary>
/// Marks a static method whose body becomes the top-level executable code of the
/// generated TypeScript module instead of being emitted as a regular function.
/// The containing class is typically annotated with
/// <see cref="NoContainerAttribute"/> so that the rest of its static members
/// flatten onto the module surface alongside the unwrapped entry point — but
/// <c>[ModuleEntryPoint]</c> itself does not require it.
///
/// <para>
/// Exercised end-to-end by <c>samples/SampleTodo.Service</c>, whose Hono
/// bootstrap method becomes the module's top-level statements after
/// transpilation.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ModuleEntryPointAttribute : Attribute;
