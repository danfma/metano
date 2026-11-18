using Metano.Compiler.Frontend.Roslyn;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The name a method groups under (spec 013 FR-038), shared by the overload
/// groups and the value equality shapes.
/// </summary>
internal static class OverloadGroupName
{
    /// <summary>
    /// The name a method groups under: its C# name, or an explicit
    /// implementation's interface member name.
    /// </summary>
    public static string Of(IMethodSymbol method) =>
        ExplicitImplementationLowering.ImplementedInterfaceMember(method)?.Name ?? method.Name;
}
