using Metano.Compiler.Analysis;
using Metano.Compiler.Dart.AST;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Lists the Dart types a method body names, so <see cref="DartImportCollector"/> can import
/// their files exactly as it does for signature types. The walk is read-only and descends
/// through <see cref="IrNodeChildren"/> (<see cref="DartIrChildren"/> for expressions), crossing lambda boundaries.
/// <para>
/// Covered positions: static-member receivers (<c>DelExt.twice(h.get)</c>, which includes the
/// type hosting an extension helper), <c>new</c> expressions, casts, type tests and top-level
/// <c>is Type</c> patterns, local-variable and <c>foreach</c> variable types, and catch
/// exception types. Types that only appear inside nested patterns (property, positional,
/// list, logical) or generic type arguments of a call are not collected yet.
/// </para>
/// </summary>
public static class DartBodyTypeReferences
{
    public static IReadOnlyList<DartType> Collect(IReadOnlyList<IrStatement>? body)
    {
        var types = new List<DartType>();

        foreach (var statement in body ?? [])
            VisitStatement(statement, types);

        return types;
    }

    public static IReadOnlyList<DartType> Collect(IrExpression? expression)
    {
        var types = new List<DartType>();

        if (expression is not null)
            VisitExpression(expression, types);

        return types;
    }

    private static void VisitStatement(IrStatement statement, List<DartType> types)
    {
        types.AddRange(TypesNamedBy(statement));

        foreach (var expression in IrNodeChildren.ExpressionsOf(statement))
            VisitExpression(expression, types);

        foreach (var nested in IrNodeChildren.StatementsOf(statement))
            VisitStatement(nested, types);
    }

    private static void VisitExpression(IrExpression expression, List<DartType> types)
    {
        types.AddRange(TypesNamedBy(expression));

        foreach (var child in DartIrChildren.ExpressionsOf(expression))
            VisitExpression(child, types);

        foreach (var statement in IrNodeChildren.StatementsOf(expression))
            VisitStatement(statement, types);
    }

    private static IEnumerable<DartType> TypesNamedBy(IrStatement statement) =>
        (
            statement switch
            {
                IrVariableDeclaration declaration => [declaration.Type],
                IrForEachStatement loop => [loop.VariableType],
                IrTryStatement attempt => (attempt.Catches ?? []).Select(c => c.ExceptionType),
                _ => [],
            }
        )
            .OfType<IrTypeRef>()
            .Select(IrToDartTypeMapper.Map);

    private static IEnumerable<DartType> TypesNamedBy(IrExpression expression) =>
        expression switch
        {
            DartNamedConstruction construction => [IrToDartTypeMapper.Map(construction.Type)],
            IrTypeReference reference => [IrToDartTypeMapper.Map(NamedTypeOf(reference))],
            IrNewExpression creation => [IrToDartTypeMapper.Map(creation.Type)],
            IrCastExpression cast => [IrToDartTypeMapper.Map(cast.TargetType)],
            IrTypeCheck check => [IrToDartTypeMapper.Map(check.Type)],
            IrIsPatternExpression { Pattern: IrTypePattern pattern } =>
            [
                IrToDartTypeMapper.Map(pattern.Type),
            ],
            _ => [],
        };

    private static IrNamedTypeRef NamedTypeOf(IrTypeReference reference) =>
        new(IrToDartNamingPolicy.InnermostTypeSegment(reference.Name), Origin: reference.Origin);
}
