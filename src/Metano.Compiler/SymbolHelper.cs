using Metano.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler;

/// <summary>
/// Target-agnostic helpers for reading Metano attributes from Roslyn symbols and
/// performing common name conversions used by the file system layout (kebab-case).
///
/// Methods that are TypeScript/JavaScript-specific (camelCase identifiers, JS reserved
/// words, [Emit] string templates) live in the TypeScript target instead.
/// </summary>
public static class SymbolHelper
{
    /// <summary>
    /// Stable display format used when the same type symbol must produce the
    /// same string in two unrelated places — for instance, both
    /// <c>DeclarativeMappingRegistry</c> and <c>IrExpressionExtractor</c> key
    /// off the type's "full name" to join a BCL mapping with the call site
    /// that needs it. Pinning the format here keeps the round-trip
    /// deterministic across Roslyn versions and display-option defaults.
    /// </summary>
    public static readonly SymbolDisplayFormat StableTypeFullNameFormat =
        SymbolDisplayFormat.CSharpErrorMessageFormat;

    /// <summary>
    /// Namespace of the TypeScript-target-specific Metano annotations
    /// (<c>[External]</c>, <c>[Optional]</c>, <c>[JsxComponentBuilder]</c>,
    /// <c>[JsxNativeElement]</c>, …). Readers match on this fully-qualified
    /// namespace so unrelated attributes that share a short name (e.g. COM's
    /// <c>System.Runtime.InteropServices.OptionalAttribute</c>) are not
    /// mistaken for the Metano variant.
    /// </summary>
    private const string TypeScriptAnnotationsNamespace = "Metano.Annotations.TypeScript";

