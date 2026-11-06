using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Diagnostics;

/// <summary>
/// Severity of a transpiler diagnostic.
/// </summary>
public enum MetanoDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A transpiler diagnostic — surfaces issues like unsupported language features,
/// unresolved types, or ambiguous constructs at build time, with the original C#
/// source location preserved when available.
/// </summary>
public sealed record MetanoDiagnostic(
    MetanoDiagnosticSeverity Severity,
    string Code,
    string Message,
    Location? Location = null
)
{
    /// <summary>
    /// Formats the diagnostic in Roslyn-compatible style:
    /// path/to/file.cs(line,col): warning MS0001: message
    /// </summary>
    public string Format()
    {
        var severity = Severity switch
        {
            MetanoDiagnosticSeverity.Error => "error",
            MetanoDiagnosticSeverity.Warning => "warning",
            _ => "info",
        };

        if (Location is null)
            return $"{severity} {Code}: {Message}";

        var pos = Location.GetLineSpan();
        var path = pos.Path;
        var line = pos.StartLinePosition.Line + 1;
        var col = pos.StartLinePosition.Character + 1;
        return $"{path}({line},{col}): {severity} {Code}: {Message}";
    }
}

/// <summary>
/// Catalog of diagnostic codes used by the Metano transpiler.
/// </summary>
public static class DiagnosticCodes
{
    /// <summary>MS0001 — A C# language feature is not supported by the transpiler.</summary>
    public const string UnsupportedFeature = "MS0001";

    /// <summary>MS0002 — A referenced type could not be resolved or is not transpilable.</summary>
    public const string UnresolvedType = "MS0002";

    /// <summary>MS0003 — An ambiguous construct that may produce incorrect output.</summary>
    public const string AmbiguousConstruct = "MS0003";

    /// <summary>MS0004 — Conflicting attributes on a single symbol.</summary>
    public const string ConflictingAttributes = "MS0004";

    /// <summary>MS0005 — A cyclic reference exists between generated TypeScript files.</summary>
    public const string CyclicReference = "MS0005";

    /// <summary>MS0006 — Invalid use of [ModuleEntryPoint] (multiple, non-void/Task return,
    /// or has parameters).</summary>
    public const string InvalidModuleEntryPoint = "MS0006";

    /// <summary>MS0007 — Cross-package resolution failure: the name in <c>package.json</c>
    /// diverges from the assembly's <c>[EmitPackage]</c>, OR a consumer references a type
    /// from an assembly that does not declare <c>[EmitPackage]</c> for the active target.</summary>
    public const string CrossPackageResolution = "MS0007";

    /// <summary>MS0008 — Conflicting <c>[EmitInFile]</c> grouping: types sharing the same
    /// file name belong to different namespaces, so the file would have an ambiguous
    /// folder placement.</summary>
    public const string EmitInFileConflict = "MS0008";

    /// <summary>MS0009 — Source frontend failed to load or compile the project (project
    /// file missing, MSBuild workspace failure, null compilation, or language-level
    /// errors reported by Roslyn).</summary>
    public const string FrontendLoadFailure = "MS0009";

    /// <summary>MS0010 — <c>[Optional]</c> (from
    /// <c>Metano.Annotations.TypeScript</c>) was applied to a
    /// non-nullable parameter or property. The attribute relies on JS
    /// <c>undefined</c> collapsing to C# <c>null</c>; a non-nullable
    /// target cannot represent the absent case without undefined
    /// behavior. The fix is to make the C# type nullable
    /// (<c>[Optional] string? Name</c>).</summary>
    public const string OptionalRequiresNullable = "MS0010";

    /// <summary>MS0013 — a transpilable type's surface area (signature
    /// or body) references a type marked <c>[Ignore]</c>. Per the #106
    /// redefinition <c>[Ignore]</c> means ".NET-only"; the transpiler
    /// refuses to emit code that mentions the type so the contract
    /// stays explicit. Migrate the reference to use <c>[External]</c>
    /// (ambient TS shape) or remove the dependency from transpiled
    /// code. Not raised for source-generator output, for an interface
    /// ignored for the emitting target and listed in a base list (erased
    /// from <c>implements</c>/<c>extends</c> on that target, replaced by
    /// its non-ignored base interfaces), or for references inside
    /// attribute applications (never emitted) other than those the
    /// compiler reads for emission, such as
    /// <c>[JsonSerializable(typeof(T))]</c>.</summary>
    public const string IgnoreReferencedByTranspiledCode = "MS0013";

