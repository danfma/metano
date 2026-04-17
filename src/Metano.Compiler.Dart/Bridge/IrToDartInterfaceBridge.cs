using Metano.Compiler.IR;
using Metano.Dart.AST;

namespace Metano.Dart.Bridge;

/// <summary>
/// Converts an <see cref="IrInterfaceDeclaration"/> into a Dart <c>abstract interface class</c>.
/// Dart 3 uses this class-modifier combo to forbid subclass implementation inheritance
/// while allowing <c>implements</c> — the closest Dart idiom to a C# interface.
/// <para>
/// Method and property bodies are left abstract (<c>;</c>-terminated signatures) for now.
/// When expression extraction lands, default implementations will render as concrete bodies.
/// </para>
/// </summary>
public static class IrToDartInterfaceBridge
{
    public static void Convert(IrInterfaceDeclaration ir, List<DartTopLevel> statements)
    {
        var name = IrToDartNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
        var typeParameters = ConvertTypeParameters(ir.TypeParameters);

        // Dart's `implements` (not `extends`) is idiomatic for interface-to-interface reuse.
        var implementsList = ir.BaseInterfaces is { Count: > 0 } bases
            ? (IReadOnlyList<DartType>)bases.Select(IrToDartTypeMapper.Map).ToList()
            : null;

        var members = new List<DartClassMember>();
        if (ir.Members is not null)
        {
            foreach (var m in ir.Members)
            {
                switch (m)
                {
                    case IrPropertyDeclaration prop:
                        members.Add(ConvertPropertyAsGetter(prop));
                        break;
                    case IrMethodDeclaration method:
                        members.Add(ConvertMethod(method));
                        break;
                    // Events are not emitted on Dart interfaces — Dart doesn't have a
                    // first-class event construct. Users can expose `Stream<T>` getters
                    // explicitly if they want pub/sub.
                }
            }
        }

        statements.Add(
            new DartClass(
                name,
                Modifier: DartClassModifier.AbstractInterface,
                TypeParameters: typeParameters,
                Implements: implementsList,
                Members: members.Count > 0 ? members : null
            )
        );
    }

    private static DartGetter ConvertPropertyAsGetter(IrPropertyDeclaration prop) =>
        new(
            Name: IrToDartNamingPolicy.ToMemberName(prop.Name, prop.Attributes),
            ReturnType: IrToDartTypeMapper.Map(prop.Type),
            IsStatic: prop.IsStatic,
            IsAbstract: true
        );

    private static DartMethodSignature ConvertMethod(IrMethodDeclaration method)
    {
        var parameters = method
            .Parameters.Select(p => new DartParameter(
                IrToDartNamingPolicy.ToParameterName(p.Name),
                IrToDartTypeMapper.Map(p.Type)
            ))
            .ToList();
        return new DartMethodSignature(
            Name: IrToDartNamingPolicy.ToMemberName(method.Name, method.Attributes),
            Parameters: parameters,
            ReturnType: IrToDartTypeMapper.Map(method.ReturnType),
            TypeParameters: ConvertTypeParameters(method.TypeParameters),
            IsStatic: method.IsStatic,
            IsAbstract: true,
            IsAsync: method.Semantics.IsAsync
        );
    }

    private static IReadOnlyList<DartTypeParameter>? ConvertTypeParameters(
        IReadOnlyList<IrTypeParameter>? typeParameters
    )
    {
        if (typeParameters is null || typeParameters.Count == 0)
            return null;

        return typeParameters
            .Select(tp =>
            {
                var extends = tp.Constraints is { Count: > 0 } c
                    ? IrToDartTypeMapper.Map(c[0])
                    : null;
                return new DartTypeParameter(tp.Name, extends);
            })
            .ToList();
    }
}
