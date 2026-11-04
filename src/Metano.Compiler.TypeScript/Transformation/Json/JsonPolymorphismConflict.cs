using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// A hierarchy-wide <c>MS0032</c> that leaves the base without
/// <c>polymorphism</c>: a JSON property that cannot share a payload with the
/// discriminator (FR-012), or <c>[PlainObject]</c> guards that cannot tell
/// values apart (FR-019), with where to report it.
/// </summary>
internal readonly record struct JsonPolymorphismConflict(string Message, Location? Location);
