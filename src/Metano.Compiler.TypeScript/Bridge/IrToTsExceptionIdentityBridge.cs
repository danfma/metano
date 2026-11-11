using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The .NET identity of a user exception class (#651): its C# type name as
/// <c>name</c> and as the runtime <c>Exception</c>'s <c>$typeName</c>, and its
/// namespace as <c>$typeNamespace</c>, which <c>toString()</c> joins into
/// .NET's full type name.
/// <code>
/// override readonly name: string = "UnsupportedCurrencyException";
///
/// protected override get $typeName(): string {
///   return "UnsupportedCurrencyException";
/// }
///
/// protected override get $typeNamespace(): string {
///   return "Shop";
/// }
/// </code>
/// Without them the class would inherit the name of the runtime class it
/// derives from (<c>"ArgumentException"</c>). The names are string literals,
/// not <c>new.target.name</c>, so a minifier cannot rename them and the C#
/// name survives a <c>[Name]</c> rename of the TypeScript class.
/// <para>
/// Known deviations from .NET's <c>Type.ToString()</c>: a nested class joins
/// its containing classes with <c>.</c> (.NET writes <c>Outer+Inner</c>),
/// and a generic class keeps its open name (<c>Box`1</c>, .NET writes the
/// type arguments of the constructed type).
/// </para>
/// </summary>
internal static class IrToTsExceptionIdentityBridge
{
    private const string NameMember = "name";
    private const string TypeNameHook = "$typeName";
    private const string NamespaceHook = "$typeNamespace";

    /// <summary>
    /// The identity members of <paramref name="type"/> when <paramref name="ir"/>
    /// is an exception class, none for any other class: the runtime's
    /// <c>$typeName</c> and <c>$typeNamespace</c> hooks, whose <c>$</c> no C#
    /// member name can carry, and the JavaScript <c>name</c>, unless the class
    /// declares a member that emits as <c>name</c> itself (it keeps that member;
    /// <c>toString()</c> still reads the hooks).
    /// </summary>
    public static IReadOnlyList<TsClassMember> Members(INamedTypeSymbol type, IrClassDeclaration ir)
    {
        if (!ir.Semantics.IsException)
            return [];

        var hooks = new List<TsClassMember>
        {
            Hook(TypeNameHook, type.MetadataName),
            Hook(NamespaceHook, DotNetNamespaceOf(type)),
        };

        return DeclaresName(ir) ? hooks : [NameField(type.MetadataName), .. hooks];
    }

    /// <summary>
    /// The <c>MS0001</c> of a user exception whose BCL base has no runtime
    /// class: it extends the root <c>Exception</c>, so tests for the base's
    /// ancestors (<c>catch (SystemException)</c>) miss it; <c>null</c> for any
    /// other class.
    /// </summary>
    public static MetanoDiagnostic? UnmappedBaseDiagnostic(
        INamedTypeSymbol type,
        IrClassDeclaration ir
    )
    {
        if (
            ir.BaseType is not IrNamedTypeRef baseType
            || !BclExceptionClasses.IsBclException(baseType)
            || IrToTsTypeMapper.ExceptionClasses.IsMapped(baseType)
        )
            return null;

        var site = BaseListEntryOf(type);

        return new MetanoDiagnostic(
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.UnsupportedFeature,
            $"'{type.Name}' derives from '{type.BaseType!.ToDisplayString()}', which has no runtime "
                + "class in TypeScript: it extends 'Exception', so catch clauses and type tests for "
                + "the base's own ancestors do not take it (spec 011 FR-014).",
            site?.GetLocation() ?? type.Locations.FirstOrDefault()
        );
    }

    private static SyntaxNode? BaseListEntryOf(INamedTypeSymbol type) =>
        type
            .DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
            .Select(declaration => declaration.BaseList?.Types.FirstOrDefault())
            .FirstOrDefault(entry => entry is not null);

    private static bool DeclaresName(IrClassDeclaration ir) =>
        (ir.Members ?? []).Any(member =>
            string.Equals(member.Name, NameMember, StringComparison.OrdinalIgnoreCase)
        );

    private static TsFieldMember NameField(string typeName) =>
        new(
            NameMember,
            new TsStringType(),
            new TsStringLiteral(typeName),
            Readonly: true,
            Override: true
        );

    private static TsGetterMember Hook(string hook, string value) =>
        new(
            hook,
            new TsStringType(),
            [new TsReturnStatement(new TsStringLiteral(value))],
            Override: true,
            Accessibility: TsAccessibility.Protected
        );

    /// <summary>
    /// The namespace .NET prefixes the type name with, followed by the
    /// containing classes of a nested type.
    /// </summary>
    private static string DotNetNamespaceOf(INamedTypeSymbol type)
    {
        var segments = new List<string>();

        for (
            var containing = type.ContainingType;
            containing is not null;
            containing = containing.ContainingType
        )
            segments.Insert(0, containing.MetadataName);

        if (type.ContainingNamespace is { IsGlobalNamespace: false } ns)
            segments.Insert(0, ns.ToDisplayString());

        return string.Join('.', segments);
    }
}
