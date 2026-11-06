using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Reports <c>MS0065</c> (spec 012 FR-059, DECIDED-18) for every interop
/// boundary whose signature carries a 64-bit integer: a parameter, return,
/// property or field type of an <c>[Import]</c>, <c>[Emit]</c> or
/// <c>[External]</c> member or type, or of the BCL member a <i>user</i>
/// assembly maps with <c>[MapMethod]</c> / <c>[MapProperty]</c> (Metano's own
/// <c>src/Metano/Runtime</c> mappings are excluded — they move with the
/// compiler). Nullable, array, generic-argument and delegate-parameter forms
/// count. The emitted TypeScript type is <c>bigint</c> and no coercion is
/// inserted, so the JavaScript side exchanges a <c>bigint</c> too; the warning
/// names each declaration once so the author can adapt the JS side or change
/// the C# type. Read from the Roslyn symbols, not the IR: Metano emits no
/// declaration for these members, so the boundary lives on the declaration
/// (the same read <see cref="Int64EnumPrecision"/> makes for enums).
/// </summary>
internal sealed class Int64InteropBoundaries(
    Compilation compilation,
    bool assemblyWideTranspile,
    Action<MetanoDiagnostic> reportDiagnostic
)
{
    // The identity name of the toolchain's annotations + runtime mappings
    // assembly: every [MapMethod] / [MapProperty] it declares is Metano's own
    // and is versioned with the compiler, so it is never a user boundary.
    private static readonly string MetanoAssemblyName = typeof(TranspileAttribute)
        .Assembly.GetName()
        .Name!;

    private readonly HashSet<ISymbol> _reportedMembers = new(SymbolEqualityComparer.Default);

    private readonly HashSet<string> _reportedMappings = new(StringComparer.Ordinal);

    /// <summary>The subject the message blames for the <c>bigint</c> exchange.</summary>
    private enum BoundaryKind
    {
        Import,
        Emit,
        External,
        Mapping,
    }

    /// <summary>How the boundary moves the value, and which clause the message spells.</summary>
    private enum ExchangeDirection
    {
        Return,
        Parameter,
        ReadWrite,
    }

    public void Report()
    {
        ReportUserMappings();

        VisitNamespace(compilation.Assembly.GlobalNamespace);
    }

    /// <summary>
    /// The <c>[assembly: MapMethod]</c> / <c>[assembly: MapProperty]</c> templates
    /// of the compilation and of every referenced assembly, except Metano's own.
    /// Mirrors the walk the declarative-mapping registry is built from, so a
    /// mapping the target applies is also the mapping this checks.
    /// </summary>
    private void ReportUserMappings()
    {
        var assemblies = new List<IAssemblySymbol> { compilation.Assembly };

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                assemblies.Add(assembly);
        }

        foreach (var assembly in assemblies)
        {
            if (assembly.Identity.Name == MetanoAssemblyName)
                continue;

            foreach (var attribute in assembly.GetAttributes())
                ReportMappingAttribute(attribute, assembly);
        }
    }

    private void ReportMappingAttribute(AttributeData attribute, IAssemblySymbol assembly)
    {
        var attributeLabel = attribute.AttributeClass?.Name switch
        {
            "MapMethodAttribute" or "MapMethod" => "[MapMethod]",
            "MapPropertyAttribute" or "MapProperty" => "[MapProperty]",
            _ => null,
        };

        if (
            attributeLabel is null
            || attribute.ConstructorArguments.Length < 2
            || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol declaringType
            || attribute.ConstructorArguments[1].Value is not string memberName
        )
            return;

        // One warning per mapping attribute, whichever of the member's
        // overloads first carries a 64-bit integer. The key names the
        // attribute's declaring assembly, so the same BCL member mapped by
        // two user assemblies warns at each mapping.
        var mappingKey =
            $"{assembly.Identity.Name}|{declaringType.GetStableFullName()}|{memberName}";

        if (!_reportedMappings.Add(mappingKey))
            return;

        foreach (var member in declaringType.GetMembers(memberName))
        {
            if (SignatureSpotOf(member) is { } spot && CarriesSixtyFourBit(spot.Type))
            {
                reportDiagnostic(
                    Boundary(
                        member,
                        BoundaryKind.Mapping,
                        attributeLabel,
                        spot,
                        attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    )
                );
                return;
            }
        }
    }

    private void VisitNamespace(INamespaceSymbol namespaceSymbol)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
            VisitType(type);

        foreach (var nested in namespaceSymbol.GetNamespaceMembers())
            VisitNamespace(nested);
    }

    private void VisitType(INamedTypeSymbol type, BoundaryKind? inheritedKind = null)
    {
        var isImported = SymbolHelper.HasImport(type);
        var isExternal = SymbolHelper.HasExternal(type);

        BoundaryKind? surfaceKind;

        if (isImported || isExternal)
        {
            var own = isImported ? BoundaryKind.Import : BoundaryKind.External;

            surfaceKind = own;

            ReportSurfaceMembers(type, own);
        }
        else if (inheritedKind is { } inherited)
        {
            // A type nested in an imported or external type is part of the
            // same surface: its members cross the same boundary (the delegate
            // declarations especially).
            surfaceKind = inherited;

            ReportSurfaceMembers(type, inherited);
        }
        else
        {
            surfaceKind = null;

            if (
                SymbolHelper.IsDeclaredTranspilable(
                    type,
                    assemblyWideTranspile,
                    compilation.Assembly
                )
            )
            {
                foreach (var member in SurfaceMembers(type))
                {
                    if (KindOf(member) is { } kind)
                        ReportMember(member, kind, attributeLabel: null);
                }
            }
        }

        foreach (var nested in type.GetTypeMembers())
            VisitType(nested, surfaceKind);
    }

    private void ReportSurfaceMembers(INamedTypeSymbol type, BoundaryKind kind)
    {
        foreach (var member in SurfaceMembers(type))
            ReportMember(member, kind, attributeLabel: null);
    }

    /// <summary>
    /// The members whose signature crosses the boundary: fields, properties and
    /// methods (a delegate's <c>Invoke</c> included), but not constructors, the
    /// accessors, the compiler generated rest, or the private members that stay
    /// inside the assembly — TypeScript has no visibility below the exported
    /// surface.
    /// </summary>
    private static IEnumerable<ISymbol> SurfaceMembers(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (
                member.IsImplicitlyDeclared
                && member is not IMethodSymbol { MethodKind: MethodKind.DelegateInvoke }
            )
                continue;

            if (member.DeclaredAccessibility == Accessibility.Private)
                continue;

            if (
                member is IMethodSymbol
                {
                    MethodKind: MethodKind.Constructor
                        or MethodKind.PropertyGet
                        or MethodKind.PropertySet
                        or MethodKind.EventAdd
                        or MethodKind.EventRemove
                }
            )
                continue;

            if (member is IFieldSymbol or IPropertySymbol or IMethodSymbol)
                yield return member;
        }
    }

    private static BoundaryKind? KindOf(ISymbol member)
    {
        if (SymbolHelper.HasExternal(member))
            return BoundaryKind.External;

        if (SymbolHelper.HasImport(member))
            return BoundaryKind.Import;

        return SymbolHelper.HasEmit(member) ? BoundaryKind.Emit : null;
    }

    private void ReportMember(ISymbol member, BoundaryKind kind, string? attributeLabel)
    {
        if (!_reportedMembers.Add(member))
            return;

        if (SignatureSpotOf(member) is { } spot && CarriesSixtyFourBit(spot.Type))
            reportDiagnostic(
                Boundary(member, kind, attributeLabel, spot, member.Locations.FirstOrDefault())
            );
    }

    /// <summary>
    /// The first signature position a 64-bit integer can sit in, in a fixed
    /// order (return, then parameters, then the property or field type), so
    /// the message is stable across runs. <c>null</c> when the member has no
    /// such position (a void method without parameters).
    /// </summary>
    private static SignatureSpot? SignatureSpotOf(ISymbol member) =>
        member switch
        {
            IMethodSymbol method => MethodSignatureSpot(method),
            IPropertySymbol property => new SignatureSpot(
                ExchangeDirection.ReadWrite,
                $"has property type '{property.Type.ToDisplayString()}'",
                property.Type
            ),
            IFieldSymbol field => new SignatureSpot(
                ExchangeDirection.ReadWrite,
                $"has field type '{field.Type.ToDisplayString()}'",
                field.Type
            ),
            _ => null,
        };

    /// <summary>
    /// The return position when it carries the 64-bit integer, else the first
    /// parameter that does — a <c>string Name(long handle)</c> warns on its
    /// parameter, a <c>long Count()</c> on its return.
    /// </summary>
    private static SignatureSpot? MethodSignatureSpot(IMethodSymbol method)
    {
        if (!method.ReturnsVoid && CarriesSixtyFourBit(method.ReturnType))
        {
            return new SignatureSpot(
                ExchangeDirection.Return,
                $"returns '{method.ReturnType.ToDisplayString()}'",
                method.ReturnType
            );
        }

        return MethodParameterSpot(method);
    }

    private static SignatureSpot? MethodParameterSpot(IMethodSymbol method)
    {
        var parameter = method.Parameters.FirstOrDefault(p => CarriesSixtyFourBit(p.Type));

        return parameter is null
            ? null
            : new SignatureSpot(
                ExchangeDirection.Parameter,
                $"parameter '{parameter.Name}' is '{parameter.Type.ToDisplayString()}'",
                parameter.Type
            );
    }

    /// <summary>True for <c>long</c>, <c>ulong</c>, <c>nint</c>, <c>nuint</c>, and for
    /// every type containing one as its nullable value, array element, generic
    /// argument, or delegate parameter / return.</summary>
    private static bool CarriesSixtyFourBit(ITypeSymbol type) =>
        CarriesSixtyFourBit(type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));

    private static bool CarriesSixtyFourBit(ITypeSymbol type, HashSet<ITypeSymbol> visited)
    {
        if (!visited.Add(type))
            return false;

        if (
            type.SpecialType
            is SpecialType.System_Int64
                or SpecialType.System_UInt64
                or SpecialType.System_IntPtr
                or SpecialType.System_UIntPtr
        )
            return true;

        if (type is IArrayTypeSymbol array)
            return CarriesSixtyFourBit(array.ElementType, visited);

        if (type is not INamedTypeSymbol named)
            return false;

        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return CarriesSixtyFourBit(named.TypeArguments[0], visited);

        if (named.TypeArguments.Any(argument => CarriesSixtyFourBit(argument, visited)))
            return true;

        if (named.TypeKind != TypeKind.Delegate || named.DelegateInvokeMethod is not { } invoke)
            return false;

        if (!invoke.ReturnsVoid && CarriesSixtyFourBit(invoke.ReturnType, visited))
            return true;

        return invoke.Parameters.Any(parameter => CarriesSixtyFourBit(parameter.Type, visited));
    }

    private static MetanoDiagnostic Boundary(
        ISymbol member,
        BoundaryKind kind,
        string? attributeLabel,
        SignatureSpot spot,
        Location? location
    )
    {
        var subject = kind switch
        {
            BoundaryKind.Import => "imported",
            BoundaryKind.Emit => "emitted",
            BoundaryKind.External => "external",
            _ => "mapped",
        };

        var label = attributeLabel is null
            ? $"'{ShortNameOf(member)}'"
            : $"{attributeLabel} '{ShortNameOf(member)}'";

        return new MetanoDiagnostic(
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.BigIntAtInteropBoundary,
            $"{label} {spot.Text}, which is a bigint in TypeScript; "
                + $"the {subject} JavaScript {ExchangeClause(spot.Direction)} (spec 012 FR-059).",
            location
        );
    }

    private static string ExchangeClause(ExchangeDirection direction) =>
        direction switch
        {
            ExchangeDirection.Return => "must return a bigint, not a number",
            ExchangeDirection.Parameter => "must receive a bigint, not a number",
            _ => "exchanges a bigint with it, not a number",
        };

    /// <summary>
    /// <c>'Lib.Count()'</c> for a method, <c>'Lib.Ticks'</c> for a property or
    /// field, and <c>'X()'</c> for a constructor — the short surface name, as
    /// the diagnostic contract's example spells it.
    /// </summary>
    private static string ShortNameOf(ISymbol member)
    {
        var container = member.ContainingType?.Name ?? "";

        if (member is IMethodSymbol { MethodKind: MethodKind.Constructor })
            return $"{container}()";

        return member is IMethodSymbol
            ? $"{container}.{member.Name}()"
            : $"{container}.{member.Name}";
    }

    /// <summary>The one signature position named by the message.</summary>
    private sealed record SignatureSpot(ExchangeDirection Direction, string Text, ITypeSymbol Type);
}