    /// <summary>MS0011 — <c>[Discriminator("FieldName")]</c> (from
    /// <c>Metano.Annotations.TypeScript</c>) refers to a field that
    /// either doesn't exist on the annotated type, isn't a
    /// <c>[StringEnum]</c>, or is nullable. The short-circuit guard
    /// emission relies on the field being a present, non-null
    /// StringEnum so the discriminant check can narrow the rest of
    /// the shape.</summary>
    public const string InvalidDiscriminator = "MS0011";

    /// <summary>MS0012 — <c>[External]</c> (from
    /// <c>Metano.Annotations.TypeScript</c>) was applied to a
    /// non-static class, or combined with <c>[Transpile]</c>. The
    /// attribute marks a stub for runtime globals — the target class
    /// emits no file. Non-static types and combinations with
    /// <c>[Transpile]</c> force the transpiler to simultaneously honor
    /// "no emission" and "full emission", which are incompatible.</summary>
    public const string InvalidExternal = "MS0012";

    /// <summary>MS0014 — <c>[Constant]</c> (from
    /// <c>Metano.Annotations</c>) was applied to a parameter whose
    /// call-site argument is not a compile-time constant, or to a
    /// field that cannot carry the attribute. Parameters accept
    /// Roslyn <c>ConstantValue</c> expressions (literal,
    /// <c>const</c> field/local) and references to another
    /// <c>[Constant]</c>-decorated field whose own initializer has
    /// been validated. Fields must be <c>const</c> or
    /// <c>readonly</c> with a literal-reducible initializer; mutable
    /// fields are rejected so downstream lowering (<c>[Emit]</c>
    /// templates, <c>[Inline]</c> expansion) can substitute the
    /// literal form without a separate analyzer pass.</summary>
    public const string InvalidConstant = "MS0014";

    /// <summary>MS0018 — <c>[This]</c> (from
    /// <c>Metano.Annotations</c>) was applied to a parameter that
    /// cannot act as the JavaScript <c>this</c> receiver. Valid
    /// placement is strictly the first positional parameter of a
    /// delegate or inlinable method, without <c>ref</c> /
    /// <c>out</c> / <c>params</c> modifiers. The diagnostic surfaces
    /// misuses such as applying the attribute to a later parameter,
    /// to a <c>ref</c> / <c>out</c> / <c>params</c> slot, or to a
    /// parameter list whose emitted shape would leave the function
    /// without a receiver to bind to.</summary>
    public const string InvalidThis = "MS0018";

    /// <summary>MS0017 — Stripping the <c>I</c> prefix from an
    /// interface name would collide with another top-level type in
    /// the same namespace. Emitted only when
    /// <c>--strip-interface-prefix</c> / <c>MetanoStripInterfacePrefix</c>
    /// is enabled. The generator falls back to keeping the original
    /// prefixed name so the consumer surface stays compilable;
    /// authors wanting the strip must rename the conflicting type or
    /// override one side with <c>[Name(TypeScript, "…")]</c>.</summary>
    public const string InterfacePrefixCollision = "MS0017";

    /// <summary>MS0016 — <c>[Inline]</c> (from
    /// <c>Metano.Annotations</c>) was applied to an unsupported
    /// shape or its expansion would cycle. Valid targets are
    /// <c>static readonly</c> fields with an initializer and
    /// <c>static</c> properties with an expression-bodied getter;
    /// instance members, mutable fields, methods, and properties
    /// with block-bodied accessors raise the code with a
    /// shape-specific message. Cycles between <c>[Inline]</c>
    /// members (detected by the frontend validator via DFS over
    /// each initializer) raise the code with a cycle message so
    /// downstream substitution stays bounded.</summary>
    public const string InvalidInline = "MS0016";

