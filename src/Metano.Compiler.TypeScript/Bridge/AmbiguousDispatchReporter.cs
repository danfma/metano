using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The two <c>MS0063</c> reporters as one: every dispatcher the target emits
/// — a class's constructors and methods, a module-function or extension
/// group, a static group of a branded companion namespace — is checked for a
/// catch-all overload (<see cref="CatchAllOverloadReporter"/>) and for a
/// branded 64-bit ambiguity (<see cref="BrandedSixtyFourBitReporter"/>).
/// </summary>
internal sealed class AmbiguousDispatchReporter(Action<MetanoDiagnostic> reportDiagnostic)
{
    private readonly CatchAllOverloadReporter _catchAll = new(reportDiagnostic);
    private readonly BrandedSixtyFourBitReporter _brandedSixtyFourBit = new(reportDiagnostic);

    public void ReportConstructors(
        INamedTypeSymbol type,
        IrConstructorDeclaration primary,
        TypeCheckScope classScope
    )
    {
        _catchAll.ReportConstructors(type, primary, classScope);
        _brandedSixtyFourBit.ReportConstructors(type, primary);
    }

    public void ReportMethods(
        INamedTypeSymbol type,
        IrMethodDeclaration primary,
        TypeCheckScope classScope
    )
    {
        _catchAll.ReportMethods(type, primary, classScope);
        _brandedSixtyFourBit.ReportMethods(type, primary);
    }

    /// <summary>
    /// A function dispatcher named <paramref name="name"/> on
    /// <paramref name="type"/>: it has no class scope and inherits nothing.
    /// </summary>
    /// <param name="isOperatorGroup">The group holds the operators or
    /// conversions of a branded companion.</param>
    public void ReportFunctions(
        INamedTypeSymbol type,
        string name,
        IReadOnlyList<FunctionOverloadShape> overloads,
        bool isOperatorGroup = false
    )
    {
        _catchAll.ReportFunctions(type, name, overloads);
        _brandedSixtyFourBit.ReportFunctions(type, name, overloads, isOperatorGroup);
    }
}
