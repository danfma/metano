namespace Metano.Annotations;

/// <summary>
/// <b>Superseded by</b> <see cref="NoContainerAttribute"/>. Migrate
/// unless the call site needs the wrapper handle described below.
/// <para>
/// <c>[NoContainer]</c> produces the same top-level emission and
/// additionally flattens call-site access (<c>ClassName.member</c> →
/// <c>member</c>), closing a latent bug where cross-module references
/// to an <c>[ExportedAsModule]</c> class emitted dangling
/// <c>ClassName.member</c> without a TypeScript-side class declaration.
/// </para>
/// <para>
/// JSX-DSL exception: SampleCounterV3-style widget DSLs depend on
/// <c>ClassName.member</c> staying observable so the JSX runtime can
/// dispatch on the wrapper. There <c>[ExportedAsModule]</c> is
/// intentionally the chosen primitive — switching to
/// <c>[NoContainer]</c> would flatten the call site and erase the
/// wrapper handle. Authors of new widget DSLs make the choice
/// deliberately.
/// </para>
/// </summary>
/// <seealso cref="NoContainerAttribute"/>
[Obsolete("Use [NoContainer] instead. [ExportedAsModule] will be removed in a future release.")]
[AttributeUsage(AttributeTargets.Class)]
public sealed class ExportedAsModuleAttribute : Attribute;