    /// <summary>MS0015 — <c>[NoContainer]</c> (from
    /// <c>Metano.Annotations</c>) was applied to a non-static class,
    /// or combined with <c>[Transpile]</c>. The attribute marks a
    /// static class whose scope vanishes at the call site — the
    /// class emits no file and member access flattens to a bare
    /// identifier. Non-static targets have no static surface to
    /// flatten, and <c>[Transpile]</c> asks for full emission which
    /// is incompatible with the no-file contract. Member-level
    /// emission contracts inside an <c>[NoContainer]</c> class (plain
    /// bodies projected as top-level exports, <c>[Inline]</c>
    /// expansion) are enforced in a follow-up slice; this code does
    /// not yet surface them.</summary>
    public const string InvalidNoContainer = "MS0015";

    /// <summary>MS0019 — instantiating a generic type parameter via the
    /// <c>new()</c> constraint produces invalid TypeScript because TS
    /// erases generics at runtime, so the parameter is not a callable
    /// constructor. Refactor the API to take an instance or a factory
    /// (<c>Func&lt;T&gt;</c>) instead, then call the factory at the use
    /// site.</summary>
    public const string GenericNewConstraint = "MS0019";

    /// <summary>MS0020 — a <c>[NoContainer]</c> static method's emitted name
    /// (after <c>[Name]</c> resolution, otherwise camelCase) collides
    /// with the TS name of a transpilable type the same emit scope can
    /// see, or with another <c>[NoContainer]</c> factory of the same name
    /// across classes. The collision silently produces broken TypeScript:
    /// the import collector resolves the bare identifier to the factory
    /// instead of the class, the factory body's <c>new ClassName(...)</c>
    /// becomes a recursive call, and consumer files import the function
    /// where they meant the type. Rename the factory via
    /// <c>[Name(\"otherName\")]</c> or drop the override to fall back to
    /// camelCase.</summary>
    public const string NoContainerFactoryNameClash = "MS0020";

    /// <summary>MS0021 — two extension members (classic <c>(this T)</c>
    /// methods or C# 14 <c>extension(T) { … }</c> block members) declared
    /// on different static classes resolve to the same emitted TS helper
    /// name, so the import collector cannot pick which file to import
    /// from on a bare call site. Add <c>[Name("…")]</c> to one of the
    /// members to disambiguate.</summary>
    public const string ExtensionHelperNameClash = "MS0021";

    /// <summary>MS0022 — a local declaration (factory function, class,
    /// alias) shadows an imported symbol with the same name and the
    /// transpiler synthesized an alias to keep both surfaces working.
    /// Pin the alias deterministically with a <c>using NewName = T;</c>
    /// directive (or the upcoming <c>[ImportAlias]</c> attribute) to
    /// silence this notice.</summary>
    public const string AliasedImportConflict = "MS0022";

    /// <summary>MS0023 — an <c>[Emit]</c>-annotated method declares a
    /// parameter with <c>ref</c>, <c>out</c>, <c>in</c>, or <c>ref
    /// readonly</c>. The TypeScript / Dart targets have no notion of
    /// pass-by-reference at the call boundary, so flowing the argument
    /// through the template would silently drop the C# mutation contract.
    /// Drop the modifier and pass / return the value directly, or wrap
    /// the call in a helper that materializes the result.</summary>
    public const string InvalidEmit = "MS0023";

    /// <summary>MS0024 — a lambda body flagged for expression-tree
    /// capture (via <c>[Queryable]</c>, an <c>Expression&lt;Func&lt;…&gt;&gt;</c>
    /// parameter, or an <c>IQueryable&lt;T&gt;</c> receiver) uses a syntax
    /// shape outside the Phase B / #31 MVP subset
    /// (param / capture / literal / member / call / binary / unary /
    /// conditional). Refactor the body into the supported subset, or
    /// drop the queryable surface so the lambda flows through as an
    /// opaque closure.</summary>
    public const string UnsupportedQueryableBody = "MS0024";

