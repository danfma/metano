using System.Globalization;
using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Analysis;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The runtime type descriptor of a reached declaration (spec 011 FR-018,
/// FR-019, FR-049), a TypeScript projection of its declaration data — there is
/// no descriptor in the IR. Every shape is a data literal that imports nothing
/// from <c>metano-runtime</c> but an interface's <c>implementsInterface</c>,
/// and every cross-module reference in it is a thunk, so a module loads in any
/// order and an unused descriptor bundles to nothing:
/// <list type="bullet">
///   <item>a class, record or struct gets a lazily memoized static getter,
///   <c>static get $runtimeType(): ClassType&lt;X&gt; { return (X$rt ??= { … }); }</c>,
///   with its memo a module-level <c>let</c> (hoisted out of a nested type's
///   namespace) and <c>static override get</c> when its base has a getter;</item>
///   <item>an interface gets <c>export const IFoo$runtimeType: InterfaceType&lt;IFoo&gt;</c>
///   after its declaration;</item>
///   <item>an enum gets <c>export const E$runtimeType: EnumType&lt;E&gt;</c> after
///   its <c>as const</c> object, with an import-free membership test (every
///   valid bit combination for a <c>[Flags]</c> enum).</item>
/// </list>
/// </summary>
internal sealed class RuntimeTypeEmitter(TypeScriptTransformContext context)
{
    private const string ValueParameter = "v";

    private readonly TypeScriptTransformContext _context = context;

    private RuntimeTypeReachabilityResult RuntimeTypes => _context.RuntimeTypes;

    /// <summary>
    /// Adds the getter and its memo to the class <paramref name="type"/> emitted
    /// into <paramref name="sink"/> from <paramref name="start"/>, when the class
    /// is reached. A nested class hoists its memo into
    /// <paramref name="moduleLocal"/>, so it stays at module scope.
    /// </summary>
    public void DecorateClass(
        INamedTypeSymbol type,
        List<TsTopLevel> sink,
        int start,
        ModuleLocalDeclarations? moduleLocal
    )
    {
        if (!IsReached(type))
            return;

        var index = sink.FindIndex(start, statement => statement is TsClass);

        if (index < 0 || sink[index] is not TsClass declaration)
            return;

        if (ReportsNameClash(type, declaration, sink))
            return;

        var memoName = MemoNameOf(type);
        var memo = MemoDeclaration(memoName, ClassTypeOf(type, declaration.Name));

        sink[index] = declaration with
        {
            Members = [Getter(type, declaration, memoName), .. declaration.Members],
        };

        if (moduleLocal is null)
            sink.Insert(index, memo);
        else
            moduleLocal.Hoist(memo);
    }

    /// <summary>The interface's exported descriptor literal; <c>null</c> when it is not reached.</summary>
    public TsTopLevel? InterfaceDescriptor(INamedTypeSymbol type, string tsName)
    {
        if (!IsReached(type))
            return null;

        var exportName = $"{tsName}{IrToTsRuntimeTypeReferences.ExportSuffix}";

        var literal = new TsObjectLiteral([
            .. Identity("interface", type),
            new TsObjectProperty(
                "baseInterfaces",
                Thunks(type.AllInterfaces, "a base interface thunk")
            ),
            new TsObjectProperty("plainImplementers", new TsArrayLiteral([])),
            new TsObjectProperty(
                "test",
                Predicate(
                    SelfType(type, tsName),
                    new TsCallExpression(
                        new TsRuntimeHelperReference("implementsInterface"),
                        [new TsIdentifier(ValueParameter), new TsIdentifier(exportName)]
                    )
                )
            ),
        ]);

        return ExportedDescriptor(exportName, "InterfaceType", SelfType(type, tsName), literal);
    }

    /// <summary>The enum's exported descriptor literal; <c>null</c> when it is not reached.</summary>
    public TsTopLevel? EnumDescriptor(
        INamedTypeSymbol type,
        IrEnumDeclaration ir,
        IReadOnlyList<(string Key, TsExpression Value)> entries,
        string tsName
    )
    {
        if (!IsReached(type))
            return null;

        var values = entries.Select(entry => entry.Value).ToList();
        var enumType = new TsNamedType(tsName);

        var literal = new TsObjectLiteral([
            .. Identity(ir.Style is IrEnumStyle.String ? "stringEnum" : "enum", type),
            new TsObjectProperty("values", new TsArrayLiteral(values)),
            new TsObjectProperty(
                "names",
                new TsArrayLiteral([
                    .. ir.Members.Select(member => new TsStringLiteral(member.Name)),
                ])
            ),
            new TsObjectProperty("isFlags", BooleanLiteral(ir.IsFlags)),
            new TsObjectProperty(
                "test",
                Predicate(enumType, ir.IsFlags ? FlagsTest(ir) : MembershipTest(values))
            ),
        ]);

        return ExportedDescriptor(
            $"{tsName}{IrToTsRuntimeTypeReferences.ExportSuffix}",
            "EnumType",
            enumType,
            literal
        );
    }

    private bool IsReached(INamedTypeSymbol type) =>
        RuntimeTypes.Reaches(RuntimeTypeReachability.KeyOf(type));

    /// <summary>
    /// A user static the output names <c>$runtimeType</c> (through <c>[Name]</c>),
    /// or a module declaration named like the memo, would collide with the
    /// descriptor: <c>MS0001</c>, and the class keeps no getter.
    /// </summary>
    private bool ReportsNameClash(
        INamedTypeSymbol type,
        TsClass declaration,
        IReadOnlyList<TsTopLevel> sink
    )
    {
        var memoName = MemoNameOf(type);

        var clash =
            declaration.Members.Any(member =>
                MemberNameOf(member) == IrToTsRuntimeTypeReferences.GetterName
            )
                ? $"a static member of '{type.Name}' is emitted as "
                    + $"'{IrToTsRuntimeTypeReferences.GetterName}'"
            : sink.Any(statement => DeclaredNameOf(statement) == memoName)
                ? $"a module declaration is named '{memoName}'"
            : null;

        if (clash is null)
            return false;

        _context.ReportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedFeature,
                $"The runtime type descriptor of '{type.ToDisplayString()}' cannot be emitted: "
                    + $"{clash}, the name the descriptor uses (spec 011 FR-018). Rename it with "
                    + "[Name(\"...\")].",
                type.Locations.FirstOrDefault()
            )
        );

        return true;
    }

    private static string? MemberNameOf(TsClassMember member) =>
        member switch
        {
            TsGetterMember getter => getter.Name,
            TsFieldMember field => field.Name,
            TsMethodMember method => method.Name,
            _ => null,
        };

    private static string? DeclaredNameOf(TsTopLevel statement) =>
        statement switch
        {
            TsClass declaration => declaration.Name,
            TsFunction function => function.Name,
            TsConstObject constObject => constObject.Name,
            TsTopLevelStatement { Inner: TsVariableDeclaration variable } => variable.Name,
            _ => null,
        };

    private TsGetterMember Getter(INamedTypeSymbol type, TsClass declaration, string memoName)
    {
        var classType = ClassTypeOf(type, declaration.Name);
        var literal = ClassLiteral(type, declaration);

        return new TsGetterMember(
            IrToTsRuntimeTypeReferences.GetterName,
            classType,
            [
                new TsReturnStatement(
                    new TsParenthesized(
                        new TsBinaryExpression(new TsIdentifier(memoName), "??=", literal)
                    )
                ),
            ],
            Static: true,
            Override: RuntimeTypes.OverridingGetters.Contains(RuntimeTypeReachability.KeyOf(type))
        );
    }

    private TsObjectLiteral ClassLiteral(INamedTypeSymbol type, TsClass declaration)
    {
        var self = new TsIdentifier(declaration.Name);
        var selfType = SelfType(type, declaration.Name);

        return new TsObjectLiteral([
            .. Identity("class", type),
            new TsObjectProperty("ctor", self),
            .. BaseThunk(type, declaration),
            new TsObjectProperty("interfaces", Thunks(type.AllInterfaces, "an interface thunk")),
            new TsObjectProperty(
                "typeParameters",
                new TsArrayLiteral([
                    .. type.TypeParameters.Select(parameter => new TsStringLiteral(parameter.Name)),
                ])
            ),
            new TsObjectProperty("isAbstract", BooleanLiteral(type.IsAbstract)),
            new TsObjectProperty("isValueType", BooleanLiteral(type.IsValueType)),
            .. Create(type, self),
            new TsObjectProperty(
                "test",
                Predicate(
                    selfType,
                    IrToTsRuntimeTestBridge.InstanceofTest(new TsIdentifier(ValueParameter), self)
                )
            ),
        ]);
    }

    /// <summary>
    /// <c>base: () =&gt; Base.$runtimeType</c> when the base declares a getter:
    /// the class extends a reached class, a library class or a runtime exception.
    /// </summary>
    private IEnumerable<TsObjectProperty> BaseThunk(INamedTypeSymbol type, TsClass declaration)
    {
        if (
            !RuntimeTypes.OverridingGetters.Contains(RuntimeTypeReachability.KeyOf(type))
            || declaration.Extends is not TsNamedType baseType
        )
            return [];

        var baseValue = IrToTsExpressionBridge.TypeValueReference(baseType.Name, baseType.Origin);

        return
        [
            new TsObjectProperty(
                "base",
                Thunk(new TsPropertyAccess(baseValue, IrToTsRuntimeTypeReferences.GetterName))
            ),
        ];
    }

    /// <summary>
    /// <c>create: () =&gt; new X()</c> for a concrete class with a public
    /// parameterless constructor (declared or implicit) and for a struct that
    /// declares none.
    /// </summary>
    private static IEnumerable<TsObjectProperty> Create(INamedTypeSymbol type, TsExpression self)
    {
        var creatable =
            !type.IsAbstract
            && type.InstanceConstructors.Any(constructor =>
                constructor is { Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public }
                && (!type.IsValueType || constructor.IsImplicitlyDeclared)
            )
            && (!type.IsValueType || type.InstanceConstructors.All(c => c.IsImplicitlyDeclared));

        return creatable
            ? [new TsObjectProperty("create", Thunk(new TsNewExpression(self, [])))]
            : [];
    }

    /// <summary>
    /// The descriptors of <paramref name="interfaces"/> that exist: the reached
    /// interfaces of this compilation and those of referenced libraries, as
    /// thunks, deduplicated, in declaration order.
    /// </summary>
    private TsArrayLiteral Thunks(IEnumerable<INamedTypeSymbol> interfaces, string site) =>
        new([
            .. interfaces
                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                .Where(HasDescriptor)
                .Select(iface => DescriptorOf(iface, site))
                .OfType<TsExpression>()
                .Select(Thunk),
        ]);

    private bool HasDescriptor(INamedTypeSymbol iface) =>
        SymbolEqualityComparer.Default.Equals(iface.ContainingAssembly, _context.CurrentAssembly)
            ? IsReached(iface)
            : iface.ContainingAssembly.HasTranspileAssembly()
                && iface.DeclaredAccessibility is Accessibility.Public;

    private TsExpression? DescriptorOf(INamedTypeSymbol type, string site) =>
        IrToTsRuntimeTypeReferences.Of(
            IrTypeRefMapper.Map(
                type.OriginalDefinition,
                _context.OriginResolver,
                TargetLanguage.TypeScript
            ),
            site
        );

    private static IEnumerable<TsObjectProperty> Identity(string kind, INamedTypeSymbol type) =>
        [
            new("kind", new TsStringLiteral(kind)),
            new("name", new TsStringLiteral(type.MetadataName)),
            new("fullName", new TsStringLiteral(FullNameOf(type))),
            new("namespace", new TsStringLiteral(NamespaceOf(type))),
        ];

    /// <summary>The .NET <c>Type.FullName</c> of a definition: <c>Ns.Outer+Inner`1</c>.</summary>
    private static string FullNameOf(INamedTypeSymbol type)
    {
        var name = type.ContainingType is { } container
            ? $"{FullNameOf(container)}+{type.MetadataName}"
            : type.MetadataName;

        var ns = NamespaceOf(type);

        return type.ContainingType is null && ns.Length > 0 ? $"{ns}.{name}" : name;
    }

    private static string NamespaceOf(INamedTypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "";

    /// <summary>
    /// The module-level memo of a class getter, named after the class's
    /// containment path so a nested class's memo never clashes with another's.
    /// </summary>
    private string MemoNameOf(INamedTypeSymbol type)
    {
        var path = new List<string>();

        for (
            INamedTypeSymbol? current = type;
            current is not null;
            current = current.ContainingType
        )
            path.Insert(0, _context.ResolveTsName(current));

        return $"{string.Join("$", path)}{IrToTsRuntimeTypeReferences.MemoSuffix}";
    }

    private static TsTopLevel MemoDeclaration(string name, TsType classType) =>
        new TsTopLevelStatement(
            new TsVariableDeclaration(
                name,
                Initializer: null,
                Const: false,
                Type: new TsUnionType([classType, new TsNamedType("undefined")])
            )
        );

    private static TsTopLevel ExportedDescriptor(
        string name,
        string descriptorType,
        TsType describedType,
        TsObjectLiteral literal
    ) =>
        new TsTopLevelStatement(
            new TsVariableDeclaration(
                name,
                literal,
                Const: true,
                Exported: true,
                Type: RuntimeTypeNamed(descriptorType, describedType)
            )
        );

    private static TsType ClassTypeOf(INamedTypeSymbol type, string tsName) =>
        RuntimeTypeNamed("ClassType", SelfType(type, tsName));

    private static TsNamedType RuntimeTypeNamed(string name, TsType describedType) =>
        new(name, [describedType], RuntimeHelperBinding: name);

    /// <summary>
    /// The described type, with <c>unknown</c> for each type parameter: a static
    /// getter cannot name the class's own type parameters (TS2302), and the
    /// descriptor describes the open definition.
    /// </summary>
    private static TsNamedType SelfType(INamedTypeSymbol type, string tsName) =>
        type.TypeParameters.Length == 0
            ? new TsNamedType(tsName)
            : new TsNamedType(
                tsName,
                [.. type.TypeParameters.Select(_ => new TsNamedType("unknown"))]
            );

    /// <summary><c>(v: unknown): v is X =&gt; test</c>.</summary>
    private static TsArrowFunction Predicate(TsType described, TsExpression test) =>
        new(
            [new TsParameter(ValueParameter, new TsNamedType("unknown"))],
            [new TsReturnStatement(test)]
        )
        {
            ReturnType = new TsTypePredicateType(ValueParameter, described),
        };

    private static TsArrowFunction Thunk(TsExpression value) =>
        new([], [new TsReturnStatement(value)]);

    private static TsLiteral BooleanLiteral(bool value) => new(value ? "true" : "false");

    /// <summary><c>v === 0 || v === 1 || …</c>; <c>false</c> for an enum without members.</summary>
    private static TsExpression MembershipTest(IReadOnlyList<TsExpression> values) =>
        values.Count == 0
            ? BooleanLiteral(false)
            : values
                .Select(value =>
                    (TsExpression)
                        new TsBinaryExpression(new TsIdentifier(ValueParameter), "===", value)
                )
                .Aggregate((left, right) => new TsBinaryExpression(left, "||", right));

    /// <summary>
    /// Every valid bit combination of a <c>[Flags]</c> enum: an integer with no
    /// bit outside the members' union.
    /// </summary>
    private static TsExpression FlagsTest(IrEnumDeclaration ir)
    {
        var mask = ir.Members.Aggregate(0L, (all, member) =>
            all | Convert.ToInt64(member.Value, CultureInfo.InvariantCulture)
        );

        var value = new TsIdentifier(ValueParameter);

        return new TsBinaryExpression(
            new TsBinaryExpression(
                IrToTsRuntimeTestBridge.TypeofTest(value, "number"),
                "&&",
                new TsCallExpression(
                    new TsPropertyAccess(new TsIdentifier("Number"), "isInteger"),
                    [value]
                )
            ),
            "&&",
            new TsBinaryExpression(
                new TsParenthesized(
                    new TsBinaryExpression(
                        value,
                        "&",
                        new TsLiteral($"~{mask.ToString(CultureInfo.InvariantCulture)}")
                    )
                ),
                "===",
                new TsLiteral("0")
            )
        );
    }
}
