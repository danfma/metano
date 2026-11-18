using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Analysis;

/// <summary>
/// The TypeScript reachability of runtime type descriptors (spec 011 FR-027 as
/// amended by D29): one sequential phase over the frontend's
/// <see cref="IrCompilation.RuntimeQuestions"/>, run before the parallel group
/// transforms so every group lowers against the same answer, and so its
/// diagnostics survive a warm group cache.
/// <list type="bullet">
///   <item>Only a descriptor read reaches: a test, <c>as</c>, cast or overload
///   dispatch of an interface or an enum (TypeScript tests those through
///   <c>isType</c> / <c>asType</c> / <c>castTo</c> with the descriptor),
///   <c>typeof(X)</c>, a <c>GetType()</c> receiver under the receiver rules,
///   <c>[EmitRuntimeType]</c>, every public type of an <c>[EmitPackage]</c>
///   library, and every transpiled exception class. A class test, pattern,
///   cast or dispatch renders <c>instanceof</c> or <c>castTo(x, Ctor)</c>
///   and reaches nothing.</item>
///   <item>The receiver rules: a class or struct <c>C</c> reaches <c>C</c> and
///   every transpiled type derived from it; an enum reaches itself; a
///   transpiled interface reaches its transpiled implementers; a constrained
///   type parameter applies the rule of each constraint; <c>object</c>,
///   <c>dynamic</c>, a BCL class and an unconstrained type parameter reach
///   nothing.</item>
///   <item>The closure: a reached class reaches its whole transpiled base chain;
///   a reached interface reaches its transpiled base interfaces and every
///   transpiled class whose <c>AllInterfaces</c> contains it.</item>
/// </list>
/// It reports <c>MS0045</c> (a delegate test), <c>MS0048</c> (a
/// <c>GetType()</c> that may meet a plain object), <c>MS0055</c> (an unchecked
/// cast) and <c>MS0001</c> for a <c>typeof</c> with no descriptor.
/// </summary>
internal sealed class RuntimeTypeReachability
{
    private const string UnsafeCastHint =
        "Use x.UnsafeCast<T>() to state that the cast is intentionally unchecked.";

    private readonly Compilation _compilation;
    private readonly Action<MetanoDiagnostic> _report;
    private readonly IReadOnlyList<DescribableType> _types;
    private readonly Dictionary<SupertypeKey, DescribableType> _typesByKey;
    private readonly IReadOnlyList<INamedTypeSymbol> _plainObjects;
    private readonly List<SupertypeKey> _reachedOrder = [];
    private readonly HashSet<SupertypeKey> _reached = [];
    private readonly HashSet<SupertypeKey> _undescribable = [];

    private RuntimeTypeReachability(
        Compilation compilation,
        IReadOnlyList<INamedTypeSymbol> emittedTypes,
        Action<MetanoDiagnostic> report
    )
    {
        _compilation = compilation;
        _report = report;
        _types = [.. emittedTypes.Select(DescribableType.Of).OfType<DescribableType>()];
        _typesByKey = _types
            .GroupBy(type => type.Key)
            .ToDictionary(group => group.Key, group => group.First());
        _plainObjects = [.. emittedTypes.Where(SymbolHelper.HasPlainObject)];
    }

    /// <summary>
    /// Computes the reached descriptors of <paramref name="ir"/>'s questions over
    /// <paramref name="emittedTypes"/> (every type this transform emits, nested
    /// ones included), reporting the phase's diagnostics to
    /// <paramref name="report"/>.
    /// </summary>
    public static RuntimeTypeReachabilityResult Compute(
        IrCompilation ir,
        Compilation compilation,
        IReadOnlyList<INamedTypeSymbol> emittedTypes,
        Action<MetanoDiagnostic> report
    )
    {
        var analysis = new RuntimeTypeReachability(compilation, emittedTypes, report);

        analysis.ReachRoots(ir);
        analysis.Close();

        return analysis.ToResult(ir);
    }

    private void ReachRoots(IrCompilation ir)
    {
        foreach (var question in ir.RuntimeQuestions)
            Answer(question);

        var assemblyForcesAll = HasEmitRuntimeType(_compilation.Assembly);
        var isLibrary = ir.PackageName is not null;

        foreach (var type in _types)
        {
            if (
                type.IsException
                || HasEmitRuntimeType(type.Symbol)
                || ((assemblyForcesAll || isLibrary) && IsPublic(type.Symbol))
            )
                Reach(type);
        }
    }