    /// <summary>MS0025 — a <c>&lt;MetanoAsset&gt;</c> declared on the
    /// consumer csproj could not be copied to the generated output
    /// directory. Causes include the source file being missing on
    /// disk, the resolved destination escaping the output tree (via a
    /// <c>..</c> segment or a rooted <c>OutputPath</c>), or an I/O
    /// failure during copy. Surfaced as an error so the build fails
    /// fast — a broken manifest silently shipping an incomplete
    /// artifact would be worse than a loud diagnostic.</summary>
    public const string AssetCopyFailure = "MS0025";

    /// <summary>MS0026 — a type used in a JSX-renderable position (a
    /// <c>Children</c> element, a <c>Render()</c> return, the
    /// <c>render</c> entry lambda) cannot be classified as any of: a
    /// component (derives from a <c>[JsxComponentBuilder]</c> base), a
    /// native element (<c>[JsxNativeElement("tag")]</c>), or an imported
    /// renderable (<c>[Import]</c>/<c>[External]</c> typed as / convertible
    /// to the marked <c>JsxElement</c>); or a <c>[JsxComponentBuilder]</c>
    /// base is misapplied (its render method does not return the marked
    /// element type). Carries the offending expression / declaration
    /// location. Mark the type with the appropriate JSX marker, or declare
    /// it as an imported renderable, so the transpiler raises this error
    /// instead of emitting silently-wrong JSX.</summary>
    public const string JsxRenderableUnrecognized = "MS0026";

    /// <summary>MS0027 — <c>[JsTuple]</c> (from
    /// <c>Metano.Annotations.TypeScript</c>) was applied to a type with no
    /// positional shape (a non-positional record or class). Without a
    /// positional constructor there is no field order to map to the array
    /// slots of a JS tuple. The fix is to declare the type as a positional
    /// record (<c>record Signal&lt;T&gt;(Func&lt;T&gt; Getter, …)</c>) so each
    /// member has a stable array index.</summary>
    public const string InvalidJsTuple = "MS0027";

    /// <summary>MS0028 — <c>[JsCallable]</c> (from
    /// <c>Metano.Annotations.TypeScript</c>) was applied to a non-interface,
    /// or to a <c>[JsCallable]</c> interface that declares members other than
    /// <c>Invoke</c>. The attribute models an erased callable facade whose only
    /// operation is invocation; any other member would have no emitted
    /// declaration to bind to. The fix is to move the extra members to a
    /// separate interface or drop <c>[JsCallable]</c>.</summary>
    public const string InvalidJsCallable = "MS0028";

    /// <summary>MS0029 — <c>[EmitPackage(SubpathRoot = …)]</c> is not a dotted C#
    /// namespace. Raised by the producer's TypeScript build (an error, so the build
    /// fails); consumers of such a package fall back to full-namespace subpaths. See
    /// spec 008.</summary>
    public const string InvalidExportsSubpathRoot = "MS0029";

    /// <summary>MS0030 — two namespace barrels claim the same
    /// <c>package.json#exports</c> key under <c>[EmitPackage(SubpathRoot = …)]</c>
    /// (e.g. the root namespace's barrel and the opt-in root aggregation barrel both
    /// claim <c>.</c>). The barrel under the root keeps the key; the error stops the
    /// run because some consumer import would resolve to the wrong barrel. See spec
    /// 008.</summary>
    public const string ExportsSubpathKeyCollision = "MS0030";

    /// <summary>MS0031 — an explicit interface implementation (<c>string IPlain.Name</c>)
    /// cannot be lowered. The targets have no explicit implementations, so the member is
    /// declared under the interface member's name (#278); here another emitted member of
    /// the type or of a base class (an implicit <c>Name</c>, or <c>IOther.Name</c>)
    /// already claims that name. The explicit member is dropped. Raised only for
    /// interfaces of the transpiled contract; a BCL bridge such as
    /// <c>IEnumerable.GetEnumerator</c> is dropped silently. Rename one of them,
    /// implement the interface member implicitly, or mark the explicit member
    /// <c>[Ignore]</c>.</summary>
    public const string ExplicitImplementationConflict = "MS0031";

