namespace MetaSharp;

/// <summary>
/// Marks a value-like struct to transpile as a branded primitive companion object in TypeScript.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class InlineWrapperAttribute : Attribute;
