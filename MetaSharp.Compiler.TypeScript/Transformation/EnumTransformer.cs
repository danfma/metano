using MetaSharp.Compiler;
using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace MetaSharp.Transformation;

/// <summary>
/// Transforms a C# enum into TypeScript output. Two shapes are emitted:
/// <list type="bullet">
///   <item>
///     Plain numeric enums → a TypeScript <c>enum</c> declaration whose members carry the
///     same constant values as the C# source.
///   </item>
///   <item>
///     <c>[StringEnum]</c>-tagged enums → a <c>const</c> object literal of the string
///     values plus a companion <c>type</c> alias derived from <c>typeof</c> /
///     <c>keyof</c>, giving a string union with autocompletion in the consumer.
///   </item>
/// </list>
///
/// Pure / stateless: no instance state, no diagnostics, no compilation lookups.
/// </summary>
public static class EnumTransformer
{
    public static void Transform(INamedTypeSymbol type, List<TsTopLevel> statements)
    {
        var isStringEnum = SymbolHelper.HasStringEnum(type);

        if (isStringEnum)
        {
            var entries = new List<(string Key, TsExpression Value)>();
            foreach (var member in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (!member.HasConstantValue)
                    continue;
                var name = SymbolHelper.GetNameOverride(member) ?? member.Name;
                entries.Add((member.Name, new TsStringLiteral(name)));
            }

            var tsName = TypeTransformer.GetTsTypeName(type);
            // export const EnumName = { Member: "value", ... } as const;
            statements.Add(new TsConstObject(tsName, entries));
            // export type EnumName = typeof EnumName[keyof typeof EnumName];
            statements.Add(
                new TsTypeAlias(tsName, new TsNamedType($"typeof {tsName}[keyof typeof {tsName}]"))
            );
        }
        else
        {
            var members = new List<TsEnumMember>();
            foreach (var member in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (!member.HasConstantValue)
                    continue;
                var name = SymbolHelper.GetNameOverride(member) ?? member.Name;
                members.Add(
                    new TsEnumMember(name, new TsLiteral(member.ConstantValue!.ToString()!))
                );
            }

            statements.Add(new TsEnum(TypeTransformer.GetTsTypeName(type), members));
        }
    }
}