    /// <summary>
    /// True when <paramref name="attribute"/> is a Metano TypeScript-target
    /// annotation whose class name matches one of <paramref name="names"/>
    /// (with or without the <c>Attribute</c> suffix) and lives in
    /// <see cref="TypeScriptAnnotationsNamespace"/>.
    /// </summary>
    private static bool IsMetanoTsAttribute(AttributeData attribute, params string[] names)
    {
        var name = attribute.AttributeClass?.Name;
        if (name is null)
            return false;
        if (
            attribute.AttributeClass?.ContainingNamespace?.ToDisplayString()
            != TypeScriptAnnotationsNamespace
        )
            return false;
        foreach (var candidate in names)
        {
            if (name == candidate || name == candidate + "Attribute")
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns the type's open-generic full name in a format stable enough
    /// to use as a dictionary key across the IR pipeline (registry build /
    /// IR origin extraction).
    /// </summary>
    public static string GetStableFullName(this ITypeSymbol type) =>
        type.OriginalDefinition.ToDisplayString(StableTypeFullNameFormat);

    /// <summary>
    /// Returns the key used by
    /// <see cref="IR.IrCompilation.CrossAssemblyOrigins"/> to store an
    /// <see cref="IR.IrTypeOrigin"/> for a type that lives in a referenced
    /// transpilable assembly. The key is assembly-qualified
    /// (<c>"{assemblyName}:{stableFullName}"</c>) so two referenced
    /// assemblies that happen to expose types with identical stable full
    /// names cannot silently clobber each other's origin entry.
    /// </summary>
    public static string GetCrossAssemblyOriginKey(this ITypeSymbol type) =>
        $"{type.ContainingAssembly?.Name ?? string.Empty}:{type.GetStableFullName()}";

    public static bool HasAttribute(this ISymbol symbol, string attributeName)
    {
        return symbol
            .GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name == attributeName
                || a.AttributeClass?.Name == attributeName + "Attribute"
            );
    }

    /// <summary>
    /// True when <paramref name="symbol"/> carries an attribute whose short
    /// name is <paramref name="shortName"/> (with or without the
    /// <c>Attribute</c> suffix) declared in the
    /// <see cref="TypeScriptAnnotationsNamespace"/>. Factors the repeated
    /// namespace-qualified match shared by the TS-annotation predicates.
    /// </summary>
    private static bool HasTypeScriptAttribute(ISymbol symbol, string shortName) =>
        symbol
            .GetAttributes()
            .Any(a =>
                (
                    a.AttributeClass?.Name == shortName
                    || a.AttributeClass?.Name == shortName + "Attribute"
                )
                && a.AttributeClass?.ContainingNamespace?.ToDisplayString()
                    == TypeScriptAnnotationsNamespace
            );

    /// <summary>
    /// Reads a symbol's <c>[Name]</c> override. Multiple <c>[Name]</c>
    /// attributes can coexist on the same symbol — at most one untargeted
    /// plus at most one per <see cref="TargetLanguage"/> — so resolution
    /// picks the best match:
    /// <list type="number">
    ///   <item>A <c>[Name(target, "…")]</c> with a matching target wins.</item>
    ///   <item>Otherwise the untargeted <c>[Name("…")]</c> (if any).</item>
    ///   <item>Otherwise <c>null</c>.</item>
    /// </list>
    /// </summary>
    public static string? GetNameOverride(this ISymbol symbol, TargetLanguage? target = null)
    {
        string? untargeted = null;
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name is not ("NameAttribute" or "Name"))
                continue;
            if (attr.ConstructorArguments.Length == 0)
                continue;

            // Two constructor shapes exist: (string name) → untargeted; and
            // (TargetLanguage target, string name) → per-target. Roslyn surfaces
            // the enum as its backing integer value in ConstructorArguments.
            if (
                attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is string onlyName
            )
            {
                untargeted = onlyName;
                continue;
            }

            if (
                attr.ConstructorArguments.Length >= 2
                && attr.ConstructorArguments[0].Value is int targetValue
                && attr.ConstructorArguments[1].Value is string perTargetName
                && target is TargetLanguage wanted
                && (int)wanted == targetValue
            )
            {
                // Exact match — early return, no need to scan further.
                return perTargetName;
            }
        }
        return untargeted;
    }

    public static bool HasTranspile(this ISymbol symbol) => HasAttribute(symbol, "Transpile");

    public static bool HasStringEnum(this ISymbol symbol) => HasAttribute(symbol, "StringEnum");

    public static bool HasFlags(this ISymbol symbol) =>
        HasAttribute(symbol, "Flags") || HasAttribute(symbol, "FlagsAttribute");

    /// <summary>
    /// Backwards-compatible overload: returns true when <em>any</em> <c>[Ignore]</c>
    /// is present, targeted or not. Callers that know which backend they are
    /// emitting for should prefer the target-aware overload below so a
    /// <c>[Ignore(TargetLanguage.Dart)]</c> doesn't silently suppress a member
    /// on the TS side.
    /// </summary>
    public static bool HasIgnore(this ISymbol symbol) => HasIgnore(symbol, target: null);

    /// <summary>
    /// Target-aware <c>[Ignore]</c> lookup. Returns true when either an
    /// untargeted <c>[Ignore]</c> is present or a <c>[Ignore(target)]</c> for
    /// the given <paramref name="target"/>. Per-target occurrences for a
    /// different target are treated as absent — they do not suppress the
    /// member on the current target.
    /// </summary>
    public static bool HasIgnore(this ISymbol symbol, TargetLanguage? target) =>
        HasTargetableFlag(symbol, "Ignore", target);

    /// <summary>
    /// Shared matcher for per-target "flag" attributes (e.g. <c>[Ignore]</c>)
    /// that carry only an optional <see cref="TargetLanguage"/>.
    /// <para>Match rules:</para>
    /// <list type="bullet">
    ///   <item>An <em>untargeted</em> occurrence (<c>[Attr]</c>) satisfies every
    ///   caller, regardless of <paramref name="target"/>.</item>
    ///   <item>A <em>targeted</em> occurrence (<c>[Attr(target)]</c>) only satisfies
    ///   callers passing the exact same <paramref name="target"/>. A caller
    ///   passing <c>null</c> (target-agnostic queries such as the legacy
    ///   <see cref="IsTranspilable(this ISymbol, bool, IAssemblySymbol?)"/>)
    ///   does <b>not</b> match a targeted occurrence — otherwise
    ///   <c>[Ignore(TargetLanguage.Dart)]</c> would suppress TS discovery too.</item>
    /// </list>
    /// </summary>
    private static bool HasTargetableFlag(
        ISymbol symbol,
        string attributeShortName,
        TargetLanguage? target
    )
    {
        var attributeName = attributeShortName + "Attribute";
        foreach (var attr in symbol.GetAttributes())
        {
            if (
                attr.AttributeClass?.Name != attributeShortName
                && attr.AttributeClass?.Name != attributeName
            )
                continue;

            // Untargeted form — constructor with no args. Matches every caller.
            if (attr.ConstructorArguments.Length == 0)
                return true;

            // Targeted form — single TargetLanguage arg (surfaces as int).
            // Only matches a non-null caller that asked for the same target;
            // target-null callers fall through so a Dart-specific flag cannot
            // poison TS discovery paths (see IsTranspilable).
            if (
                attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is int targetValue
                && target is TargetLanguage wanted
                && (int)wanted == targetValue
            )
                return true;
        }
        return false;
    }

    public static bool HasModule(this ISymbol symbol) => HasAttribute(symbol, "Module");

    public static bool HasExportedAsModule(this ISymbol symbol) =>
        HasAttribute(symbol, "ExportedAsModule");

    public static bool HasImport(this ISymbol symbol) => HasAttribute(symbol, "Import");

    public static bool HasEmit(this ISymbol symbol) => HasAttribute(symbol, "Emit");

    public static bool HasGenerateGuard(this ISymbol symbol) =>
        HasAttribute(symbol, "GenerateGuard");

    public static bool HasModuleEntryPoint(this ISymbol symbol) =>
        HasAttribute(symbol, "ModuleEntryPoint");

    public static bool HasPlainObject(this ISymbol symbol) => HasAttribute(symbol, "PlainObject");

    /// <summary>
    /// Reads <c>[Optional]</c> from the
    /// <c>Metano.Annotations.TypeScript</c> namespace. TS-specific
    /// attribute — callers outside the TS target should treat
    /// <c>true</c> as a no-op (the field stays nullable either way).
    /// Matches on the fully-qualified namespace so the unrelated
    /// <c>System.Runtime.InteropServices.OptionalAttribute</c> (which
    /// shares the same short name and is used by COM interop) is not
    /// mistaken for the Metano variant.
    /// </summary>
    public static bool HasOptional(this ISymbol symbol) =>
        HasTypeScriptAttribute(symbol, "Optional");

    /// <summary>
    /// Reads <c>[External]</c> from the
    /// <c>Metano.Annotations.TypeScript</c> namespace. TS-specific
    /// attribute marking the symbol as runtime-provided — no
    /// declaration is emitted for it. This helper only answers
    /// whether the attribute is present; the exact call-site shape
    /// (flatten vs. class-qualified access, per-member vs. class
    /// scope) is decided by the lowering pipeline. In the current
    /// slice, class-level <c>[External]</c> flattens static member
    /// access at the bridge; per-member lowering ships alongside the
    /// <c>[Ignore]</c> redefinition. Namespace-qualified match so
    /// unrelated <c>[External]</c> attributes from other libraries
    /// are not mistaken for the Metano variant.
    /// </summary>
    public static bool HasExternal(this ISymbol symbol) =>
        HasTypeScriptAttribute(symbol, "External");

    /// <summary>
    /// Reads <c>[JsxComponentBuilder]</c> from the
    /// <c>Metano.Annotations.TypeScript</c> namespace. Marks the abstract
    /// base of a JSX component family (the marker carrier itself, not the
    /// concrete components that derive from it). Namespace-qualified match so
    /// unrelated attributes sharing the short name are not mistaken for the
    /// Metano variant.
    /// </summary>
    public static bool HasJsxComponentBuilder(this ISymbol symbol) =>
        HasTypeScriptAttribute(symbol, "JsxComponentBuilder");

    /// <summary>
    /// Reads the tag name from <c>[JsxNativeElement("tag")]</c> (in the
    /// <c>Metano.Annotations.TypeScript</c> namespace). Returns the
    /// constructor argument when present, or <c>null</c> otherwise. The
    /// attribute class has no <c>Attribute</c> suffix, so the match accepts
    /// both spellings defensively. Namespace-qualified so unrelated
    /// attributes are not mistaken for the Metano variant.
    /// </summary>
    public static string? GetJsxNativeElementTag(this ISymbol symbol)
    {
        var attr = symbol
            .GetAttributes()
            .FirstOrDefault(a => IsMetanoTsAttribute(a, "JsxNativeElement"));
        if (attr is null || attr.ConstructorArguments.Length == 0)
            return null;
        return attr.ConstructorArguments[0].Value as string;
    }

    /// <summary>
    /// Walks the <see cref="INamedTypeSymbol.BaseType"/> chain and returns
    /// <c>true</c> when any base carries <c>[JsxComponentBuilder]</c>. The
    /// type's own attribute is not consulted — a component is recognized by
    /// the marker on one of its bases (the abstract builder), not on itself.
    /// </summary>
    public static bool DerivesFromJsxComponentBuilder(this INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.HasJsxComponentBuilder())
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="type"/> is a JSX <em>component</em> — a type a
    /// JSX backend emits as a function component. The single predicate both the
    /// type-semantics extractor and the type-ref mapper consult.
    /// <para>
    /// Three conditions must all hold:
    /// <list type="bullet">
    ///   <item>it derives (transitively) from a <c>[JsxComponentBuilder]</c>
    ///   base — that is the marker of the component family;</item>
    ///   <item>it is <em>not</em> the abstract marker carrier itself — the base
    ///   that carries <c>[JsxComponentBuilder]</c> is the family root, not an
    ///   emittable component;</item>
    ///   <item>it is <em>not</em> a native element (<c>[JsxNativeElement]</c>) —
    ///   <c>Html.Div</c> derives from the builder base yet must lower to the
    ///   intrinsic <c>&lt;div&gt;</c>, not a <c>&lt;Div/&gt;</c> component.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static bool IsJsxComponent(this INamedTypeSymbol type) =>
        type.DerivesFromJsxComponentBuilder()
        && !type.HasJsxComponentBuilder()
        && type.GetJsxNativeElementTag() is null;

    /// <summary>
    /// True when <paramref name="type"/> is renderable in a JSX value
    /// position: it is a component (derives from a <c>[JsxComponentBuilder]</c>
    /// base), OR it carries <c>[JsxNativeElement]</c>, OR it is an
    /// <c>[Import]</c>/<c>[External]</c>-typed library renderable that is (or
    /// is implicitly convertible to) the marked <c>JsxElement</c> type.
    /// </summary>
    public static bool IsJsxRenderable(this ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && named.DerivesFromJsxComponentBuilder())
            return true;
        if (type.GetJsxNativeElementTag() is not null)
            return true;
        // Imported renderable (FR-022): a library-provided element typed as
        // the marked JsxElement. Recognize when the type itself carries
        // [Import]/[External] and is/derives-from the marked element type.
        if ((type.HasImport() || type.HasExternal()) && IsMarkedJsxElementType(type))
            return true;
        return false;
    }

    /// <summary>
    /// True when <paramref name="member"/> is a JSX element base's
    /// children-collection slot: a property or field whose type is an array (or
    /// array-like collection) of the marked <c>[External] JsxElement</c> type
    /// (e.g. <c>JsxElement[]? Children</c> on the element base or on a
    /// <c>solid-router</c> <c>Route</c>). A JSX backend routes an assignment to
    /// such a member into the element's children — by this resolved shape, never
    /// by the literal member name — so a binding may name its slot anything.
    /// </summary>
    public static bool IsJsxChildrenSlot(this ISymbol member)
    {
        var memberType = member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };
        var elementType = memberType switch
        {
            IArrayTypeSymbol array => array.ElementType,
            INamedTypeSymbol { TypeArguments.Length: 1 } named when named.IsCollectionLike() =>
                named.TypeArguments[0],
            _ => null,
        };
        return elementType is not null && IsMarkedJsxElementType(elementType);
    }

    /// <summary>
    /// v1 recognition heuristic for the abstract element type that
    /// <c>IJsxComponentBuilder&lt;TSelf, TElement&gt;</c> uses as
    /// <c>TElement</c>: a type (or one of its bases) named <c>JsxElement</c>
    /// that carries <c>[External]</c>. Pragmatic on purpose — a precise
    /// resolution of the open generic's <c>TElement</c> argument is deferred
    /// until a binding needs more than the name-plus-marker check.
    /// <para>
    /// Public so the JSX-position validator (MS0026) can ask whether a
    /// <em>position's expected/converted type</em> is the marked element — i.e.
    /// whether the source actually expects a renderable there — before judging
    /// the concrete constructed type's classification.
    /// </para>
    /// </summary>
    public static bool IsMarkedJsxElementType(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.Name == "JsxElement" && current.HasExternal())
                return true;
        }
        return false;
    }

    /// <summary>
    /// Reads <c>[This]</c> from <c>Metano.Annotations</c>. Marks
    /// the first parameter of a delegate (or inlinable method) as
    /// the JavaScript <c>this</c> receiver so backends that support
    /// the rebind can drop the parameter from the positional list
    /// and emit it as the function type's synthetic <c>this</c>
    /// annotation. Namespace-qualified match so unrelated
    /// <c>[This]</c> attributes from other libraries are not
    /// mistaken for the Metano variant.
    /// </summary>
    public static bool HasThis(this ISymbol symbol) =>
        symbol
            .GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name is ("ThisAttribute" or "This")
                && a.AttributeClass?.ContainingNamespace?.ToDisplayString() == "Metano.Annotations"
            );

    /// <summary>
    /// Reads <c>[Inline]</c> from <c>Metano.Annotations</c>. Marks
    /// a <c>static readonly</c> field or a <c>static</c> property
    /// for use-site substitution — every reference to the member is
    /// replaced by its initializer (or getter expression) before
    /// lowering. Namespace-qualified match so unrelated
    /// <c>[Inline]</c> attributes from other libraries are not
    /// mistaken for the Metano variant.
    /// </summary>
    /// <summary>
    /// True when the symbol carries <c>[ObjectArgs]</c> from
    /// <c>Metano.Annotations</c>, asking the transpiler to lower its
    /// parameter list as a single object literal at the TypeScript
    /// surface (JSX-style "props as object"). Applies to methods,
    /// constructors, and classes (class-level form propagates to the
    /// primary constructor).
    /// </summary>
    public static bool HasObjectArgs(this ISymbol symbol) =>
        symbol
            .GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name is ("ObjectArgsAttribute" or "ObjectArgs")
                && a.AttributeClass?.ContainingNamespace?.ToDisplayString() == "Metano.Annotations"
            );

    public static bool HasInline(this ISymbol symbol) =>
        symbol
            .GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name is ("InlineAttribute" or "Inline")
                && a.AttributeClass?.ContainingNamespace?.ToDisplayString() == "Metano.Annotations"
            );

    /// <summary>
    /// Reads the <see cref="Metano.Annotations.InlineMode"/> argument from a
    /// member's <c>[Inline]</c> attribute. Returns
    /// <see cref="Metano.Annotations.InlineMode.Materialize"/> when the
    /// attribute is absent or its constructor argument is missing — matching
    /// the attribute's own default.
    /// </summary>
    public static Metano.Annotations.InlineMode GetInlineMode(this ISymbol symbol)
    {
        var attr = FindInlineAttribute(symbol);
        if (attr is null && symbol.ContainingType is { } containing)
            attr = FindInlineAttribute(containing);
        if (attr is null || attr.ConstructorArguments.Length == 0)
            return Metano.Annotations.InlineMode.Materialize;
        var raw = attr.ConstructorArguments[0].Value;
        return raw is int value
            ? (Metano.Annotations.InlineMode)value
            : Metano.Annotations.InlineMode.Materialize;
    }

    private static AttributeData? FindInlineAttribute(ISymbol symbol) =>
        symbol
            .GetAttributes()
            .FirstOrDefault(a =>
                a.AttributeClass?.Name is ("InlineAttribute" or "Inline")
                && a.AttributeClass?.ContainingNamespace?.ToDisplayString() == "Metano.Annotations"
            );

    /// <summary>
    /// True when the member is effectively inline-marked — either
    /// directly via <c>[Inline]</c> or because its containing static
    /// class carries <c>[Inline]</c> as a propagation directive
    /// (catalog-style classes whose entries all inline). Only static
    /// fields, properties, and methods qualify; the propagation
    /// silently skips members that cannot satisfy <c>[Inline]</c>
    /// (instance members, void methods, multi-statement bodies) and
    /// the validator surfaces those as MS0016 separately.
    /// </summary>
    public static bool IsInlineMember(this ISymbol symbol) =>
        symbol.HasInline() || HasInheritedInlineFromStaticClass(symbol);

    private static bool HasInheritedInlineFromStaticClass(ISymbol symbol)
    {
        // Reduced extension calls (`value.Method()`) surface a non-static
        // method symbol whose `ReducedFrom` points back at the actual
        // static declaration. Unreduce so propagation lookups see the
        // same member identity regardless of call syntax.
        var canonical = symbol is IMethodSymbol method
            ? (ISymbol)(method.ReducedFrom ?? method)
            : symbol;
        if (!canonical.IsStatic)
            return false;
        var containing = canonical.ContainingType;
        if (containing is null || !containing.IsStatic || !containing.HasInline())
            return false;
        return canonical switch
        {
            IFieldSymbol field => field.IsReadOnly && HasFieldInitializerSyntax(field),
            IPropertySymbol property => HasExpressionBodiedGetterSyntax(property),
            IMethodSymbol m
                when m.MethodKind
                    is not (
                        MethodKind.Constructor
                        or MethodKind.StaticConstructor
                        or MethodKind.PropertyGet
                        or MethodKind.PropertySet
                    ) => HasInlinableMethodBodySyntax(m),
            _ => false,
        };
    }

    private static bool HasFieldInitializerSyntax(IFieldSymbol field)
    {
        foreach (var reference in field.DeclaringSyntaxReferences)
        {
            if (
                reference.GetSyntax()
                    is Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax declarator
                && declarator.Initializer is not null
            )
                return true;
        }
        return false;
    }

    private static bool HasExpressionBodiedGetterSyntax(IPropertySymbol property)
    {
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (
                reference.GetSyntax()
                is not Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax decl
            )
                continue;
            if (decl.ExpressionBody is not null)
                return true;
            if (decl.AccessorList is { } accessorList)
            {
                foreach (var accessor in accessorList.Accessors)
                {
                    if (
                        accessor.IsKind(
                            Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration
                        ) && accessor.ExpressionBody is not null
                    )
                        return true;
                }
            }
        }
        return false;
    }

    private static bool HasInlinableMethodBodySyntax(IMethodSymbol method)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (
                reference.GetSyntax()
                is not Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax decl
            )
                continue;
            if (decl.ExpressionBody is not null)
                return true;
            if (
                decl.Body is { Statements: { Count: 1 } statements }
                && statements[0]
                    is Microsoft.CodeAnalysis.CSharp.Syntax.ReturnStatementSyntax
                    {
                        Expression: not null,
                    }
            )
                return true;
        }
        return false;
    }

    /// <summary>
    /// Reads <c>[Constant]</c> from <c>Metano.Annotations</c>. Marks
    /// a parameter or field whose value must be a compile-time
    /// constant literal. Used by downstream lowering
    /// (<c>[Emit]</c> / <c>[Inline]</c>) to guarantee the value is
    /// known at compile time. Namespace-qualified match so unrelated
    /// <c>[Constant]</c> attributes from other libraries are not
    /// mistaken for the Metano variant.
    /// </summary>
    public static bool HasConstant(this ISymbol symbol) =>
        symbol
            .GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name is ("ConstantAttribute" or "Constant")
                && a.AttributeClass?.ContainingNamespace?.ToDisplayString() == "Metano.Annotations"
            );

    /// <summary>
    /// Reads <c>[NoContainer]</c> from <c>Metano.Annotations</c>. Static
    /// class whose scope vanishes at every call site — no
    /// <c>.ts</c> file, and static member access drops the enclosing
    /// class name (<c>HtmlElementType.Div</c> → <c>Div</c>). Members
    /// inside emit per their own attributes (plain body → top-level
    /// function, <c>[External]</c> → ambient, <c>[Emit]</c> → template,
    /// <c>[Inline]</c> → expansion, <c>[Ignore]</c> → dropped).
    /// Subsumes <c>[ExportedAsModule]</c> (deprecated) and fixes the
    /// latent call-site flatten bug.
    /// </summary>
    public static bool HasNoContainer(this ISymbol symbol) =>
        symbol
            .GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name is ("NoContainerAttribute" or "NoContainer")
                && a.AttributeClass?.ContainingNamespace?.ToDisplayString() == "Metano.Annotations"
            );

    /// <summary>
    /// Reads <c>[StrictUnionGuard]</c> from the
    /// <c>Metano.Annotations.TypeScript</c> namespace. When present on
    /// a <c>[GenerateGuard]</c> abstract base, the emitted guard
    /// dispatches per-variant shape validation via the runtime
    /// <c>UnionGuardRegistry</c> instead of relying on the
    /// discriminator-only narrow. Namespace-qualified match so
    /// unrelated <c>[StrictUnionGuard]</c> attributes from other
    /// libraries cannot be mistaken for the Metano variant.
    /// </summary>
    public static bool HasStrictUnionGuard(this ISymbol symbol) =>
        HasTypeScriptAttribute(symbol, "StrictUnionGuard");

    /// <summary>
    /// Reads <c>[JsTuple]</c> from the
    /// <c>Metano.Annotations.TypeScript</c> namespace. Marks a positional
    /// record as a JS array-tuple (the array-shape sibling of
    /// <c>[PlainObject]</c>): standalone it lowers to a tuple type alias
    /// <c>= [T0, T1, …]</c>; combined with <c>[Import]</c> it is erased and
    /// resolves to the imported library tuple. TS-specific — callers outside
    /// the TypeScript target should treat <c>true</c> as a no-op.
    /// Namespace-qualified match so unrelated <c>[JsTuple]</c> attributes
    /// from other libraries are not mistaken for the Metano variant.
    /// </summary>
    public static bool HasJsTuple(this ISymbol symbol) => HasTypeScriptAttribute(symbol, "JsTuple");

    /// <summary>
    /// Reads <c>[JsCallable]</c> from the
    /// <c>Metano.Annotations.TypeScript</c> namespace. Marks an erased
    /// interface modeling a JS callable value; calls to its <c>Invoke(…)</c>
    /// member(s) lower to direct receiver invocation. TS-specific — callers
    /// outside the TypeScript target should treat <c>true</c> as a no-op.
    /// Namespace-qualified match so unrelated <c>[JsCallable]</c> attributes
    /// from other libraries are not mistaken for the Metano variant.
    /// </summary>
    public static bool HasJsCallable(this ISymbol symbol) =>
        HasTypeScriptAttribute(symbol, "JsCallable");

    /// <summary>
    /// The sole member name a <c>[JsCallable]</c> interface may declare — the
    /// conventional .NET call-operation name (mirrors delegate <c>.Invoke</c>).
    /// Shared by the invoke-lowering predicate and the MS0028 validator so the
    /// magic string lives in one place.
    /// </summary>
    public const string JsCallableInvokeMember = "Invoke";

    /// <summary>
    /// True when <paramref name="method"/> is the <c>Invoke</c> member of a
    /// <c>[JsCallable]</c> interface — the shape the expression bridge lowers
    /// to a direct receiver call (<c>recv.Invoke(args)</c> → <c>recv(args)</c>).
    /// Matches by member name (<c>Invoke</c>) plus the
    /// <see cref="HasJsCallable"/> marker on the containing type, so any
    /// overload of <c>Invoke</c> qualifies regardless of arity.
    /// </summary>
    public static bool IsJsCallableInvoke(IMethodSymbol method) =>
        method.Name == JsCallableInvokeMember
        && method.ContainingType is { } containing
        && containing.HasJsCallable();

    /// <summary>
    /// Returns the primary (positional) constructor of <paramref name="type"/>
    /// when it declares one with at least one parameter — the positional shape
    /// <c>[JsTuple]</c> needs. Probes the declaring syntax for a
    /// <c>TypeDeclarationSyntax</c> carrying a <c>ParameterList</c> (records and
    /// C# 12 primary constructors). Returns <c>null</c> when the type has no
    /// positional constructor. Single source of truth for both the
    /// element-index resolution (<see cref="GetJsTupleElementIndex"/>) and the
    /// MS0027 positional-shape validation in <c>CSharpSourceFrontend</c>.
    /// </summary>
    public static IMethodSymbol? GetPositionalPrimaryConstructor(INamedTypeSymbol type) =>
        type.InstanceConstructors.FirstOrDefault(c =>
            c.Parameters.Length > 0
            && c.DeclaringSyntaxReferences.Any(r =>
                r.GetSyntax() is TypeDeclarationSyntax { ParameterList: not null }
            )
        );

    /// <summary>
    /// Resolves the positional index of <paramref name="member"/> within its
    /// <c>[JsTuple]</c> record's primary (positional) constructor. Returns the
    /// zero-based slot index when the containing type carries <c>[JsTuple]</c>
    /// and the member maps to a primary-constructor parameter (by name), or
    /// <c>-1</c> otherwise. The index drives positional element access
    /// (<c>value.Item</c> → <c>value[i]</c>) on a tuple-typed receiver.
    /// </summary>
    public static int GetJsTupleElementIndex(ISymbol member)
    {
        if (member.ContainingType is not { } containing || !containing.HasJsTuple())
            return -1;

        var primaryConstructor = GetPositionalPrimaryConstructor(containing);
        if (primaryConstructor is null)
            return -1;

        for (var i = 0; i < primaryConstructor.Parameters.Length; i++)
        {
            if (
                string.Equals(
                    primaryConstructor.Parameters[i].Name,
                    member.Name,
                    StringComparison.Ordinal
                )
            )
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Reads <c>[Discriminator("FieldName")]</c> from the
    /// <c>Metano.Annotations.TypeScript</c> namespace. Returns the
    /// discriminant field name (original C# casing) when the attribute
    /// is present, or <c>null</c> otherwise. Namespace-qualified match
    /// so unrelated <c>[Discriminator]</c> attributes from other
    /// libraries cannot be mistaken for the Metano variant. Callers
    /// outside the TypeScript target should treat a non-null result as
    /// a no-op (Dart / Kotlin have no equivalent narrowing
    /// convention).
    /// </summary>
    public static string? GetDiscriminatorFieldName(this ISymbol symbol) =>
        symbol
            .GetAttributes()
            .Where(a =>
                a.AttributeClass?.Name is ("DiscriminatorAttribute" or "Discriminator")
                && a.AttributeClass?.ContainingNamespace?.ToDisplayString()
                    == "Metano.Annotations.TypeScript"
            )
            .Select(a =>
                a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as string : null
            )
            .FirstOrDefault(s => s is not null);

    /// <summary>
    /// Reads the file name from <c>[EmitInFile("name")]</c> on a type symbol, or null
    /// when the attribute isn't present (in which case the type takes its own name as
    /// the file).
    /// </summary>
    public static string? GetEmitInFile(this ISymbol symbol)
    {
        var attr = symbol
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name is "EmitInFileAttribute" or "EmitInFile");
        if (attr is null || attr.ConstructorArguments.Length == 0)
            return null;
        return attr.ConstructorArguments[0].Value as string;
    }

    /// <summary>
    /// Reads <c>[ExportVarFromBody("name", AsDefault = ?, InPlace = ?)]</c> from a method
    /// symbol. Returns null when the attribute isn't present.
    /// </summary>
    public static ExportVarFromBodyInfo? GetExportVarFromBody(this ISymbol symbol)
    {
        var attr = symbol
            .GetAttributes()
            .FirstOrDefault(a =>
                a.AttributeClass?.Name is "ExportVarFromBodyAttribute" or "ExportVarFromBody"
            );
        if (attr is null)
            return null;

        var name =
            attr.ConstructorArguments.Length > 0
                ? attr.ConstructorArguments[0].Value?.ToString()
                : null;
        if (name is null)
            return null;

        var asDefault = false;
        var inPlace = false;
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "AsDefault" && named.Value.Value is bool ad)
                asDefault = ad;
            else if (named.Key == "InPlace" && named.Value.Value is bool ip)
                inPlace = ip;
        }

        return new ExportVarFromBodyInfo(name, asDefault, inPlace);
    }

    public sealed record ExportVarFromBodyInfo(string Name, bool AsDefault, bool InPlace);

    /// <summary>
    /// Reads the <c>[assembly: EmitPackage("name", target)]</c> declaration from
    /// <paramref name="assembly"/> for the requested <paramref name="target"/>. Returns
    /// the package info (name + optional version override) on a match, or <c>null</c>
    /// when no matching attribute exists. Multiple <c>[EmitPackage]</c> instances are
    /// supported (one per target); the first one whose <c>Target</c> matches wins.
    /// </summary>
    /// <param name="targetEnumValue">Integer value of the EmitTarget enum (matches the
    /// underlying value the attribute was constructed with). Pass 0 for JavaScript.</param>
    public static EmitPackageInfo? GetEmitPackageInfo(IAssemblySymbol assembly, int targetEnumValue)
    {
        foreach (var attr in assembly.GetAttributes())
        {
            if (attr.AttributeClass?.Name is not ("EmitPackageAttribute" or "EmitPackage"))
                continue;

            // Constructor: (string name, EmitTarget target = JavaScript)
            if (attr.ConstructorArguments.Length == 0)
                continue;
            var name = attr.ConstructorArguments[0].Value as string;
            if (string.IsNullOrEmpty(name))
                continue;

            // Target arg may be omitted (default = JavaScript = 0) or present.
            var target = 0;
            if (attr.ConstructorArguments.Length > 1 && attr.ConstructorArguments[1].Value is int t)
                target = t;
            if (target != targetEnumValue)
                continue;

            string? version = null;
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "Version" && named.Value.Value is string v && v.Length > 0)
                    version = v;
            }

            return new EmitPackageInfo(name, version);
        }
        return null;
    }

    /// <summary>
    /// Convenience overload that returns just the package name (or null) for callers
    /// that don't care about the version override.
    /// </summary>
    public static string? GetEmitPackage(IAssemblySymbol assembly, int targetEnumValue) =>
        GetEmitPackageInfo(assembly, targetEnumValue)?.Name;

    public sealed record EmitPackageInfo(string Name, string? Version);

    /// <summary>
    /// Returns <c>true</c> when the compilation declares
    /// <c>[assembly: TranspileAssembly]</c>. Checks the semantic model
    /// first (covers MSBuild-driven projects) and falls back to walking
    /// the syntax trees for inline test compilations whose attribute may
    /// not yet appear on <see cref="IAssemblySymbol.GetAttributes"/>.
    /// Single source of truth for both the legacy
    /// <c>TypeTransformer</c> and the IR <c>CSharpSourceFrontend</c>.
    /// </summary>
    public static bool HasTranspileAssembly(this Compilation compilation)
    {
        var hasSemanticAttr = compilation
            .Assembly.GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name is "TranspileAssemblyAttribute" or "TranspileAssembly"
            );
        if (hasSemanticAttr)
            return true;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            foreach (var attrList in root.DescendantNodes().OfType<AttributeListSyntax>())
            {
                if (attrList.Target?.Identifier.Text != "assembly")
                    continue;

                foreach (var attr in attrList.Attributes)
                {
                    var name = attr.Name.ToString();
                    if (
                        name
                        is "TranspileAssembly"
                            or "TranspileAssemblyAttribute"
                            or "Metano.TranspileAssembly"
                            or "Metano.TranspileAssemblyAttribute"
                    )
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Reads <c>[Branded]</c> or its predecessor
    /// <c>[InlineWrapper]</c> from <c>Metano.Annotations</c>. Both
    /// attributes mark a value-like struct as a branded primitive
    /// companion on the TS side — identical semantics, different
    /// names for the same shape. Accepting either here lets callers
    /// migrate to <c>[Branded]</c> without breaking pre-existing
    /// <c>[InlineWrapper]</c> usages.
    /// <para>
    /// Namespace-qualified match so unrelated <c>[Branded]</c> or
    /// <c>[InlineWrapper]</c> attributes shipped by third-party
    /// assemblies are not mistaken for the Metano variants — the
    /// short name <c>Branded</c> is generic enough that a collision
    /// would silently rewrite types into branded primitives.
    /// </para>
    /// </summary>
    public static bool HasBranded(this ISymbol symbol) =>
        symbol
            .GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name
                    is (
                        "BrandedAttribute"
                        or "Branded"
                        or "InlineWrapperAttribute"
                        or "InlineWrapper"
                    )
                && a.AttributeClass?.ContainingNamespace?.ToDisplayString() == "Metano.Annotations"
            );

    /// <summary>
    /// Legacy alias preserved so older call sites keep compiling
    /// after the <c>[InlineWrapper]</c> → <c>[Branded]</c> rename.
    /// Delegates to <see cref="HasBranded"/> so adding <c>[Branded]</c>
    /// on a type is equivalent to the historical attribute.
    /// </summary>
    public static bool HasInlineWrapper(this ISymbol symbol) => HasBranded(symbol);

    /// <summary>
    /// Determines if a type should be transpiled, considering:
    /// 1. [Ignore] → always excluded; type is paint-as-.NET-only and
    ///    references from transpiled code raise MS0013.
    /// 2. [External] → excluded from transpilation, but the type stays
    ///    discoverable via the Roslyn semantic model so user code can
    ///    reference it. No .ts file is generated and no import is added —
    ///    it's an ambient declaration over an external library shape.
    /// 3. [Transpile] → always included.
    /// 4. assemblyWideTranspile + public → included.
    /// </summary>
    public static bool IsTranspilable(
        ISymbol symbol,
        bool assemblyWideTranspile = false,
        IAssemblySymbol? currentAssembly = null
    )
    {
        if (HasIgnore(symbol))
            return false;
        // `[External]` is emission-scope "no emit": the class is a
        // stub for runtime globals and must never produce a .ts file.
        // `[NoContainer]` participates in transpilation instead — its
        // members project as top-level exports in a file named after
        // the class, while static member access flattens at the call
        // site. Both are kept separate from `[Ignore]` so the
        // attribute semantics stay explicit at the source-code layer.
        if (HasExternal(symbol))
            return false;
        // `[JsCallable]` interfaces are erased — they model a JS callable
        // value (every `recv.Invoke(args)` lowers to `recv(args)`), not a TS
        // type. They never produce a .ts file and must not be registered as a
        // transpilable type ref (a reference would otherwise import a
        // non-existent module). Per research D9 this holds with or without
        // `[External]`/`[Import]`.
        if (HasJsCallable(symbol))
            return false;
        // C# 11 file-scoped types are emit-time metadata carriers
        // (e.g. `[ImportAlias]` carriers in #181 Stage 3). They never
        // produce a .ts file regardless of the surrounding
        // [Transpile] / [assembly: TranspileAssembly] state.
        if (symbol is INamedTypeSymbol { IsFileLocal: true })
            return false;
        if (HasTranspile(symbol))
            return true;
        // Assembly-wide: only for types in the current compilation's assembly (not BCL/referenced assemblies)
        if (
            assemblyWideTranspile
            && symbol.DeclaredAccessibility == Accessibility.Public
            && (
                currentAssembly is null
                || SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, currentAssembly)
            )
        )
            return true;
        return false;
    }

    /// <summary>
    /// "Author opted this type into transpilation" — it carries
    /// <c>[Transpile]</c>, or the assembly is in <c>[assembly: TranspileAssembly]</c>
    /// mode and the type is public (and not <c>[Ignore]</c>).
    /// <para>
    /// Unlike <see cref="IsTranspilable(ISymbol, bool, IAssemblySymbol?)"/>, this
    /// does <b>not</b> short-circuit on the emission-scope markers
    /// (<c>[External]</c>, <c>[JsCallable]</c>, file-local) that make a type
    /// emit no <c>.ts</c> file. Those markers are exactly the ones whose misuse a
    /// declaration-time validator must still flag — gating such a validator on
    /// <see cref="IsTranspilable"/> would silently suppress its own diagnostic
    /// (a <c>[JsCallable]</c> interface is never <c>IsTranspilable</c>). Use this
    /// predicate to scope attribute validators to author-opted-in types.
    /// </para>
    /// </summary>
    public static bool IsDeclaredTranspilable(
        ISymbol symbol,
        bool assemblyWideTranspile,
        IAssemblySymbol? currentAssembly = null
    )
    {
        if (HasIgnore(symbol))
            return false;
        if (HasTranspile(symbol))
            return true;
        return assemblyWideTranspile
            && symbol.DeclaredAccessibility == Accessibility.Public
            && (
                currentAssembly is null
                || SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, currentAssembly)
            );
    }

    /// <summary>
    /// Reads <c>[Import("name", from: "module", AsDefault = ?, Version = ?)]</c> from
    /// a symbol.
    /// </summary>
    public static ImportInfo? GetImport(this ISymbol symbol)
    {
        var attr = symbol
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name is "ImportAttribute" or "Import");

        if (attr is null)
            return null;

        var name =
            attr.ConstructorArguments.Length > 0
                ? attr.ConstructorArguments[0].Value?.ToString()
                : null;
        var from =
            attr.ConstructorArguments.Length > 1
                ? attr.ConstructorArguments[1].Value?.ToString()
                : null;

        if (name is null || from is null)
            return null;

        var asDefault = false;
        string? version = null;
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "AsDefault" && named.Value.Value is bool ad)
                asDefault = ad;
            else if (named.Key == "Version" && named.Value.Value is string v && v.Length > 0)
                version = v;
        }

        return new ImportInfo(name, from, asDefault, version);
    }

    public sealed record ImportInfo(
        string Name,
        string From,
        bool AsDefault = false,
        string? Version = null
    );

    public static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        if (char.IsLower(name[0]))
            return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static string? NormalizeDivergentName(string? candidate, string anchor) =>
        candidate is not null && !string.Equals(candidate, anchor, StringComparison.Ordinal)
            ? candidate
            : null;

    /// <summary>
    /// Converts PascalCase to kebab-case for file paths.
    /// Examples: "UserId" → "user-id", "InMemoryIssueRepository" → "in-memory-issue-repository",
    /// "IIssueRepository" → "i-issue-repository", "PageRequest" → "page-request".
    /// </summary>
    public static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                // Insert hyphen before any uppercase that follows a lowercase or digit,
                // OR before an uppercase that is followed by a lowercase (acronym boundary).
                var prev = name[i - 1];
                var next = i + 1 < name.Length ? name[i + 1] : '\0';
                var prevIsLowerOrDigit = char.IsLower(prev) || char.IsDigit(prev);
                var nextIsLower = char.IsLower(next);
                if (prevIsLowerOrDigit || (char.IsUpper(prev) && nextIsLower))
                    sb.Append('-');
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Resolves the <see cref="SemanticModel"/> for <paramref name="syntaxTree"/>
    /// against <paramref name="compilation"/>, walking referenced
    /// <see cref="CompilationReference"/>s when the tree belongs to a
    /// dependency's compilation. Cross-project <c>[Inline]</c> initializers
    /// and method bodies live in the referenced project's compilation, not
    /// the consumer's, so a plain
    /// <see cref="Compilation.GetSemanticModel(SyntaxTree, bool)"/> on the
    /// outer compilation throws. Returns <c>null</c> when no compilation in
    /// the graph owns the tree.
    /// </summary>
    public static SemanticModel? TryGetSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
    {
        if (compilation.ContainsSyntaxTree(syntaxTree))
            return compilation.GetSemanticModel(syntaxTree);

        foreach (var reference in compilation.References.OfType<CompilationReference>())
        {
            if (reference.Compilation.ContainsSyntaxTree(syntaxTree))
                return reference.Compilation.GetSemanticModel(syntaxTree);
        }

        return null;
    }
}
