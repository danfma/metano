using System.Runtime.CompilerServices;
using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reports a constructor body that reads, through <c>this</c> (explicit or implicit), a property a derived
/// class can still override. C# dispatches that read to the most derived
/// override, whose state is already initialized: field initializers of every
/// class in the hierarchy run before any constructor. The generated classes
/// run in construction order instead. TypeScript assigns a derived class's
/// properties (including parameter properties) only after <c>super()</c>
/// returns, so the base constructor reads <c>undefined</c> (an
/// <c>abstract</c> member is even rejected with TS2715). Dart runs field
/// initializers and initializing formals before the super constructor, but
/// anything a derived constructor body assigns runs after it. The
/// diagnostic is target-neutral; the output is still produced.
///
/// <para>
/// Reported: reading an abstract property, or a virtual / unsealed-override
/// property that the compilation overrides in a derived class (a library
/// virtual with no override in the compilation stays silent; overrides in other
/// packages are a known gap). Not reported: a sealed override, a non-virtual property, a
/// <c>base.X</c> read (it binds to the base implementation, not the
/// override), a write (<c>X = v</c> does not observe the value), reads inside
/// a lambda or local function (they run when invoked, usually after
/// construction, and the closure cannot be attributed to construction time),
/// and any read in a sealed class (nothing can override further). Virtual
/// method calls are left out on purpose: the call itself dispatches
/// correctly on both targets (members live on the prototype or the class), so
/// whether it misbehaves depends on the override's body, which is not
/// visible here. Field and property initializers are not scanned: C# rejects
/// <c>this</c> access in them (CS0236). A local function that the constructor calls immediately is
/// not tracked.
/// </para>
/// </summary>
internal static class OverridablePropertyConstructionReads
{
    private static readonly ConditionalWeakTable<
        Compilation,
        HashSet<IPropertySymbol>
    > OverriddenPropertiesCache = new();

    public static void Report(
        INamedTypeSymbol type,
        Compilation? compilation,
        TargetLanguage? target
    )
    {
        if (compilation is null || type.TypeKind is not TypeKind.Class || type.IsSealed)
            return;

        foreach (var root in ConstructorRoots(type, compilation))
        foreach (var read in OverridableReadsIn(root, compilation, target))
            ReportRead(read);
    }

    private static IEnumerable<IOperation> ConstructorRoots(
        INamedTypeSymbol type,
        Compilation compilation
    )
    {
        foreach (var syntax in type.DeclaringSyntaxReferences.Select(r => r.GetSyntax()))
        {
            if (syntax is not TypeDeclarationSyntax declaration)
                continue;

            if (SymbolHelper.TryGetSemanticModel(compilation, syntax.SyntaxTree) is not { } model)
                continue;

            foreach (var root in ConstructorsOf(declaration))
                if (model.GetOperation(root) is { } operation)
                    yield return operation;
        }
    }

    private static IEnumerable<SyntaxNode> ConstructorsOf(TypeDeclarationSyntax declaration) =>
        declaration
            .Members.OfType<ConstructorDeclarationSyntax>()
            .Where(c => !IsStatic(c.Modifiers));

    private static bool IsStatic(SyntaxTokenList modifiers) =>
        modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));

    private static IEnumerable<IPropertyReferenceOperation> OverridableReadsIn(
        IOperation operation,
        Compilation compilation,
        TargetLanguage? target
    )
    {
        if (operation is IAnonymousFunctionOperation or ILocalFunctionOperation or INameOfOperation)
            yield break;

        if (
            operation is IPropertyReferenceOperation reference
            && IsReportableRead(reference, compilation, target)
        )
        {
            yield return reference;
        }

        foreach (var child in ChildrenObservingReads(operation))
        foreach (var read in OverridableReadsIn(child, compilation, target))
            yield return read;
    }

    /// <summary>
    /// The children of <paramref name="operation"/> whose properties are read:
    /// the target property of a plain or deconstructing assignment is written, not
    /// read (its receiver and indexer arguments still are). Indexers are method-like
    /// and, like virtual methods, are not reported.
    /// </summary>
    private static IEnumerable<IOperation> ChildrenObservingReads(IOperation operation) =>
        operation switch
        {
            ISimpleAssignmentOperation { Target: IPropertyReferenceOperation target } assignment =>
                target.ChildOperations.Append(assignment.Value),
            IDeconstructionAssignmentOperation deconstruction => [deconstruction.Value],
            _ => operation.ChildOperations,
        };

    private static bool IsReportableRead(
        IPropertyReferenceOperation reference,
        Compilation compilation,
        TargetLanguage? target
    ) =>
        IsThisRead(reference)
        && IsOverridden(reference.Property, compilation)
        && !SymbolHelper.HasIgnore(reference.Property, target);

    private static bool IsThisRead(IPropertyReferenceOperation reference) =>
        reference.Instance
            is IInstanceReferenceOperation
            {
                ReferenceKind: InstanceReferenceKind.ContainingTypeInstance,
                Syntax: not BaseExpressionSyntax,
            };

    /// <summary>
    /// Whether a derived class can change what the read returns: an abstract
    /// property always can (it has no value of its own); a virtual or unsealed
    /// override property only when the compilation actually contains an
    /// override of it anywhere (matched per property, not per subtree). Overrides in other packages are not visible (known gap).
    /// </summary>
    private static bool IsOverridden(IPropertySymbol property, Compilation compilation) =>
        property.IsAbstract
        || (
            IsOverridable(property)
            && OverriddenPropertiesIn(compilation).Contains(property.OriginalDefinition)
        );

    private static bool IsOverridable(IPropertySymbol property) =>
        !property.IsIndexer && (property.IsVirtual || (property.IsOverride && !property.IsSealed));

    private static HashSet<IPropertySymbol> OverriddenPropertiesIn(Compilation compilation) =>
        OverriddenPropertiesCache.GetValue(compilation, ScanOverriddenProperties);

    private static HashSet<IPropertySymbol> ScanOverriddenProperties(Compilation compilation)
    {
        var overridden = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);

        foreach (var type in AllTypesIn(compilation.Assembly.GlobalNamespace))
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            for (
                var parent = property.OverriddenProperty;
                parent is not null;
                parent = parent.OverriddenProperty
            )
                overridden.Add(parent.OriginalDefinition);

        return overridden;
    }

    private static IEnumerable<INamedTypeSymbol> AllTypesIn(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        foreach (var nested in WithNestedTypes(type))
            yield return nested;

        foreach (var child in ns.GetNamespaceMembers())
        foreach (var type in AllTypesIn(child))
            yield return type;
    }

    private static IEnumerable<INamedTypeSymbol> WithNestedTypes(INamedTypeSymbol type)
    {
        yield return type;

        foreach (var nested in type.GetTypeMembers())
        foreach (var inner in WithNestedTypes(nested))
            yield return inner;
    }

    private static void ReportRead(IPropertyReferenceOperation read)
    {
        var property = read.Property;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'{property.ContainingType.Name}.{property.Name}' is overridable and is read "
                    + "while the object is still being constructed. C# dispatches the read to the "
                    + "most derived override, but TypeScript assigns a derived class's properties "
                    + "only after super() returns (so the read sees undefined, and an abstract "
                    + "property is rejected with TS2715), and Dart runs constructor-body "
                    + "assignments after the super constructor. Read it from a method called "
                    + "after construction, or pass the value to the constructor.",
                read.Syntax.GetLocation()
            )
        );
    }
}
