using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// What the expression extractor needs to know about the primary constructor
/// of the type whose members it is extracting. <see cref="IrClassExtractor"/>
/// sets it around member extraction (<see cref="IrExpressionExtractor.PrimaryCtorScope"/>).
/// </summary>
/// <param name="CapturedParams">The primary-constructor parameters captured by member bodies, keyed to the backing field name the backend assigns in the constructor body; a read is rewritten to <c>this.field</c>.</param>
/// <param name="PromotedParams">The primary-constructor parameters folded into the property that only exposes them (<c>A { get; } = a</c>), keyed to that property; a read in a member body is rewritten to <c>this.A</c>, the parameter's only emitted storage.</param>
/// <param name="InitializersRunInConstructor">The member initializers run in the constructor body after the captured fields are assigned (<see cref="PrimaryConstructorInitializerDeferral.AppliesTo"/>), so an initializer reads a captured parameter through its field too, sharing the storage a member body mutates. Otherwise initializers keep the bare parameter.</param>
internal sealed record PrimaryCtorExtractionScope(
    IReadOnlyDictionary<ISymbol, string> CapturedParams,
    IReadOnlyDictionary<ISymbol, IPropertySymbol> PromotedParams,
    bool InitializersRunInConstructor
);