    private void Answer(IrRuntimeQuestion question)
    {
        switch (question.Kind)
        {
            case IrRuntimeQuestionKind.Test or IrRuntimeQuestionKind.As:
                ReportDelegateTest(question);
                ReachTestedType(question.Type);
                break;
            case IrRuntimeQuestionKind.OverloadParameter:
                ReachTestedType(question.Type);
                break;
            case IrRuntimeQuestionKind.Cast:
                ReportUncheckedCast(question);
                ReachTestedType(question.Type);
                break;
            case IrRuntimeQuestionKind.TypeOf:
                ReachTypeOf(question);
                break;
            case IrRuntimeQuestionKind.GetType:
                ReachReceiver(question);
                break;
        }
    }

    /// <summary>
    /// TypeScript tests an interface and an enum through their descriptor; a
    /// class, struct or record tests by <c>instanceof</c> and reaches nothing.
    /// An interface of a referenced library has its descriptor there; the
    /// classes of this compilation that implement it are reached, so their
    /// descriptors list it.
    /// </summary>
    private void ReachTestedType(IrQuestionedType tested)
    {
        if (DescribableOf(tested) is { Kind: not DescriptorKind.Class } type)
        {
            Reach(type);

            return;
        }

        if (LibraryInterfaceOf(tested) is not { } iface)
            return;

        foreach (var implementer in ImplementersOf(iface))
            Reach(implementer);
    }

    private INamedTypeSymbol? LibraryInterfaceOf(IrQuestionedType tested) =>
        tested is { Shape: IrQuestionedTypeShape.Named, Key: { } key }
        && IrTypeKeys.Find(key, _compilation) is { TypeKind: TypeKind.Interface } iface
        && HasRuntimeDescriptor(iface)
            ? iface
            : null;

    private void ReachTypeOf(IrRuntimeQuestion question)
    {
        if (question.Type is not { Shape: IrQuestionedTypeShape.Named, Key: { } key })
            return;

        if (DescribableOf(question.Type) is { } type)
        {
            Reach(type);

            return;
        }

        if (question.Type.CollectionDefinitionName is not null)
            return;

        if (IrTypeKeys.Find(key, _compilation) is { } symbol && !HasRuntimeDescriptor(symbol))
            ReportUndescribableTypeOf(symbol, question.Location);
    }

