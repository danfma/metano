using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Bridge;
using Metano.Compiler.TypeScript.Transformation;
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
internal sealed partial class RuntimeTypeReachability
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
    private readonly HashSet<SupertypeKey> _guarded = [];
    private readonly HashSet<SupertypeKey> _foldedPlainObjects = [];
    private readonly bool _boxingEnabled;
    private readonly IReadOnlyList<INamedTypeSymbol> _boxableValueTypes;
    private bool _hasBoxingConversion;

    private readonly Dictionary<
        SupertypeKey,
        IReadOnlyList<SupertypeKey>
    > _libraryPlainImplementers = [];

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
        _boxingEnabled = !compilation.Assembly.HasAttribute(nameof(DisableBoxingAttribute));

        _boxableValueTypes =
        [
            .. emittedTypes.Where(type =>
                type.TypeKind is TypeKind.Enum || SymbolHelper.HasInlineWrapper(type)
            ),
        ];
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
        Action<MetanoDiagnostic> report,
        JsonReachabilityInputs? json = null
    )
    {
        var analysis = new RuntimeTypeReachability(compilation, emittedTypes, report);

        analysis.ReachRoots(ir);
        analysis.ReachJsonRoots(json?.ConstructedClasses ?? []);
        analysis.ReachCollectionConstructions(ir, json?.CollectionTypes ?? []);
        analysis.ReportUnanswerableInterfaceTests(ir);
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
        ReportUnboxedTypeQuestion(question);

        switch (question.Kind)
        {
            case IrRuntimeQuestionKind.Test:
                ReportDelegateTest(question);
                ReportErasedElementTest(question);
                AnswerCollectionTest(question);
                ReachTestedType(question, DescriptorRead.None);
                break;
            case IrRuntimeQuestionKind.As:
                ReportDelegateTest(question);
                ReportErasedElementTest(question);
                AnswerCollectionTest(question);
                ReachTestedType(question, DescriptorRead.Enum);
                break;
            case IrRuntimeQuestionKind.OverloadParameter:
                ReachTestedType(question, DescriptorRead.None);
                break;
            case IrRuntimeQuestionKind.Cast:
                ReportUncheckedCast(question);
                ReachTestedType(question, DescriptorRead.EnumAndPlainObject);
                break;
            case IrRuntimeQuestionKind.TypeOf:
                ReachTypeOf(question);
                break;
            case IrRuntimeQuestionKind.GetType:
                ReachReceiver(question);
                break;
            case IrRuntimeQuestionKind.Box when _boxingEnabled:
                AnswerBox(question);
                break;
        }
    }

    /// <summary>
    /// A boxing conversion (FR-050): to <c>object</c>, <c>ValueType</c> or
    /// <c>Enum</c>, an enum with a descriptor is boxed with it, so the
    /// conversion reaches it; an enum without one, or any conversion to an
    /// interface, stays raw and reports <c>MS0058</c> reason 2 where a type
    /// question reads the value.
    /// </summary>
    private void AnswerBox(IrRuntimeQuestion question)
    {
        var toInterface = question.InputType?.AcceptsBoxedValues is not true;

        if (BoxedEnumOf(question.Type) is not { } boxed)
            return;

        if (toInterface)
        {
            ReportUnboxedConversion(question, "a conversion to an interface stays raw");

            return;
        }

        if (DescribableOf(boxed) is { } described)
        {
            Reach(described);
            _hasBoxingConversion = true;

            return;
        }

        if (HasRuntimeDescriptor(boxed))
        {
            _hasBoxingConversion = true;

            return;
        }

        ReportUnboxedConversion(question, $"'{boxed.ToDisplayString()}' has no runtime descriptor");
    }

    /// <summary>
    /// Whether a tested value of static type <paramref name="input"/> may hold a
    /// box with boxing on (<c>object</c>, <c>ValueType</c>, <c>Enum</c>, an
    /// interface, a type parameter): an enum test then reads the enum's
    /// descriptor (FR-050).
    /// </summary>
    private bool MayHoldBox(IrQuestionedType? input) =>
        _boxingEnabled
        && input is not null
        && input.Shape is not IrQuestionedTypeShape.Dynamic
        && (
            input.AcceptsBoxedValues
            || input.Shape is IrQuestionedTypeShape.TypeParameter
            || (
                input is { Shape: IrQuestionedTypeShape.Named, Key: { } key }
                && IrTypeKeys.Find(key, _compilation) is { TypeKind: TypeKind.Interface }
            )
        );

    private INamedTypeSymbol? BoxedEnumOf(IrQuestionedType boxed) =>
        boxed is { Shape: IrQuestionedTypeShape.Named, Key: { } key }
        && IrTypeKeys.Find(key, _compilation) is { TypeKind: TypeKind.Enum } symbol
            ? symbol
            : null;

    private void ReportUnboxedConversion(IrRuntimeQuestion question, string reason)
    {
        if (!question.FeedsTypeQuestion)
            return;

        _report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.TypeQuestionOnUnboxedValue,
                $"'{question.Text}' is not boxed ({reason}), so a type question on its value "
                    + "answers for the raw value (spec 011 FR-050).",
                LocationOf(question)
            )
        );
    }

    /// <summary>
    /// <c>MS0058</c> reason 1: with <c>[assembly: DisableBoxing]</c>, a
    /// <c>GetType()</c>, or a test, <c>as</c> or cast of an enum, a primitive or an
    /// interface, on a value whose static type may hold a raw enum or wrapper of
    /// the compilation.
    /// </summary>
    private void ReportUnboxedTypeQuestion(IrRuntimeQuestion question)
    {
        if (_boxingEnabled || _boxableValueTypes.Count == 0)
            return;

        var receiver = question.Kind switch
        {
            IrRuntimeQuestionKind.GetType => question.Type,
            IrRuntimeQuestionKind.Test
            or IrRuntimeQuestionKind.As
            or IrRuntimeQuestionKind.Cast when MayBeRawValueTest(question.Type) =>
                question.InputType,
            _ => null,
        };

        if (receiver is null || !MayHoldRawValue(receiver))
            return;

        _report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.TypeQuestionOnUnboxedValue,
                $"'{question.Text ?? "GetType()"}' may meet an enum or [InlineWrapper] value that "
                    + "carries no runtime type: [assembly: DisableBoxing] keeps those values raw, "
                    + "so the answer is the raw value's (spec 011 FR-050).",
                LocationOf(question)
            )
        );
    }

    private bool MayBeRawValueTest(IrQuestionedType tested) =>
        tested.Shape is IrQuestionedTypeShape.Primitive
        || (
            tested is { Shape: IrQuestionedTypeShape.Named, Key: { } key }
            && IrTypeKeys.Find(key, _compilation)
                is { TypeKind: TypeKind.Enum or TypeKind.Interface }
                    or { IsValueType: true }
        );

    private bool MayHoldRawValue(IrQuestionedType receiver) =>
        receiver.AcceptsBoxedValues
        || (
            receiver is { Shape: IrQuestionedTypeShape.Named, Key: { } key }
            && IrTypeKeys.Find(key, _compilation) is { TypeKind: TypeKind.Interface } iface
            && _boxableValueTypes.Any(type =>
                type.AllInterfaces.Contains(iface, SymbolEqualityComparer.Default)
            )
        );

    /// <summary>
    /// TypeScript tests an interface through its descriptor everywhere. An
    /// enum is read only where a cast or <c>as</c> checks the value with its
    /// descriptor, a <c>[PlainObject]</c> only where a cast does (<paramref name="read"/>);
    /// every other test renders inline (the enum's value test, the plain
    /// object's synthesized guard, which the test makes the compilation emit).
    /// A class, struct or record tests by <c>instanceof</c> and reaches nothing.
    /// An interface of a referenced library has its descriptor there; the
    /// classes of this compilation that implement it are reached, so their
    /// descriptors list it, and its <c>[PlainObject]</c> implementers, which the
    /// test site passes to <c>withImplementers</c>.
    /// </summary>
    private void ReachTestedType(IrRuntimeQuestion question, DescriptorRead read)
    {
        if (DescribableOf(question.Type) is { } type)
        {
            ReachTested(type, question, read);

            return;
        }

        if (LibraryInterfaceOf(question.Type) is not { } iface)
            return;

        foreach (var implementer in ImplementersOf(iface))
            Reach(implementer);

        var plainImplementers = PlainImplementersOf(iface).ToList();

        foreach (var plain in plainImplementers)
            Reach(plain);

        if (plainImplementers.Count > 0)
            _libraryPlainImplementers[KeyOf(iface)] = [.. plainImplementers.Select(p => p.Key)];
    }

    private void ReachTested(DescribableType type, IrRuntimeQuestion question, DescriptorRead read)
    {
        switch (type.Kind)
        {
            case DescriptorKind.Interface:
            case DescriptorKind.Enum when read is not DescriptorRead.None:
            case DescriptorKind.Enum when MayHoldBox(question.InputType):
                Reach(type);
                break;
            case DescriptorKind.PlainObject:
                GuardPlainObject(type, question);

                if (read is DescriptorRead.EnumAndPlainObject)
                    Reach(type);

                break;
        }
    }

    /// <summary>
    /// A tested <c>[PlainObject]</c> is recognized by its synthesized guard
    /// (FR-020), which the compilation then emits; one that can get no guard
    /// reports <c>MS0045</c>.
    /// </summary>
    private void GuardPlainObject(DescribableType type, IrRuntimeQuestion? question)
    {
        if (TypeGuardBuilder.CanHaveGuard(type.Symbol))
        {
            _guarded.Add(type.Key);

            return;
        }

        _report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnanswerableRuntimeTypeTest,
                $"'{type.Symbol.ToDisplayString()}' is a [PlainObject] that gets no structural "
                    + "guard, so no runtime test can recognize it (spec 011 FR-020).",
                question is null ? type.Symbol.Locations.FirstOrDefault() : LocationOf(question)
            )
        );
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
            ReachTypeArguments(question.Type);

            return;
        }

        if (question.Type.CollectionDefinitionName is not null)
        {
            ReachTypeArguments(question.Type);

            return;
        }

        if (IrTypeKeys.Find(key, _compilation) is { } symbol && !HasRuntimeDescriptor(symbol))
            ReportUndescribableTypeOf(symbol, LocationOf(question));
    }

    /// <summary>
    /// A closed generic's descriptor read specializes the open descriptor with
    /// the arguments' (FR-023), so a describable argument is reached too; an
    /// argument with none specializes to the <c>UnknownType</c> wildcard.
    /// </summary>
    private void ReachTypeArguments(IrQuestionedType type)
    {
        foreach (var argument in type.TypeArguments)
        {
            if (DescribableOf(argument) is { } describable)
                Reach(describable);

            ReachTypeArguments(argument);
        }
    }

    /// <summary>
    /// A type outside this compilation has a descriptor when it emits a
    /// declaration that carries one: a public type of an <c>[EmitPackage]</c>
    /// library (D29: libraries are complete) or an exception with a runtime
    /// class; never an <c>[Import]</c>, <c>[External]</c> or ignored type, nor
    /// one shaped without a class (a plain object, a branded wrapper).
    /// </summary>
    private bool HasRuntimeDescriptor(INamedTypeSymbol type) =>
        !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _compilation.Assembly)
        && DescribableType.Of(type) is not null
        && (
            IsException(type) || (type.ContainingAssembly.HasTranspileAssembly() && IsPublic(type))
        );

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
                ReachNamedReceiver(receiver, question);
                break;
            case IrQuestionedTypeShape.TypeParameter when receiver.Constraints.Count > 0:
                foreach (var constraint in receiver.Constraints)
                    ReachNamedReceiver(
                        new IrQuestionedType
                        {
                            Shape = IrQuestionedTypeShape.Named,
                            Key = constraint,
                        },
                        question
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

    private void ReachNamedReceiver(IrQuestionedType receiver, IrRuntimeQuestion question)
    {
        if (DescribableOf(receiver) is not { } type)
            return;

        switch (type.Kind)
        {
            case DescriptorKind.PlainObject:
                ReachPlainObjectReceiver(type, question);
                break;
            case DescriptorKind.Class:
                foreach (var derived in _types.Where(candidate => candidate.DerivesFrom(type)))
                    Reach(derived);

                // A struct receiver folds to its specialized descriptor.
                if (type.Symbol.IsValueType)
                    ReachTypeArguments(receiver);

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

    /// <summary>
    /// <c>dto.GetType()</c> on a static <c>[PlainObject]</c> type folds to its
    /// descriptor when no other plain object derives from it (FR-020); with
    /// derived plain objects the runtime answers <c>System.Object</c> for any
    /// of them, so it warns (MS0048 reason 2).
    /// </summary>
    private void ReachPlainObjectReceiver(DescribableType type, IrRuntimeQuestion question)
    {
        var hasDerived = _types.Any(candidate =>
            candidate.Kind is DescriptorKind.PlainObject
            && !ReferenceEquals(candidate, type)
            && candidate.DerivesFrom(type)
        );

        if (!hasDerived)
        {
            _foldedPlainObjects.Add(type.Key);
            Reach(type);

            return;
        }

        _report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.RuntimeTypeOfPlainObject,
                $"GetType() on a '{type.Symbol.Name}' answers 'System.Object' at runtime: other "
                    + "[PlainObject] types derive from it, and a plain object has no runtime "
                    + "identity (spec 011 FR-020).",
                LocationOf(question)
            )
        );
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
                LocationOf(question)
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
                LocationOf(question)
            )
        );
    }

    /// <summary>
    /// <c>MS0046</c> reason 1: a test of an array of a non-<c>object</c>
    /// element checks the array only (a cast reports <c>MS0055</c> instead).
    /// </summary>
    private void ReportErasedElementTest(IrRuntimeQuestion question)
    {
        if (question.Type is not { Shape: IrQuestionedTypeShape.Array, ElementIsObject: false })
            return;

        _report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.ErasedTypeArgumentTest,
                $"'{question.Text}' tests only that the value is an array: an array's element "
                    + "type is erased at runtime (spec 011 FR-021).",
                LocationOf(question)
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
                LocationOf(question)
            )
        );
    }

    private void Reach(DescribableType type)
    {
        if (!_reached.Add(type.Key))
            return;

        _reachedOrder.Add(type.Key);

        if (type.Kind is DescriptorKind.PlainObject)
            GuardPlainObject(type, question: null);
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
            DescriptorKind.Interface => BaseInterfacesOf(type)
                .Concat(ImplementersOf(type))
                .Concat(PlainImplementersOf(type.Symbol)),
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

    /// <summary>
    /// The <c>[PlainObject]</c> types of this compilation that implement
    /// <paramref name="iface"/>, in declaration order (OD-8).
    /// </summary>
    private IEnumerable<DescribableType> PlainImplementersOf(INamedTypeSymbol iface) =>
        _types.Where(candidate =>
            candidate.Kind is DescriptorKind.PlainObject
            && candidate.Symbol.AllInterfaces.Any(implemented =>
                SymbolEqualityComparer.Default.Equals(
                    implemented.OriginalDefinition,
                    iface.OriginalDefinition
                )
            )
        );

    private RuntimeTypeReachabilityResult ToResult(IrCompilation ir) =>
        new()
        {
            Reached = _reached,
            GuardedPlainObjects = _guarded,
            FoldedPlainObjects = _foldedPlainObjects,
            LibraryPlainImplementers = _libraryPlainImplementers,
            PlainImplementers = PlainImplementersOfReachedInterfaces(),
            QuestionedCollectionTypes = ir.QuestionedCollectionTypes,
            QuestionedCollectionClasses = QuestionedCollectionClassesOf(ir),
            UnanswerableCollectionTests = _unanswerableCollectionTests,
            OverridingGetters = OverridingGetters(),
            Undescribable = _undescribable,
            BoxingEnabled = _boxingEnabled,
            HasBoxingConversion = _hasBoxingConversion,
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
                        PlainImplementersOf(type.Symbol).Select(plain => plain.Key).ToList()
            );

    /// <summary>
    /// A reached class overrides its base's getter only when the base actually
    /// emits one: a describable base of this compilation (reached with the
    /// whole chain), or a type of another assembly with a descriptor
    /// (<see cref="HasRuntimeDescriptor"/>). An <c>[Import]</c>,
    /// <c>[External]</c> or ignored base has none.
    /// </summary>
    private HashSet<SupertypeKey> OverridingGetters() =>
        _reachedOrder
            .Select(key => _typesByKey[key])
            .Where(OverridesDescriptorGetter)
            .Select(type => type.Key)
            .ToHashSet();

    private bool OverridesDescriptorGetter(DescribableType type) =>
        type.Kind is DescriptorKind.Class
        && type.Symbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType
        && (
            DescribableOf(baseType) is not null
            || HasRuntimeDescriptor(baseType)
            || IsRuntimeCollectionClass(baseType)
        );

    private static bool IsRuntimeCollectionClass(INamedTypeSymbol type) =>
        RuntimeCollections.KindOfClrName(ClrNameOf(type.OriginalDefinition)) is { } kind
        && !RuntimeCollections.IsInterface(kind);

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
        symbol.HasAttribute(nameof(EmitRuntimeTypeAttribute));

    private static Location? LocationOf(IrRuntimeQuestion question) =>
        question.Span is { } span ? IrSourceSpanLocations.ToLocation(span) : null;

    internal static bool IsPublic(INamedTypeSymbol type)
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
        PlainObject,
    }

    /// <summary>Which descriptors a test of a tested type reads.</summary>
    private enum DescriptorRead
    {
        /// <summary>Only an interface's: a test, a pattern, a dispatch.</summary>
        None,

        /// <summary>An interface's and an enum's: <c>as</c>.</summary>
        Enum,

        /// <summary>An interface's, an enum's and a plain object's: a cast.</summary>
        EnumAndPlainObject,
    }

    /// <summary>
    /// A type of this compilation that TypeScript can describe: a class,
    /// record or struct emitted as a class, a <c>[PlainObject]</c>, an emitted
    /// interface or an enum. Branded wrappers, <c>[JsTuple]</c>s, static classes
    /// and types with no emitted declaration have none.
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
                TypeKind.Class or TypeKind.Struct when IsPlainObjectShape(type) =>
                    DescriptorKind.PlainObject,
                _ => null,
            };
        }

        private static bool HasNoEmittedDeclaration(INamedTypeSymbol type) =>
            SymbolHelper.HasImport(type)
            || SymbolHelper.HasIgnore(type, TargetLanguage.TypeScript)
            || SymbolHelper.HasAttribute(type, "NoEmit");

        /// <summary>
        /// A generic plain object has no runtime test yet (its TS interface and
        /// guard are not emitted generically), so it gets no descriptor.
        /// </summary>
        private static bool IsPlainObjectShape(INamedTypeSymbol type) =>
            !type.IsStatic
            && SymbolHelper.HasPlainObject(type)
            && !SymbolHelper.HasJsTuple(type)
            && type.TypeParameters.Length == 0;

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
