namespace Metano.Annotations;

/// <summary>
/// Co-locates the decorated type with other types that share the same file name in the
/// generated TypeScript output, instead of producing a separate <c>.ts</c> file per
/// type (the default).
///
/// <para>
/// Use case: a primary type plus its tightly-coupled helper types (status enums,
/// nested DTOs, builder types) where forcing them into separate files creates churn
/// and weakens cohesion. The user marks each related type with the same
/// <c>fileName</c> argument, and the compiler emits one <c>.ts</c> with all of them.
/// </para>
///
/// <code>
/// [Transpile, EmitInFile("issue")]
/// public record Issue(IssueId Id, string Title, IssueStatus Status);
///
/// [Transpile, EmitInFile("issue")]
/// public enum IssueStatus { Open, Closed }
///
/// [Transpile, EmitInFile("issue")]
/// public enum IssuePriority { Low, High }
/// </code>
///
/// <para>
/// Both types must live in the same C# namespace — the file is placed under that
/// namespace's folder. Mixing namespaces under the same file name is rejected with
/// <c>MS0008</c>.
/// </para>
///
/// <para>
/// Cross-package consumers automatically resolve the import to the file path: a
/// reference to <c>IssueStatus</c> from another project becomes
/// <c>import { IssueStatus } from "&lt;package&gt;/&lt;ns&gt;/issue"</c>, and multiple
/// names from the same file are merged into one import line.
/// </para>
/// </summary>
/// <param name="fileName">Kebab-case file name (without <c>.ts</c>) under which the
/// type is co-located. All types sharing this name in the same namespace land in the
/// same file.</param>
[AttributeUsage(
    AttributeTargets.Class
        | AttributeTargets.Struct
        | AttributeTargets.Enum
        | AttributeTargets.Interface
        | AttributeTargets.Delegate
)]
public sealed class EmitInFileAttribute(string fileName) : Attribute
{
    public string FileName { get; } = fileName;
}
