using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>The interop methods of the <c>Metano</c> package the core lowers itself.</summary>
internal enum InteropMethodKind
{
    None,

    /// <summary><c>Js.Code(…)</c> / <c>Js.Code&lt;T&gt;(…)</c> (spec 011 FR-031).</summary>
    Code,

    /// <summary><c>InteropExtensions.UnsafeCast&lt;T&gt;()</c> (spec 011 FR-035).</summary>
    UnsafeCast,

    /// <summary><c>InteropExtensions.AsDynamic()</c> (spec 011 FR-036).</summary>
    AsDynamic,
}

/// <summary>Recognizes the interop methods of <see cref="InteropMethodKind"/>.</summary>
internal static class InteropMethods
{
    private const string JsTypeName = "Metano.Interop.Js";
    private const string ExtensionsTypeName = "Metano.Interop.InteropExtensions";

    /// <summary>Whether a simple name can spell an interop method (a cheap pre-filter).</summary>
    public static bool MayName(string identifier) =>
        identifier
            is nameof(InteropMethodKind.Code)
                or nameof(InteropMethodKind.UnsafeCast)
                or nameof(InteropMethodKind.AsDynamic);

    /// <summary>The interop method <paramref name="method"/> is, reduced or not.</summary>
    public static InteropMethodKind KindOf(IMethodSymbol? method)
    {
        var definition = method?.ReducedFrom ?? method;

        return (definition?.ContainingType?.ToDisplayString(), definition?.Name) switch
        {
            (JsTypeName, "Code") => InteropMethodKind.Code,
            (ExtensionsTypeName, "UnsafeCast") => InteropMethodKind.UnsafeCast,
            (ExtensionsTypeName, "AsDynamic") => InteropMethodKind.AsDynamic,
            _ => InteropMethodKind.None,
        };
    }
}
