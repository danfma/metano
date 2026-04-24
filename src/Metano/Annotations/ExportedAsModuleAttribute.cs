namespace Metano.Annotations;

/// <summary>
/// <b>Superseded by</b> <see cref="ErasableAttribute"/>. Migrating
/// existing callers is scheduled for the follow-up PR that ships the
/// <c>[NoEmit]</c> redefinition; until then this attribute stays fully
/// functional.
/// <para>
/// <c>[Erasable]</c> produces the same top-level emission and
/// additionally flattens call-site access (<c>ClassName.member</c> →
/// <c>member</c>), closing a latent bug where cross-module references
/// to an <c>[ExportedAsModule]</c> class emitted dangling
/// <c>ClassName.member</c> without a TypeScript-side class declaration.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ExportedAsModuleAttribute : Attribute;
