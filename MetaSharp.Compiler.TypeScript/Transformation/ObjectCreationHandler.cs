using MetaSharp.Compiler;
using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Handles all forms of <c>new</c>-expression and the <c>record with { … }</c>
/// non-destructive update expression.
///
/// Several special cases short-circuit the default <c>new Type(args)</c> output:
/// <list type="bullet">
///   <item><c>new UserId(v)</c> on an <c>[InlineWrapper]</c> struct → <c>UserId.create(v)</c></item>
///   <item><c>new SomeRecord(...)</c> → <c>new SomeRecord(...)</c> with named-argument resolution via <see cref="ArgumentResolver"/></item>
///   <item><c>new SomeException(msg)</c> → <c>new SomeException(msg)</c> if transpilable, otherwise <c>new Error(msg)</c></item>
///   <item><c>record with { X = expr }</c> → <c>source.with({ x: expr })</c> (calls <see cref="RecordSynthesizer.GenerateWith"/>'s output)</item>
/// </list>
///
/// Implicit object creation (<c>SomeType x = new(args)</c>) reuses the same dispatch via
/// the converted-type lookup.
/// </summary>
public sealed class ObjectCreationHandler(ExpressionTransformer parent)
{
    private readonly ExpressionTransformer _parent = parent;

    public TsExpression TransformObjectCreation(ObjectCreationExpressionSyntax creation)
    {
        var typeInfo = _parent.Model.GetTypeInfo(creation);
        var type = typeInfo.Type;

        // Inline wrapper structs are emitted as companion objects, not classes:
        // new UserId(v) -> UserId.create(v)
        if (type is INamedTypeSymbol inlineWrapperType && SymbolHelper.HasInlineWrapper(inlineWrapperType))
        {
            var args = _parent.ArgumentResolver.Resolve(creation.ArgumentList, creation);
            var tsTypeName = SymbolHelper.GetNameOverride(inlineWrapperType) ?? inlineWrapperType.Name;
            return new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier(tsTypeName), "create"),
                args
            );
        }

        // Record struct/record → new Type(args)
        if (type is INamedTypeSymbol { IsRecord: true } recordType)
            return CreateNewFromArgs(recordType, creation.ArgumentList);

        // Exception → new ErrorSubclass(...) or new Error(...)
        if (IsExceptionType(type))
        {
            var args = _parent.ArgumentResolver.Resolve(creation.ArgumentList, creation);
            var errorName = type is INamedTypeSymbol named
                && SymbolHelper.IsTranspilable(named, _parent.AssemblyWideTranspile, _parent.CurrentAssembly)
                ? named.Name
                : "Error";
            return new TsNewExpression(new TsIdentifier(errorName), args);
        }

        // Default: new Type(args) — resolve named arguments to positional
        var ctorArgs = _parent.ArgumentResolver.Resolve(creation.ArgumentList, creation);
        var typeName = type is INamedTypeSymbol nt ? BuildQualifiedTypeName(nt) : (type?.Name ?? "Object");
        return new TsNewExpression(new TsIdentifier(typeName), ctorArgs);
    }

    public TsExpression TransformImplicitObjectCreation(ImplicitObjectCreationExpressionSyntax creation)
    {
        var typeInfo = _parent.Model.GetTypeInfo(creation);
        var type = typeInfo.ConvertedType;

        if (type is INamedTypeSymbol inlineWrapperType && SymbolHelper.HasInlineWrapper(inlineWrapperType))
        {
            var inlineArgs = creation
                .ArgumentList.Arguments.Select(a => _parent.TransformExpression(a.Expression))
                .ToList();
            var tsTypeName = SymbolHelper.GetNameOverride(inlineWrapperType) ?? inlineWrapperType.Name;
            return new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier(tsTypeName), "create"),
                inlineArgs
            );
        }

        if (type is INamedTypeSymbol { IsRecord: true } recordType)
            return CreateNewFromArgs(recordType, creation.ArgumentList);

        var args = creation
            .ArgumentList.Arguments.Select(a => _parent.TransformExpression(a.Expression))
            .ToList();

        return new TsNewExpression(new TsIdentifier(type?.Name ?? "Object"), args);
    }

    public TsExpression TransformWithExpression(WithExpressionSyntax withExpr)
    {
        // record with { X = expr } → source.with({ x: expr })
        var source = _parent.TransformExpression(withExpr.Expression);
        var properties = new List<TsObjectProperty>();

        foreach (var assignment in withExpr.Initializer.Expressions)
        {
            if (assignment is AssignmentExpressionSyntax assign)
            {
                var name = TypeScriptNaming.ToCamelCase(assign.Left.ToString());
                var value = _parent.TransformExpression(assign.Right);
                properties.Add(new TsObjectProperty(name, value));
            }
        }

        return new TsCallExpression(
            new TsPropertyAccess(source, "with"),
            [new TsObjectLiteral(properties)]
        );
    }

    private TsNewExpression CreateNewFromArgs(
        INamedTypeSymbol recordType,
        ArgumentListSyntax? argumentList)
    {
        // Use ResolveArguments for named argument support
        if (argumentList is not null)
        {
            // Find the syntax node that triggered this (for symbol resolution)
            var parentExpr = argumentList.Parent as ExpressionSyntax;
            if (parentExpr is not null)
            {
                var args = _parent.ArgumentResolver.Resolve(argumentList, parentExpr);
                return new TsNewExpression(new TsIdentifier(recordType.Name), args);
            }
        }

        var simpleArgs = argumentList?.Arguments
            .Select(a => _parent.TransformExpression(a.Expression))
            .ToList() ?? [];
        return new TsNewExpression(new TsIdentifier(recordType.Name), simpleArgs);
    }

    /// <summary>
    /// Builds the TS-side qualified name for a type. Nested types become <c>Outer.Inner</c>.
    /// </summary>
    private static string BuildQualifiedTypeName(INamedTypeSymbol type)
    {
        if (type.ContainingType is null) return type.Name;
        var parts = new List<string> { type.Name };
        var current = type.ContainingType;
        while (current is not null)
        {
            parts.Insert(0, current.Name);
            current = current.ContainingType;
        }
        return string.Join(".", parts);
    }

    private static bool IsExceptionType(ITypeSymbol? type)
    {
        var current = type;
        while (current is not null)
        {
            if (current.ToDisplayString() == "System.Exception")
                return true;
            current = current.BaseType;
        }

        return false;
    }
}
