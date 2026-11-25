using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Diagnostics for C# events the class lowering cannot reproduce: every event is
/// emitted as a field-like instance event (a backing field plus <c>name$add</c> /
/// <c>name$remove</c> methods), so custom <c>add</c>/<c>remove</c> bodies would be
/// dropped and a static event would be read from the instance (#768 audit).
/// </summary>
internal static class EventDiagnostics
{
    /// <summary>
    /// Reports MS0001 at each event of <paramref name="type"/> the emitted shape does not
    /// reproduce. Non-empty custom accessors are an error (decision W9-13): their bodies
    /// are dropped, so the output is known to be wrong. A static event is a warning: it is
    /// read from the instance, which fails loudly (TypeScript and run time).
    /// </summary>
    public static void Report(INamedTypeSymbol type, Action<MetanoDiagnostic> report)
    {
        foreach (var evt in type.GetMembers().OfType<IEventSymbol>())
        {
            if (UnsupportedShape(evt) is not var (shape, severity))
                continue;

            report(
                new MetanoDiagnostic(
                    severity,
                    DiagnosticCodes.UnsupportedFeature,
                    $"The event '{type.Name}.{evt.Name}' is {shape}, which TypeScript "
                        + "emission does not support: it is emitted as a field-like "
                        + "instance event.",
                    evt.Locations.FirstOrDefault()
                )
            );
        }
    }

    private static (string Shape, MetanoDiagnosticSeverity Severity)? UnsupportedShape(
        IEventSymbol evt
    )
    {
        var notEmitted =
            evt.IsImplicitlyDeclared
            || evt.IsAbstract
            || SymbolHelper.HasIgnore(evt, TargetLanguage.TypeScript);

        if (notEmitted)
            return null;

        if (DeclaresAccessorBodies(evt))
            return ("declared with custom add/remove accessors", MetanoDiagnosticSeverity.Error);

        return evt.IsStatic ? ("static", MetanoDiagnosticSeverity.Warning) : null;
    }

    /// <summary>
    /// Whether an accessor of <paramref name="evt"/> has a body that does something:
    /// empty <c>add { }</c> / <c>remove { }</c> (the usual explicit interface stub)
    /// have no observable effect, so the field-like shape is equivalent.
    /// </summary>
    private static bool DeclaresAccessorBodies(IEventSymbol evt) =>
        evt.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is EventDeclarationSyntax { AccessorList: { } accessors }
            && accessors.Accessors.Any(accessor =>
                accessor.ExpressionBody is not null || accessor.Body is { Statements.Count: > 0 }
            )
        );
}