    /// <summary>MS0032 — a <c>[JsonPolymorphic]</c> / <c>[JsonDerivedType]</c>
    /// configuration the generated TypeScript context cannot honor, checked for each base
    /// the context dispatches through. A registration that is generic, not assignable to
    /// the base (to each construction the context reaches, for a generic base), not
    /// transpiled in its declaring assembly, abstract without
    /// <c>FallBackToNearestAncestor</c>, or a repeated type or tag is an error at its
    /// attribute and is left out of the descriptor. A sealed base, a
    /// <c>[JsonPolymorphic]</c> without registrations, a reserved discriminator
    /// (<c>$id</c>, <c>$ref</c>, <c>$values</c>), or a JSON property of a tagged contract
    /// named like the discriminator or integer-like leaves the base without
    /// <c>polymorphism</c> (spec 009 FR-012, FR-015, FR-047, #268).</summary>
    public const string InvalidJsonPolymorphism = "MS0032";

    /// <summary>MS0033 — a <c>[JsonDerivedType]</c> registration that does not
    /// round-trip, exactly as in System.Text.Json: an untagged registration (written
    /// without a tag, read back as a concrete base or unreadable through an abstract one),
    /// or a derived type that declares its own <c>[JsonDerivedType]</c>s (not re-dispatched
    /// through the outer base). Until spec 009 S6c, also an interface or
    /// <c>[PlainObject]</c> hierarchy emitted without a discriminator. A warning at the
    /// attribute (spec 009 FR-013, #268).</summary>
    public const string LossyJsonPolymorphism = "MS0033";

    /// <summary>MS0034 — a <c>[JsonFactory]</c> the generated context cannot call: on an
    /// instance method, an operator, a method of a type that is not
    /// <c>[Branded]</c> / <c>[InlineWrapper]</c>, a method that does not take exactly one
    /// parameter of the wrapper's underlying type or does not return the containing type,
    /// a generic or overloaded method, a method not emitted for TypeScript
    /// (<c>[Ignore]</c>, <c>[Emit]</c>, <c>[Import]</c>, <c>[Inline]</c>), a method whose
    /// TypeScript name equals a member the companion synthesizes (<c>create</c>,
    /// <c>toString</c>), or a second factory on the same type. An error at the method; the
    /// descriptor keeps the non-validating <c>create</c> (spec 009 FR-022, FR-023,
    /// #269).</summary>
    public const string InvalidJsonFactory = "MS0034";

    /// <summary>MS0035 — a <c>[JsonSerializable]</c> root the generated TypeScript
    /// context cannot describe: an open generic or type parameter, an error type,
    /// <c>object</c> / <c>dynamic</c>, a non-collection interface, an object-kind type
    /// that is not a transpiled class or struct (BCL types, <c>[Ignore]</c> /
    /// <c>[External]</c> types, closed generics, types of non-Metano assemblies), an
    /// array of rank &gt; 1, a dictionary with non-<c>string</c> keys, or a value root
    /// whose element / value type has no spec in the context. An error, reported at
    /// the attribute; the root is not emitted (spec 009 FR-035, #270).</summary>
    public const string InvalidJsonSerializableRoot = "MS0035";

    /// <summary>MS0036 — two context members collide: two distinct types map to one
    /// <c>JsonTypeInfo</c> member name, a name equals a <c>SerializerContext</c> instance
    /// member (<c>options</c>, <c>createSpec</c>, <c>_registry</c>, …), or one type is listed
    /// twice with different <c>TypeInfoPropertyName</c> values. An error when the
    /// losing type is referenced by a <c>ref</c> / <c>base</c> link or the name is
    /// reserved; a warning when the loser is an unreferenced root (which is dropped)
    /// or for the repeated override (the first one is kept). See spec 009 FR-036,
    /// #270.</summary>
    public const string JsonTypeInfoNameCollision = "MS0036";