    /// <summary>
    /// A type outside this compilation has a descriptor when an
    /// <c>[EmitPackage]</c> library emits it (D29: libraries are complete) or
    /// when it is an exception with a runtime class.
    /// </summary>
    private bool HasRuntimeDescriptor(INamedTypeSymbol type) =>
        !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _compilation.Assembly)
            && type.ContainingAssembly.HasTranspileAssembly()
        || IsException(type);

    private void ReportUndescribableTypeOf(INamedTypeSymbol type, Location? location)
    {
        _undescribable.Add(KeyOf(type));

        _report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'typeof({type.ToDisplayString()})' has no runtime type descriptor in TypeScript: "
                    + "only transpiled classes, structs, interfaces and enums, the primitives and "
                    + "the runtime exceptions have one (spec 011 FR-025).",
                location
            )
        );
    }

    private void ReachReceiver(IrRuntimeQuestion question)
    {
        var receiver = question.Type;

        switch (receiver.Shape)
        {
            case IrQuestionedTypeShape.Named:
                ReachNamedReceiver(receiver);
                break;
            case IrQuestionedTypeShape.TypeParameter when receiver.Constraints.Count > 0:
                foreach (var constraint in receiver.Constraints)
                    ReachNamedReceiver(
                        new IrQuestionedType
                        {
                            Shape = IrQuestionedTypeShape.Named,
                            Key = constraint,
                        }
                    );

                break;
            case IrQuestionedTypeShape.TypeParameter
            or IrQuestionedTypeShape.Object
            or IrQuestionedTypeShape.Dynamic:
                ReportPlainObjectReceiver(question);
                break;
        }

        if (IsPlainObjectInterface(receiver))
            ReportPlainObjectReceiver(question);
    }

    private void ReachNamedReceiver(IrQuestionedType receiver)
    {
        if (DescribableOf(receiver) is not { } type)
            return;

        switch (type.Kind)
        {
            case DescriptorKind.Class:
                foreach (var derived in _types.Where(candidate => candidate.DerivesFrom(type)))
                    Reach(derived);

                break;
            case DescriptorKind.Enum:
                Reach(type);
                break;
            case DescriptorKind.Interface:
                foreach (var implementer in ImplementersOf(type))
                    Reach(implementer);

                break;
        }
    }

    private bool IsPlainObjectInterface(IrQuestionedType receiver) =>
        receiver.Key is { } key
        && IrTypeKeys.Find(key, _compilation) is { TypeKind: TypeKind.Interface } iface
        && _plainObjects.Any(plain =>
            plain.AllInterfaces.Contains(iface, SymbolEqualityComparer.Default)
        );

    private void ReportPlainObjectReceiver(IrRuntimeQuestion question)
    {
        if (_plainObjects.Count == 0)
            return;

        _report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.RuntimeTypeOfPlainObject,
                "GetType() on a value whose static type may hold a [PlainObject] answers "
                    + "'System.Object' for a plain object: a plain object has no runtime identity "
                    + "(spec 011 FR-020).",
                question.Location
            )
        );
    }

    private void ReportDelegateTest(IrRuntimeQuestion question)
    {
        if (question.Type.Shape is not IrQuestionedTypeShape.Delegate)
            return;

        _report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnanswerableRuntimeTypeTest,
                "A delegate type has no runtime type test in TypeScript: a function carries no "
                    + "signature to test (spec 011 FR-021).",
                question.Location
            )
        );
    }

    private void ReportUncheckedCast(IrRuntimeQuestion question)
    {
        var reason = question.Type switch
        {
            { Shape: IrQuestionedTypeShape.Array, ElementIsObject: false } =>
                "an array's element type is erased at runtime",
            { Shape: IrQuestionedTypeShape.Delegate } =>
                "a delegate type has no runtime descriptor",
            { Shape: IrQuestionedTypeShape.TypeParameter } =>
                "a type parameter that is not reified has no runtime descriptor",
            _ => null,
        };

        if (reason is null)
            return;

        _report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UncheckedCast,
                $"The cast is emitted unchecked because {reason} (spec 011 FR-026). "
                    + UnsafeCastHint,
                question.Location
            )
        );
    }

    private void Reach(DescribableType type)
    {
        if (_reached.Add(type.Key))
            _reachedOrder.Add(type.Key);
    }

    /// <summary>
    /// The closure, breadth-first in reaching order: base chains, base
    /// interfaces and the implementers of every reached interface.
    /// </summary>
    private void Close()
    {
        for (var index = 0; index < _reachedOrder.Count; index++)
        {
            var type = _typesByKey[_reachedOrder[index]];

            foreach (var linked in LinkedTypesOf(type))
                Reach(linked);
        }
    }

    private IEnumerable<DescribableType> LinkedTypesOf(DescribableType type) =>
        type.Kind switch
        {
            DescriptorKind.Class => BasesOf(type),
            DescriptorKind.Interface => BaseInterfacesOf(type).Concat(ImplementersOf(type)),
            _ => [],
        };

    private IEnumerable<DescribableType> BasesOf(DescribableType type)
    {
        for (var current = type.Symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (DescribableOf(current) is { } describable)
                yield return describable;
        }
    }

    private IEnumerable<DescribableType> BaseInterfacesOf(DescribableType type) =>
        type.Symbol.AllInterfaces.Select(DescribableOf).OfType<DescribableType>();

    private IEnumerable<DescribableType> ImplementersOf(DescribableType iface) =>
        ImplementersOf(iface.Symbol);

    /// <summary>
    /// The classes of this compilation whose <c>AllInterfaces</c> contain
    /// <paramref name="iface"/>, constructed or not.
    /// </summary>
    private IEnumerable<DescribableType> ImplementersOf(INamedTypeSymbol iface) =>
        _types.Where(candidate =>
            candidate.Kind is DescriptorKind.Class
            && candidate.Symbol.AllInterfaces.Any(implemented =>
                SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, iface)
            )
        );

    private RuntimeTypeReachabilityResult ToResult(IrCompilation ir) =>
        new()
        {
            Reached = _reached,
            PlainImplementers = PlainImplementersOfReachedInterfaces(),
            QuestionedCollectionTypes = ir.QuestionedCollectionTypes,
            OverridingGetters = OverridingGetters(),
            Undescribable = _undescribable,
        };

    private Dictionary<
        SupertypeKey,
        IReadOnlyList<SupertypeKey>
    > PlainImplementersOfReachedInterfaces() =>
        _reachedOrder
            .Select(key => _typesByKey[key])
            .Where(type => type.Kind is DescriptorKind.Interface)
            .ToDictionary(
                type => type.Key,
                type =>
                    (IReadOnlyList<SupertypeKey>)
                        _plainObjects
                            .Where(plain =>
                                plain.AllInterfaces.Contains(
                                    type.Symbol,
                                    SymbolEqualityComparer.Default
                                )
                            )
                            .Select(KeyOf)
                            .ToList()
            );

    /// <summary>
    /// A reached class overrides its base's getter when the base has one: a
    /// transpiled base (reached with the whole chain), a library class, or a
    /// runtime exception class.
    /// </summary>
    private HashSet<SupertypeKey> OverridingGetters() =>
        _reachedOrder
            .Select(key => _typesByKey[key])
            .Where(type =>
                type.Kind is DescriptorKind.Class
                && type.Symbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType
                && (DescribableOf(baseType) is not null || HasRuntimeDescriptor(baseType))
            )
            .Select(type => type.Key)
            .ToHashSet();

    private DescribableType? DescribableOf(IrQuestionedType questioned) =>
        questioned is { Shape: IrQuestionedTypeShape.Named, Key: { } key }
        && IrTypeKeys.Find(key, _compilation) is { } symbol
            ? DescribableOf(symbol)
            : null;

    private DescribableType? DescribableOf(INamedTypeSymbol symbol) =>
        SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, _compilation.Assembly)
        && _typesByKey.TryGetValue(KeyOf(symbol), out var type)
            ? type
            : null;

    private static bool HasEmitRuntimeType(ISymbol symbol) =>
        symbol
            .GetAttributes()
            .Any(attribute => attribute.AttributeClass?.Name is nameof(EmitRuntimeTypeAttribute));

    private static bool IsPublic(INamedTypeSymbol type)
    {
        for (
            ISymbol? current = type;
            current is INamedTypeSymbol named;
            current = named.ContainingType
        )
        {
            if (named.DeclaredAccessibility is not Accessibility.Public)
                return false;
        }

        return true;
    }

    internal static bool IsException(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == "System.Exception")
                return true;
        }

        return false;
    }

    /// <summary>The reached-set key of <paramref name="type"/> (<see cref="IrNamedTypeRef.SupertypeKey"/>).</summary>
    internal static SupertypeKey KeyOf(INamedTypeSymbol type) =>
        IrTypeRefMapper.SupertypeKeyOf(type, TargetLanguage.TypeScript);

    /// <summary>The kind of descriptor a transpiled type carries.</summary>
    internal enum DescriptorKind
    {
        Class,
        Interface,
        Enum,
    }

    /// <summary>
    /// A type of this compilation that TypeScript can describe: a class,
    /// record or struct emitted as a class, an emitted interface or an enum.
    /// <c>[PlainObject]</c> types (stage 011b B2), branded wrappers,
    /// <c>[JsTuple]</c>s, static classes and types with no emitted declaration
    /// have none.
    /// </summary>
    private sealed record DescribableType(INamedTypeSymbol Symbol, DescriptorKind Kind)
    {
        public SupertypeKey Key { get; } = KeyOf(Symbol);

        public bool IsException =>
            Kind is DescriptorKind.Class && RuntimeTypeReachability.IsException(Symbol);

        public bool DerivesFrom(DescribableType other)
        {
            for (
                INamedTypeSymbol? current = Symbol;
                current is not null;
                current = current.BaseType
            )
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, other.Symbol))
                    return true;
            }

            return false;
        }

        public static DescribableType? Of(INamedTypeSymbol type) =>
            DescriptorKindOf(type) is { } kind
                ? new DescribableType(type.OriginalDefinition, kind)
                : null;

        private static DescriptorKind? DescriptorKindOf(INamedTypeSymbol type)
        {
            if (HasNoEmittedDeclaration(type))
                return null;

            return type.TypeKind switch
            {
                TypeKind.Enum => DescriptorKind.Enum,
                TypeKind.Interface
                    when !SymbolHelper.HasJsCallable(type) && !SymbolHelper.HasExternal(type) =>
                    DescriptorKind.Interface,
                TypeKind.Class or TypeKind.Struct when IsEmittedAsClass(type) =>
                    DescriptorKind.Class,
                _ => null,
            };
        }

        private static bool HasNoEmittedDeclaration(INamedTypeSymbol type) =>
            SymbolHelper.HasImport(type)
            || SymbolHelper.HasIgnore(type, TargetLanguage.TypeScript)
            || SymbolHelper.HasAttribute(type, "NoEmit");

        private static bool IsEmittedAsClass(INamedTypeSymbol type) =>
            !type.IsStatic
            && !SymbolHelper.HasPlainObject(type)
            && !SymbolHelper.HasJsTuple(type)
            && !SymbolHelper.HasBranded(type)
            && !SymbolHelper.HasInlineWrapper(type)
            && !SymbolHelper.HasExternal(type)
            && !SymbolHelper.IsJsxComponent(type);
    }
}
