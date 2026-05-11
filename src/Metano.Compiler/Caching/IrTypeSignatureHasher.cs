using System.Security.Cryptography;
using System.Text;
using Metano.Compiler.Analysis;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Caching;

/// <summary>
/// Computes a stable per-type fingerprint from a Roslyn
/// <see cref="INamedTypeSymbol"/>. The fingerprint is the unit of
/// invalidation for the per-group incremental skip
/// (#21 / ADR-0023 / PR 3b) — when a type's hash changes, every
/// group whose closure contains that type regenerates.
///
/// <para>
/// The hash mixes everything that can affect emission for the type:
/// </para>
/// <list type="bullet">
///   <item>Fully qualified name + kind (class / struct / record /
///   interface / enum / delegate)</item>
///   <item>Modifiers: <c>IsAbstract</c>, <c>IsSealed</c>,
///   <c>IsStatic</c>, <c>IsRecord</c>, <c>IsValueType</c>,
///   accessibility, <c>IsGenericType</c> + arity</item>
///   <item>Base type FQN, every interface FQN (sorted)</item>
///   <item>Every member's signature (sorted): name, kind, return
///   type, parameter types, accessibility, refness</item>
///   <item>The full normalized syntax text of every declaring node,
///   so body edits invalidate the hash even though the dep graph
///   itself is signature-only (ADR-0018)</item>
/// </list>
///
/// <para>
/// Attributes are folded in via the type-level metadata; the body
/// text inclusion is what catches a method-body edit that does not
/// touch any signature member.
/// </para>
/// </summary>
public static class IrTypeSignatureHasher
{
    public static string Hash(INamedTypeSymbol symbol)
    {
        var sb = new StringBuilder();
        AppendType(sb, symbol);
        return Sha256(sb.ToString());
    }

    private static void AppendType(StringBuilder sb, INamedTypeSymbol symbol)
    {
        sb.Append("TYPE\0");
        sb.Append(IrTypeDependencyGraph.QualifiedName(symbol)).Append('\0');
        sb.Append(symbol.TypeKind).Append('\0');
        sb.Append(symbol.DeclaredAccessibility).Append('\0');
        sb.Append("abstract=").Append(symbol.IsAbstract).Append('\0');
        sb.Append("sealed=").Append(symbol.IsSealed).Append('\0');
        sb.Append("static=").Append(symbol.IsStatic).Append('\0');
        sb.Append("record=").Append(symbol.IsRecord).Append('\0');
        sb.Append("value=").Append(symbol.IsValueType).Append('\0');
        sb.Append("readonly=").Append(symbol.IsReadOnly).Append('\0');
        sb.Append("arity=").Append(symbol.Arity).Append('\0');

        if (symbol.BaseType is { } baseType)
            sb.Append("base=").Append(baseType.ToDisplayString()).Append('\0');

        foreach (var iface in symbol.Interfaces.OrderBy(i => i.ToDisplayString(), StringComparer.Ordinal))
            sb.Append("iface=").Append(iface.ToDisplayString()).Append('\0');

        AppendAttributes(sb, symbol.GetAttributes());

        var members = symbol
            .GetMembers()
            .Where(m => m is not INamedTypeSymbol)
            .Select(MemberSignature)
            .OrderBy(s => s, StringComparer.Ordinal);
        foreach (var member in members)
            sb.Append("member=").Append(member).Append('\0');

        // Body edits (method bodies, field initializers, property
        // getters) live in the syntax text but not in the symbol
        // shape. Fold the full declaring-syntax text in so any source
        // change to this type triggers a re-hash.
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax();
            sb.Append("syntax=").Append(node.ToFullString()).Append('\0');
        }
    }

    private static string MemberSignature(ISymbol member) =>
        member switch
        {
            IMethodSymbol m =>
                $"method:{m.MethodKind}:{m.Name}({string.Join(",", m.Parameters.Select(FormatParam))})->{m.ReturnType.ToDisplayString()};acc={m.DeclaredAccessibility};static={m.IsStatic};virt={m.IsVirtual};over={m.IsOverride};abs={m.IsAbstract}",
            IPropertySymbol p =>
                $"prop:{p.Name}:{p.Type.ToDisplayString()};get={p.GetMethod is not null};set={p.SetMethod is not null};acc={p.DeclaredAccessibility};static={p.IsStatic}",
            IFieldSymbol f =>
                $"field:{f.Name}:{f.Type.ToDisplayString()};acc={f.DeclaredAccessibility};static={f.IsStatic};readonly={f.IsReadOnly};const={f.IsConst}",
            IEventSymbol e =>
                $"event:{e.Name}:{e.Type.ToDisplayString()};acc={e.DeclaredAccessibility};static={e.IsStatic}",
            _ => $"other:{member.Kind}:{member.Name}",
        };

    private static string FormatParam(IParameterSymbol p) =>
        $"{p.RefKind}:{p.Type.ToDisplayString()}:{(p.IsParams ? "params:" : "")}{(p.HasExplicitDefaultValue ? "default" : "")}";

    private static void AppendAttributes(StringBuilder sb, IReadOnlyList<AttributeData> attributes)
    {
        var formatted = attributes
            .Where(a => a.AttributeClass is not null)
            .Select(a =>
                $"{a.AttributeClass!.ToDisplayString()}({string.Join(",", a.ConstructorArguments.Select(FormatConstant))})"
            )
            .OrderBy(s => s, StringComparer.Ordinal);
        foreach (var attr in formatted)
            sb.Append("attr=").Append(attr).Append('\0');
    }

    private static string FormatConstant(TypedConstant constant) =>
        constant.Kind switch
        {
            TypedConstantKind.Array =>
                $"[{string.Join(",", constant.Values.Select(FormatConstant))}]",
            _ => constant.Value?.ToString() ?? "null",
        };

    private static string Sha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