    /// <summary>MS0037 — the generated TypeScript context does not mirror a System.Text.Json
    /// setting that changes the wire: a <c>[JsonSourceGenerationOptions]</c> option set to a
    /// non-default value (<c>DefaultIgnoreCondition</c>, <c>NumberHandling</c> flags other
    /// than <c>AllowReadingFromString</c>, <c>Converters</c>, …), or a member- or type-level
    /// attribute (<c>[JsonConverter]</c>, <c>[JsonExtensionData]</c>,
    /// <c>[JsonNumberHandling]</c>, <c>[JsonIgnore(Condition = WhenWritingNull)]</c>, a
    /// <c>[JsonInclude]</c> member the source generator cannot access, …). A warning at the
    /// option or member; the output keeps its documented behavior (spec 009 FR-046,
    /// #268).</summary>
    public const string UnmirroredJsonOption = "MS0037";

    /// <summary>MS0044 — without <c>--package-root</c>, the TypeScript CLI found no
    /// <c>package.json</c> inside the output's workspace, and the default root (the
    /// parent of the output directory) lies outside it. An error: the generated files
    /// are written, package.json is not, and the run fails. Pass <c>--package-root</c>
    /// (MSBuild <c>MetanoPackageRoot</c>) or <c>--skip-package-json</c>. The boundary
    /// rules live on <c>PackageRootResolver</c> (#334).</summary>
    public const string PackageRootOutsideWorkspace = "MS0044";

    /// <summary>MS0059 — the <c>--source-condition</c> (MSBuild
    /// <c>MetanoSourceCondition</c>) value is not a usable package.json export condition
    /// name: it contains a character other than letters, digits, <c>-</c>, <c>_</c>,
    /// <c>.</c> and <c>:</c>, it is all digits, it is a Node built-in condition
    /// (<c>import</c>, <c>require</c>, <c>default</c>, <c>types</c>, <c>node</c>,
    /// <c>browser</c>, …), or it is a bundler default (<c>module</c>, <c>development</c>,
    /// <c>production</c>). An error: the run fails before transpiling (#424).</summary>
    public const string InvalidSourceCondition = "MS0059";

    /// <summary>MS0060 — a type's constructors mix accessibility levels that map to
    /// different target modifiers. A TypeScript class has one constructor accessibility
    /// (TS2385), so the constructor is emitted with the most permissive one; the message
    /// names the narrower constructors. A warning (FR-049, #432).</summary>
    public const string MixedConstructorAccessibility = "MS0060";

    /// <summary>MS0061 — a non-public constructor is emitted public because code outside
    /// the class body constructs the type: its <c>TypeSpec</c> in a generated JSON
    /// <c>SerializerContext</c>, or a <c>new</c> written in a nested type, which the
    /// TypeScript target emits outside the container's class body. The message names the
    /// path. A warning (FR-049, #432).</summary>
    public const string ConstructorAccessibilityWidened = "MS0061";

    /// <summary>MS0062 — a generated JSON <c>SerializerContext</c> constructs a class of a
    /// referenced assembly through its <c>TypeSpec</c>, and none of that class's
    /// constructors is public there. The referenced package decided the modifier when it
    /// was transpiled, so the context does not type-check. A warning at the context
    /// (FR-049, #432).</summary>
    public const string JsonContractConstructorNotPublic = "MS0062";

    /// <summary>MS0063 — a TypeScript overload dispatcher (constructor or method) holds
    /// an overload none of whose parameters has a runtime check (each is a type
    /// parameter without a checkable constraint) ahead of a concrete overload whose
    /// argument-count range overlaps it, so calls C# binds to the concrete overload can
    /// reach the catch-all one (ambiguous runtime dispatch). C# ranks such sets at
    /// compile time, sometimes cyclically, so no dispatch order matches it; static
    /// call-site dispatch (#25) will. A warning at the catch-all overload (#440).</summary>
    public const string AmbiguousRuntimeDispatch = "MS0063";

    /// <summary>MS0064 — a member of an enum whose underlying type is <c>long</c>,
    /// <c>ulong</c>, <c>nint</c> or <c>nuint</c> has a value a TypeScript enum member
    /// (a <c>number</c>) cannot hold exactly, or is a <c>[Flags]</c> member of
    /// 2^31 or more, which JS bit operators on <c>number</c> truncate. A warning at
    /// the member (spec 012 FR-058).</summary>
    public const string Int64EnumValuePrecision = "MS0064";

