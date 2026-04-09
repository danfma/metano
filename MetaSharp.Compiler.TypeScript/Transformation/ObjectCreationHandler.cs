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

        // [PlainObject] types lower to object literals — no class wrapper, no
        // constructor invocation, just a plain JS data shape.
        if (type is INamedTypeSymbol plainType && SymbolHelper.HasPlainObject(plainType))
            return CreatePlainObjectLiteral(plainType, creation.ArgumentList, creation);

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

        if (type is INamedTypeSymbol plainType && SymbolHelper.HasPlainObject(plainType))
            return CreatePlainObjectLiteral(plainType, creation.ArgumentList, creation);

        if (type is INamedTypeSymbol { IsRecord: true } recordType)
            return CreateNewFromArgs(recordType, creation.ArgumentList);

        var args = creation
            .ArgumentList.Arguments.Select(a => _parent.TransformExpression(a.Expression))
            .ToList();

        return new TsNewExpression(new TsIdentifier(type?.Name ?? "Object"), args);
    }

    public TsExpression TransformWithExpression(WithExpressionSyntax withExpr)
    {
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

        // [PlainObject] target: lower to spread literal `{ ...source, k: v }` instead
        // of the class-based `source.with({ k: v })` form, since the type has no
        // `with` method on its prototype.
        var sourceType = _parent.Model.GetTypeInfo(withExpr.Expression).Type;
        if (sourceType is INamedTypeSymbol named && SymbolHelper.HasPlainObject(named))
        {
            var spreadProps = new List<TsObjectProperty>(properties.Count + 1)
            {
                new("", new TsSpreadExpression(source)),
            };
            spreadProps.AddRange(properties);
            return new TsObjectLiteral(spreadProps);
        }

        // record with { X = expr } → source.with({ x: expr })
        return new TsCallExpression(
            new TsPropertyAccess(source, "with"),
            [new TsObjectLiteral(properties)]
        );
    }

    /// <summary>
    /// Lowers <c>new T(args)</c> to a TypeScript object literal when <c>T</c> has
    /// <c>[PlainObject]</c>. Positional arguments are matched to the constructor's
    /// parameter names (taken from the resolved <see cref="IMethodSymbol"/>); named
    /// arguments use their explicit name. Each parameter becomes a property keyed by
    /// its camelCase name; default-valued params that the user omitted are simply
    /// dropped (they're treated as <c>undefined</c> in TS, matching how plain JS
    /// objects work).
    /// </summary>
    private TsExpression CreatePlainObjectLiteral(
        INamedTypeSymbol type,
        ArgumentListSyntax? argumentList,
        ExpressionSyntax creationSyntax)
    {
        var properties = new List<TsObjectProperty>();
        if (argumentList is null || argumentList.Arguments.Count == 0)
            return new TsObjectLiteral(properties);

        // Resolve the constructor symbol so positional args can be matched to param
        // names. Roslyn picks the right overload based on the call site.
        var ctor = _parent.Model.GetSymbolInfo(creationSyntax).Symbol as IMethodSymbol;
        var ctorParams = ctor?.Parameters;

        for (var i = 0; i < argumentList.Arguments.Count; i++)
        {
            var arg = argumentList.Arguments[i];
            string paramName;
            if (arg.NameColon is not null)
            {
                // Explicit named argument: `new T(title: "x")`
                paramName = arg.NameColon.Name.Identifier.Text;
            }
            else if (ctorParams is { } parms && i < parms.Length)
            {
                paramName = parms[i].Name;
            }
            else
            {
                // No symbol resolution available — fall back to a positional name.
                // This shouldn't happen for well-formed code; the printer would still
                // emit valid TS but with a confusing key.
                paramName = $"_{i}";
            }

            var value = _parent.TransformExpression(arg.Expression);
            properties.Add(new TsObjectProperty(TypeScriptNaming.ToCamelCase(paramName), value));
        }

        return new TsObjectLiteral(properties);
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
