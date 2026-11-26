using Metano.Compiler.Analysis;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Diagnostics for an instance field or auto-property of a value type that has
/// no initializer and no zero value the TypeScript target can spell (a date or
/// time, a tuple, a type parameter, an <c>[InlineWrapper]</c> or generic struct,
/// a struct of another package): C# starts it at <c>default</c>, the emitted
/// constructor stores <c>undefined!</c> (W10-10).
/// </summary>
internal static class UninitializedValueStorageDiagnostics
{
    /// <summary>
    /// Reports MS0001 (warning) at each such member of the class <paramref name="ir"/>
    /// that some declared constructor leaves unassigned, so the missing zero
    /// value stays visible, as the TypeScript definite-assignment check made it
    /// before the field moved into the constructor.
    /// </summary>
    public static void Report(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        Action<MetanoDiagnostic> report
    )
    {
        // A struct's unset fields report where its default is built
        // (a default or parameterless construction of it): only a class has
        // constructor stores to check.
        if (ir.Semantics.IsValueType)
            return;

        var assigned = IrConstructorAssignedMembers.Of(ir);

        foreach (var (name, storageType) in UninitializedStorageOf(ir))
        {
            if (assigned.Contains(name) || !LacksZeroValue(storageType))
                continue;

            report(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.UnsupportedFeature,
                    $"'{type.Name}.{name}' has no initializer, and TypeScript has no zero value "
                        + "for its type: it reads as undefined until a constructor assigns it. "
                        + "Initialize it in C#, or assign it in every constructor.",
                    type.GetMembers(name).FirstOrDefault()?.Locations.FirstOrDefault()
                        ?? type.Locations.FirstOrDefault()
                )
            );
        }
    }

    private static IEnumerable<(string Name, IrTypeRef Type)> UninitializedStorageOf(
        IrClassDeclaration ir
    )
    {
        foreach (var member in ir.Members ?? [])
        {
            switch (member)
            {
                case IrFieldDeclaration
                {
                    IsStatic: false,
                    IsConstant: false,
                    IsCompileTimeConstant: false,
                    IsCapturedByCtor: false,
                    IsRequired: false,
                    Initializer: null,
                } field:
                    yield return (field.Name, field.Type);
                    break;

                case IrPropertyDeclaration
                {
                    IsStatic: false,
                    Initializer: null,
                    Semantics:
                    {
                        HasGetterBody: false,
                        HasSetterBody: false,
                        IsAbstract: false,
                        IsRequired: false,
                        IsIndexer: false,
                    },
                } property:
                    yield return (property.Name, property.Type);
                    break;
            }
        }
    }

    private static bool LacksZeroValue(IrTypeRef type) =>
        IsValueType(type) && IrToTsClassBridge.InstanceStorageDefault(type) is null;

    private static bool IsValueType(IrTypeRef type) =>
        type switch
        {
            IrPrimitiveTypeRef
            {
                Primitive: IrPrimitive.String or IrPrimitive.Object or IrPrimitive.Void,
            } => false,
            IrPrimitiveTypeRef => true,
            IrNamedTypeRef named => named.Semantics?.IsValueType == true,
            IrTupleTypeRef or IrKeyValuePairTypeRef or IrTypeParameterRef => true,
            _ => false,
        };
}