    /// <summary>MS0065 — a 64-bit integer (<c>long</c>, <c>ulong</c>, <c>nint</c>,
    /// <c>nuint</c>; also nullable, array, generic argument or delegate signature)
    /// appears in the signature of an <c>[Import]</c>, <c>[Emit]</c> or
    /// <c>[External]</c> declaration, or of a member a user assembly maps with
    /// <c>[MapMethod]</c> / <c>[MapProperty]</c> (Metano's own
    /// <c>src/Metano/Runtime</c> mappings excluded). The JavaScript side now
    /// exchanges a <c>bigint</c>, and no coercion is inserted. A warning at the
    /// declaration, or at the mapping attribute (spec 012 FR-059).</summary>
    public const string BigIntAtInteropBoundary = "MS0065";

    /// <summary>MS0066 — a constant format passed to <c>ToString</c> on a 64-bit
    /// integer or used in an interpolation hole over one (<c>C</c>, <c>P</c>,
    /// <c>E</c>, <c>B</c>, a custom format), or a constant <c>NumberStyles</c> passed
    /// to <c>long.Parse</c> / <c>TryParse</c> (or the <c>ulong</c> ones), is valid in
    /// .NET but outside what the runtime implements; the call throws
    /// <c>NotSupportedException</c> at run time. A warning (spec 012 FR-037,
    /// FR-038).</summary>
    public const string UnsupportedInt64Format = "MS0066";

    /// <summary>MS0067 — the spec 013 claim pass renamed a generated factory, selector
    /// or fast path (per suffix, <c>$</c> appended until free) because its candidate was
    /// already taken: by a user-written member (reason 1), by an earlier synthesized
    /// name (reason 2) or, for a <c>new</c> method or a derived type's new overload, by
    /// an inherited instance member of any visibility (reason 3). The message names the
    /// candidate, the claimant and the emitted name. A warning at the renamed overload
    /// (spec 013 FR-004).</summary>
    public const string OverloadNameShadowed = "MS0067";

    /// <summary>MS0068 — a user-written member takes a generated name it does not own:
    /// the name of an inherited fast path it does not override (reason 1), or a derived
    /// user static named like an inherited generated factory (reason 2). User names are
    /// never renamed, so the build fails and the message points at <c>[Name]</c>; the
    /// target's message says what the clash breaks. An error at the user member (spec
    /// 013 FR-034).</summary>
    public const string OverloadFastPathHijacked = "MS0068";

    /// <summary>MS0069 — a type implements one generic interface with overload groups
    /// more than once (<c>C : IConv&lt;int&gt;, IConv&lt;string&gt;</c>): each root
    /// fast-path name would need two implementations, so the type keeps runtime dispatch
    /// for those names and emits no per-implementation fast paths. The message adds the
    /// constructions a target cannot tell apart at run time. A warning at the type
    /// (spec 013 FR-036).</summary>
    public const string MultiplyConstructedOverloadGroup = "MS0069";

    /// <summary>MS0070 — one emitted name binds two different imports in one
    /// file: two origin-carrying references staging different packages or
    /// sub-paths (a BCL export and a cross-package type, or two cross-package
    /// types), or an origin-carrying reference and a project type of the same
    /// name. The bare references cannot tell the bindings apart and one of
    /// them is wrong at runtime. An error; rename one of the two with
    /// <c>[Name]</c> (or change the BCL export's <c>ExportedName</c>).</summary>
    public const string ImportedNameClash = "MS0070";

    /// <summary>MS0071 — a user declaration or import takes the name of a runtime
    /// type the module imports and the collision cannot be aliased (<c>Temporal</c>:
    /// its only value-side emission is a mapping template that doesn't declare the
    /// binding, so an alias would break that call while the type references alias).
    /// An error at the module; the user renames the member with <c>[Name]</c> (#484).
    /// </summary>
    public const string RuntimeHelperNameHijacked = "MS0071";
}
