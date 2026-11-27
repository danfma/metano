using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Analysis;

/// <summary>Symbol facts the reified type parameter analysis and its classifiers share.</summary>
internal static class ReifiedSymbols
{
    /// <summary>The type parameters <paramref name="type"/> is, or is built from.</summary>
    public static IEnumerable<ITypeParameterSymbol> CollectTypeParameters(ITypeSymbol type)
    {
        switch (type)
        {
            case ITypeParameterSymbol tp:
                yield return tp;
                break;
            case IArrayTypeSymbol arr:
                foreach (var tp in CollectTypeParameters(arr.ElementType))
                    yield return tp;

                break;
            case INamedTypeSymbol named:
                foreach (var arg in named.TypeArguments)
                {
                    foreach (var tp in CollectTypeParameters(arg))
                        yield return tp;
                }

                break;
        }
    }

    /// <summary>
    /// The member <paramref name="symbol"/> sits in: the symbol itself, or the member
    /// that declares the lambdas (and, with <paramref name="throughLocalFunctions"/>, the
    /// local functions) around it.
    /// </summary>
    public static ISymbol MemberOf(ISymbol symbol, bool throughLocalFunctions = true)
    {
        var current = symbol;

        while (
            current is IMethodSymbol { ContainingSymbol: { } parent } method
            && (
                method.MethodKind is MethodKind.AnonymousFunction
                || (throughLocalFunctions && method.MethodKind is MethodKind.LocalFunction)
            )
        )
            current = parent;

        return current;
    }

    /// <summary>A public or protected member of a public or protected type.</summary>
    public static bool IsExportedMember(IMethodSymbol method) =>
        method.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected
        && method.ContainingType.DeclaredAccessibility
            is Accessibility.Public
                or Accessibility.Protected;
}
