using System.Text.Json.Serialization;
using SampleIssueTracker.Issues.Domain;

namespace SampleIssueTracker;

/// <summary>
/// Metano transpiles this class into a <c>JsonContext extends SerializerContext</c>
/// on the TypeScript side, with a lazy <see cref="Metano.Runtime.TypeSpec{T}"/>
/// getter per <c>[JsonSerializable]</c> type.
///
/// <para>
/// Exercises the full descriptor surface that Phase 5 validation cares about:
/// <list type="bullet">
///   <item>branded primitives (<see cref="IssueId"/>, <see cref="UserId"/>)</item>
///   <item>string enums (<see cref="IssuePriority"/>, <see cref="IssueStatus"/>, <see cref="IssueType"/>)</item>
///   <item>nullable fields (<c>UserId? AssigneeId</c>, <c>string? SprintKey</c>)</item>
///   <item>Temporal types (<see cref="System.DateTimeOffset"/>)</item>
///   <item>decimal (for the custom-converter round-trip test)</item>
///   <item>array of nested refs (<see cref="Comment"/>[])</item>
/// </list>
/// </para>
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IssueSnapshot))]
[JsonSerializable(typeof(Comment))]
public partial class JsonContext : JsonSerializerContext;
