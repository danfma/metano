using System.Collections.Immutable;
using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Analysis;

/// <summary>
/// Post-extraction fixpoint analysis for reified type parameters (spec 011
/// FR-024, stage 011d). Scans transpilable bodies for runtime questions over
/// type parameters (<c>is T</c>, <c>as T</c>, <c>typeof(T)</c>, <c>(T)x</c>,
/// <c>new T()</c>), propagates reification across override/interface groups,
/// derived class chains, and transitive calls, and reports <c>MS0050</c>,
/// <c>MS0054</c>, and <c>MS0001</c> (for <c>new T()</c> without <c>create</c>).
/// </summary>
public sealed class ReifiedTypeParameterAnalysis
{
    public static readonly ReifiedTypeAnalysisResult Disabled = new(
        isEnabled: false,
        reifiedMethodParams: new Dictionary<IMethodSymbol, IReadOnlyList<IrReifiedTypeParameter>>(
            SymbolEqualityComparer.Default
        ),
        reifiedClassParams: new Dictionary<INamedTypeSymbol, IReadOnlyList<IrReifiedTypeParameter>>(
            SymbolEqualityComparer.Default
        ),
        classSlots: new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default),
        parameterlessCtors: new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
        unreifiableMembers: new HashSet<ISymbol>(SymbolEqualityComparer.Default),
        staticClassReifiedParams: new Dictionary<
            IMethodSymbol,
            IReadOnlyList<ITypeParameterSymbol>
        >(SymbolEqualityComparer.Default)
    );

    public static ReifiedTypeAnalysisResult Analyze(
        Compilation compilation,
        bool assemblyWideTranspile,
        TargetLanguage target,
        IReadOnlyDictionary<string, string>? typeNamesBySymbol,
        List<MetanoDiagnostic> diagnostics
    )
    {
        if (target == TargetLanguage.Dart)
            return Disabled;

        var collector = new Collector(compilation, assemblyWideTranspile, target);
        collector.Collect();
        return collector.Solve(typeNamesBySymbol, diagnostics);
    }

    private sealed class Collector
    {
        private readonly Compilation _compilation;
        private readonly bool _assemblyWideTranspile;
        private readonly TargetLanguage _target;
        private readonly IAssemblySymbol _currentAssembly;
        private readonly bool _isEmitPackage;

        private readonly List<TriggerSite> _triggers = [];
        private readonly List<CallEdge> _callEdges = [];
        private readonly List<NewEdge> _newEdges = [];
        private readonly List<DerivedEdge> _derivedEdges = [];
        private readonly List<(IMethodSymbol Member, IMethodSymbol BaseOrIface)> _groupEdges = [];
        private readonly List<IMethodSymbol> _allMethods = [];
        private readonly List<INamedTypeSymbol> _allGenericTypes = [];
        private readonly HashSet<ISymbol> _unreifiableSites = new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> _jsCallableConversions = new(
            SymbolEqualityComparer.Default
        );
        private readonly HashSet<ITypeParameterSymbol> _newConstraintTriggers = new(
            SymbolEqualityComparer.Default
        );

        public Collector(Compilation compilation, bool assemblyWideTranspile, TargetLanguage target)
        {
            _compilation = compilation;
            _assemblyWideTranspile = assemblyWideTranspile;
            _target = target;
            _currentAssembly = compilation.Assembly;
            _isEmitPackage =
                SymbolHelper.GetEmitPackage(
                    _currentAssembly,
                    targetEnumValue: (int)target.ToEmitTarget()
                )
                    is not null;
        }

        public void Collect()
        {
            VisitNamespace(_currentAssembly.GlobalNamespace);
        }

        private void VisitNamespace(INamespaceSymbol ns)
        {
            foreach (var type in ns.GetTypeMembers())
                VisitType(type);

            foreach (var child in ns.GetNamespaceMembers())
                VisitNamespace(child);
        }

        private void VisitType(INamedTypeSymbol type)
        {
            if (!SymbolHelper.IsTranspilable(type, _assemblyWideTranspile, _currentAssembly))
                return;

            if (SymbolHelper.HasIgnore(type, _target))
                return;

            if (type.TypeParameters.Length > 0)
                _allGenericTypes.Add(type);

            if (type.BaseType is { IsGenericType: true } baseType)
                _derivedEdges.Add(new DerivedEdge(type, baseType));

            foreach (var member in type.GetMembers())
            {
                if (SymbolHelper.HasIgnore(member, _target))
                    continue;

                if (member is IMethodSymbol method)
                {
                    if (
                        method.MethodKind
                        is MethodKind.PropertyGet
                            or MethodKind.PropertySet
                            or MethodKind.EventAdd
                            or MethodKind.EventRemove
                    )
                    {
                        ScanMemberSyntax(method);
                        continue;
                    }

                    _allMethods.Add(method);
                    CollectGroupEdges(method);
                    ScanMemberSyntax(method);
                }
                else if (member is IPropertySymbol or IFieldSymbol)
                {
                    ScanMemberSyntax(member);
                }
            }

            foreach (var nested in type.GetTypeMembers())
                VisitType(nested);
        }

        private void CollectGroupEdges(IMethodSymbol method)
        {
            if (method.OverriddenMethod is { } overridden)
                _groupEdges.Add((method, overridden.OriginalDefinition));

            foreach (var explicitIface in method.ExplicitInterfaceImplementations)
                _groupEdges.Add((method, explicitIface.OriginalDefinition));

            var containing = method.ContainingType;

            foreach (var iface in containing.AllInterfaces)
            {
                foreach (var ifaceMember in iface.GetMembers(method.Name).OfType<IMethodSymbol>())
                {
                    var impl = containing.FindImplementationForInterfaceMember(ifaceMember);

                    if (SymbolEqualityComparer.Default.Equals(impl, method))
                        _groupEdges.Add((method, ifaceMember.OriginalDefinition));
                }
            }
        }

        private void ScanMemberSyntax(ISymbol member)
        {
            if (member.IsImplicitlyDeclared)
                return;

            foreach (var syntaxRef in member.DeclaringSyntaxReferences)
            {
                var syntax = syntaxRef.GetSyntax();
                var model = _compilation.GetSemanticModel(syntax.SyntaxTree);
                ScanNode(syntax, model, member);
            }
        }

        private void ScanNode(SyntaxNode root, SemanticModel model, ISymbol enclosingMember)
        {
            foreach (var node in root.DescendantNodesAndSelf())
            {
                var currentOwner = ResolveOwnerForNode(node, model, enclosingMember);

                switch (node)
                {
                    case BinaryExpressionSyntax bin
                        when bin.IsKind(SyntaxKind.IsExpression)
                            || bin.IsKind(SyntaxKind.AsExpression):
                    {
                        var targetType = model.GetTypeInfo(bin.Right).Type;
                        RecordTypeTrigger(
                            targetType,
                            currentOwner,
                            bin.Right.GetLocation(),
                            isNewConstraint: false
                        );
                        break;
                    }

                    case TypePatternSyntax typePattern:
                    {
                        var targetType = model.GetTypeInfo(typePattern.Type).Type;
                        RecordTypeTrigger(
                            targetType,
                            currentOwner,
                            typePattern.Type.GetLocation(),
                            isNewConstraint: false
                        );
                        break;
                    }

                    case DeclarationPatternSyntax declPattern:
                    {
                        var targetType = model.GetTypeInfo(declPattern.Type).Type;
                        RecordTypeTrigger(
                            targetType,
                            currentOwner,
                            declPattern.Type.GetLocation(),
                            isNewConstraint: false
                        );
                        break;
                    }

                    case CastExpressionSyntax cast:
                    {
                        if (
                            model.GetOperation(cast)
                                is not Microsoft.CodeAnalysis.Operations.IConversionOperation conv
                            || conv.GetConversion().IsImplicit
                            || !Frontend.Roslyn.RuntimeQuestionScanner.IsCheckedCast(
                                conv,
                                _compilation
                            )
                        )
                            break;

                        var targetType = model.GetTypeInfo(cast.Type).Type;
                        RecordTypeTrigger(
                            targetType,
                            currentOwner,
                            cast.Type.GetLocation(),
                            isNewConstraint: false
                        );
                        break;
                    }

                    case TypeOfExpressionSyntax typeOf:
                    {
                        var targetType = model.GetTypeInfo(typeOf.Type).Type;
                        RecordTypeTrigger(
                            targetType,
                            currentOwner,
                            typeOf.Type.GetLocation(),
                            isNewConstraint: false
                        );
                        break;
                    }

                    case ObjectCreationExpressionSyntax creation:
                    {
                        var createdType = model.GetTypeInfo(creation.Type).Type;

                        if (createdType is ITypeParameterSymbol tp)
                        {
                            RecordTypeTrigger(
                                tp,
                                currentOwner,
                                creation.GetLocation(),
                                isNewConstraint: true
                            );
                        }
                        else if (createdType is INamedTypeSymbol { IsGenericType: true } named)
                        {
                            _newEdges.Add(new NewEdge(currentOwner, named, creation.GetLocation()));
                        }

                        break;
                    }

                    case ImplicitObjectCreationExpressionSyntax implicitCreation:
                    {
                        if (
                            model.GetTypeInfo(implicitCreation).Type is INamedTypeSymbol
                            {
                                IsGenericType: true
                            } named
                        )
                        {
                            _newEdges.Add(
                                new NewEdge(currentOwner, named, implicitCreation.GetLocation())
                            );
                        }

                        break;
                    }

                    case InvocationExpressionSyntax inv:
                    {
                        if (model.GetSymbolInfo(inv).Symbol is IMethodSymbol callee)
                        {
                            _callEdges.Add(new CallEdge(currentOwner, callee, inv.GetLocation()));
                        }

                        break;
                    }

                    case IdentifierNameSyntax
                    or MemberAccessExpressionSyntax:
                    {
                        if (
                            node.Parent is not InvocationExpressionSyntax
                            && model.GetOperation(node)
                                is IMethodReferenceOperation { Method: var methodGroup }
                        )
                        {
                            _callEdges.Add(
                                new CallEdge(currentOwner, methodGroup, node.GetLocation())
                            );

                            if (
                                model.GetTypeInfo(node).ConvertedType is INamedTypeSymbol delType
                                && SymbolHelper.HasAttribute(delType, "JsCallableAttribute")
                            )
                            {
                                _jsCallableConversions.Add(methodGroup.OriginalDefinition);
                            }
                        }

                        break;
                    }
                }
            }
        }

        private static ISymbol ResolveOwnerForNode(
            SyntaxNode node,
            SemanticModel model,
            ISymbol fallback
        )
        {
            for (var cur = node.Parent; cur is not null; cur = cur.Parent)
            {
                if (cur is LocalFunctionStatementSyntax localFn)
                {
                    if (model.GetDeclaredSymbol(localFn) is IMethodSymbol localSym)
                        return localSym;
                }
                else if (cur is AnonymousFunctionExpressionSyntax anon)
                {
                    if (model.GetSymbolInfo(anon).Symbol is IMethodSymbol lambdaSym)
                        return lambdaSym;
                }
                else if (
                    cur
                    is MethodDeclarationSyntax
                        or ConstructorDeclarationSyntax
                        or PropertyDeclarationSyntax
                        or FieldDeclarationSyntax
                )
                {
                    break;
                }
            }

            return fallback;
        }

        private void RecordTypeTrigger(
            ITypeSymbol? type,
            ISymbol enclosingMember,
            Location location,
            bool isNewConstraint
        )
        {
            if (type is null)
                return;

            foreach (var tp in CollectTypeParameters(type))
            {
                if (isNewConstraint)
                    _newConstraintTriggers.Add(tp);

                var owner = ResolveTypeParameterOwner(tp, enclosingMember);
                _triggers.Add(
                    new TriggerSite(owner, tp, enclosingMember, location, isNewConstraint)
                );
            }
        }

        private static ISymbol? ResolveTypeParameterOwner(
            ITypeParameterSymbol tp,
            ISymbol enclosingMember
        )
        {
            if (tp.DeclaringMethod is { } declaringMethod)
                return declaringMethod.OriginalDefinition;

            if (tp.DeclaringType is { } declaringType)
            {
                var rootMember = UnwrapLocalOrLambdaRoot(enclosingMember);

                if (rootMember.IsStatic)
                    return rootMember is IMethodSymbol m ? m.OriginalDefinition : rootMember;

                return declaringType.OriginalDefinition;
            }

            return null;
        }

        private static ISymbol UnwrapLocalOrLambdaRoot(ISymbol symbol)
        {
            var cur = symbol;

            while (
                cur
                    is IMethodSymbol
                    {
                        MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction,
                        ContainingSymbol: { } parent,
                    }
            )
            {
                cur = parent;
            }

            return cur;
        }

        public ReifiedTypeAnalysisResult Solve(
            IReadOnlyDictionary<string, string>? typeNamesBySymbol,
            List<MetanoDiagnostic> diagnostics
        )
        {
            var reifiedByOwner = new Dictionary<ISymbol, HashSet<ITypeParameterSymbol>>(
                SymbolEqualityComparer.Default
            );

            bool Mark(ISymbol? owner, ITypeParameterSymbol tp)
            {
                if (owner is null)
                    return false;

                if (!reifiedByOwner.TryGetValue(owner, out var set))
                {
                    set = new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default);
                    reifiedByOwner[owner] = set;
                }

                return set.Add(tp);
            }

            // Seed from direct triggers.
            foreach (var trigger in _triggers)
                Mark(trigger.Owner, trigger.TypeParameter);

            // Fixpoint propagation.
            var changed = true;

            while (changed)
            {
                changed = false;

                // 1. Override / interface-implementation groups.
                foreach (var (member, baseOrIface) in _groupEdges)
                {
                    var mOrig = member.OriginalDefinition;
                    var bOrig = baseOrIface.OriginalDefinition;

                    if (
                        !SymbolEqualityComparer.Default.Equals(
                            bOrig.ContainingAssembly,
                            _currentAssembly
                        )
                    )
                    {
                        continue;
                    }

                    for (
                        var i = 0;
                        i < mOrig.TypeParameters.Length && i < bOrig.TypeParameters.Length;
                        i++
                    )
                    {
                        var mTp = mOrig.TypeParameters[i];
                        var bTp = bOrig.TypeParameters[i];

                        if (reifiedByOwner.TryGetValue(mOrig, out var mSet) && mSet.Contains(mTp))
                        {
                            if (Mark(bOrig, bTp))
                                changed = true;
                        }

                        if (reifiedByOwner.TryGetValue(bOrig, out var bSet) && bSet.Contains(bTp))
                        {
                            if (Mark(mOrig, mTp))
                                changed = true;
                        }
                    }
                }

                // 2. Derived class chains.
                foreach (var derivedEdge in _derivedEdges)
                {
                    var baseDef = derivedEdge.BaseType.OriginalDefinition;

                    if (
                        !SymbolEqualityComparer.Default.Equals(
                            baseDef.ContainingAssembly,
                            _currentAssembly
                        )
                    )
                    {
                        continue;
                    }

                    if (!reifiedByOwner.TryGetValue(baseDef, out var baseSet) || baseSet.Count == 0)
                    {
                        continue;
                    }

                    foreach (var arg in derivedEdge.BaseType.TypeArguments)
                    {
                        foreach (var tp in CollectTypeParameters(arg))
                        {
                            var owner = ResolveTypeParameterOwner(tp, derivedEdge.DerivedType);

                            if (Mark(owner, tp))
                                changed = true;
                        }
                    }
                }

                // 3. Transitive calls & method-group delegates.
                foreach (var call in _callEdges)
                {
                    var calleeDef = call.Callee.OriginalDefinition;

                    if (
                        !SymbolEqualityComparer.Default.Equals(
                            calleeDef.ContainingAssembly,
                            _currentAssembly
                        )
                    )
                    {
                        continue;
                    }

                    if (
                        !reifiedByOwner.TryGetValue(calleeDef, out var calleeSet)
                        || calleeSet.Count == 0
                    )
                    {
                        continue;
                    }

                    // Check method type parameters.
                    for (
                        var i = 0;
                        i < calleeDef.TypeParameters.Length && i < call.Callee.TypeArguments.Length;
                        i++
                    )
                    {
                        if (!calleeSet.Contains(calleeDef.TypeParameters[i]))
                            continue;

                        foreach (var tp in CollectTypeParameters(call.Callee.TypeArguments[i]))
                        {
                            var owner = ResolveTypeParameterOwner(tp, call.Caller);

                            if (Mark(owner, tp))
                                changed = true;
                        }
                    }

                    // Check static method using containing generic class's type parameters.
                    if (calleeDef.IsStatic && call.Callee.ContainingType.IsGenericType)
                    {
                        var containingDef = calleeDef.ContainingType.OriginalDefinition;
                        var containingArgs = call.Callee.ContainingType.TypeArguments;

                        for (
                            var i = 0;
                            i < containingDef.TypeParameters.Length && i < containingArgs.Length;
                            i++
                        )
                        {
                            if (!calleeSet.Contains(containingDef.TypeParameters[i]))
                                continue;

                            foreach (var tp in CollectTypeParameters(containingArgs[i]))
                            {
                                var owner = ResolveTypeParameterOwner(tp, call.Caller);

                                if (Mark(owner, tp))
                                    changed = true;
                            }
                        }
                    }
                }

                // 4. Generic object creations.
                foreach (var newEdge in _newEdges)
                {
                    var targetDef = newEdge.TargetType.OriginalDefinition;

                    if (
                        !SymbolEqualityComparer.Default.Equals(
                            targetDef.ContainingAssembly,
                            _currentAssembly
                        )
                    )
                    {
                        continue;
                    }

                    if (
                        !reifiedByOwner.TryGetValue(targetDef, out var targetSet)
                        || targetSet.Count == 0
                    )
                    {
                        continue;
                    }

                    foreach (var arg in newEdge.TargetType.TypeArguments)
                    {
                        foreach (var tp in CollectTypeParameters(arg))
                        {
                            var owner = ResolveTypeParameterOwner(tp, newEdge.Caller);

                            if (Mark(owner, tp))
                                changed = true;
                        }
                    }
                }
            }

            // Build final IR descriptors for reified methods and classes, and validate diagnostics.
            var reifiedMethodParams = new Dictionary<
                IMethodSymbol,
                IReadOnlyList<IrReifiedTypeParameter>
            >(SymbolEqualityComparer.Default);
            var staticClassReifiedParams = new Dictionary<
                IMethodSymbol,
                IReadOnlyList<ITypeParameterSymbol>
            >(SymbolEqualityComparer.Default);
            var reifiedClassParams = new Dictionary<
                INamedTypeSymbol,
                IReadOnlyList<IrReifiedTypeParameter>
            >(SymbolEqualityComparer.Default);
            var classSlots = new Dictionary<INamedTypeSymbol, string>(
                SymbolEqualityComparer.Default
            );
            var parameterlessCtors = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var type in _allGenericTypes)
            {
                if (HasPublicParameterlessCtor(type))
                    parameterlessCtors.Add(type.OriginalDefinition);

                if (
                    reifiedByOwner.TryGetValue(type.OriginalDefinition, out var classSet)
                    && classSet.Count > 0
                )
                {
                    var tsName = ResolveTsClassName(type, typeNamesBySymbol);
                    classSlots[type.OriginalDefinition] = $"$typeArguments${tsName}";

                    var list = new List<IrReifiedTypeParameter>(type.TypeParameters.Length);

                    for (var i = 0; i < type.TypeParameters.Length; i++)
                    {
                        var tp = type.TypeParameters[i];
                        list.Add(new IrReifiedTypeParameter(tp.Name, $"{tp.Name}$type", i, false));
                    }

                    reifiedClassParams[type.OriginalDefinition] = list;

                    // MS0050 for public generic class constructor gaining $typeArgs.
                    if (
                        type.DeclaredAccessibility
                            is Accessibility.Public
                                or Accessibility.Protected
                        && !SymbolHelper.HasAttribute(type, "ExternalAttribute")
                        && !SymbolHelper.HasAttribute(type, "ImportAttribute")
                        && type.Constructors.Any(c =>
                            !c.IsStatic
                            && c.DeclaredAccessibility
                                is Accessibility.Public
                                    or Accessibility.Protected
                        )
                    )
                    {
                        var ctorLoc = type.Constructors.FirstOrDefault(c =>
                                !c.IsStatic && !c.IsImplicitlyDeclared
                            )
                            ?.Locations.FirstOrDefault()
                            ?? type.Locations.FirstOrDefault();

                        if (ctorLoc is not null)
                        {
                            diagnostics.Add(
                                new MetanoDiagnostic(
                                    MetanoDiagnosticSeverity.Warning,
                                    DiagnosticCodes.ReifiedParameterOnInteropSurface,
                                    $"Generic class '{type.Name}' gains a hidden '$typeArgs' constructor parameter for reified type arguments; JS callers may pass UnknownType.",
                                    ctorLoc
                                )
                            );
                        }
                    }
                }
            }

            // Check method/function reification, impossibility (MS0054), and interop (MS0050).
            foreach (var method in _allMethods)
            {
                var orig = method.OriginalDefinition;

                if (!reifiedByOwner.TryGetValue(orig, out var methodSet) || methodSet.Count == 0)
                    continue;

                if (IsUnreifiableMethod(orig, out var reason))
                {
                    _unreifiableSites.Add(orig);
                    ReportUnreifiableTriggers(orig, reason, diagnostics);
                    continue;
                }

                // Check if any member in override/impl group is unreifiable or from referenced package.
                if (HasUnreifiableGroupMember(orig, out var groupReason))
                {
                    _unreifiableSites.Add(orig);
                    ReportUnreifiableTriggers(orig, groupReason, diagnostics);
                    continue;
                }

                // W5-5d: public member in an [EmitPackage] library reports MS0054.
                if (
                    _isEmitPackage
                    && IsExportedMember(orig)
                    && orig.MethodKind == MethodKind.Ordinary
                )
                {
                    _unreifiableSites.Add(orig);
                    var loc = _triggers
                        .FirstOrDefault(t => SymbolEqualityComparer.Default.Equals(t.Owner, orig))
                        ?.Location
                        ?? orig.Locations.FirstOrDefault()
                        ?? Location.None;
                    diagnostics.Add(
                        new MetanoDiagnostic(
                            MetanoDiagnosticSeverity.Error,
                            DiagnosticCodes.UnreifiableTypeParameter,
                            $"Public member '{orig.ContainingType.Name}.{orig.Name}' in an [EmitPackage] library cannot carry a hidden reified type parameter in 3.0; make it internal/private or pass an explicit descriptor.",
                            loc
                        )
                    );
                    continue;
                }

                var orderedTps = new List<ITypeParameterSymbol>();
                var staticClassTps = new List<ITypeParameterSymbol>();

                if (orig.IsStatic && orig.ContainingType.IsGenericType)
                {
                    foreach (var ctp in orig.ContainingType.OriginalDefinition.TypeParameters)
                    {
                        if (methodSet.Contains(ctp))
                        {
                            orderedTps.Add(ctp);
                            staticClassTps.Add(ctp);
                        }
                    }
                }

                foreach (var mtp in orig.TypeParameters)
                {
                    if (methodSet.Contains(mtp))
                        orderedTps.Add(mtp);
                }

                if (orderedTps.Count == 0)
                    continue;

                if (staticClassTps.Count > 0)
                    staticClassReifiedParams[orig] = staticClassTps;

                var hasParams = orig.Parameters.Length > 0 && orig.Parameters[^1].IsParams;
                var nonParamsCount = hasParams
                    ? orig.Parameters.Length - 1
                    : orig.Parameters.Length;
                var hasOptionalPreceding = orig
                    .Parameters.Take(nonParamsCount)
                    .Any(p => p.IsOptional);

                var reifiedList = new List<IrReifiedTypeParameter>(orderedTps.Count);

                for (var i = 0; i < orderedTps.Count; i++)
                {
                    reifiedList.Add(
                        new IrReifiedTypeParameter(
                            orderedTps[i].Name,
                            $"{orderedTps[i].Name}$type",
                            nonParamsCount + i,
                            hasOptionalPreceding
                        )
                    );
                }

                reifiedMethodParams[orig] = reifiedList;

                // MS0050 on interop surfaces (once per member).
                if (IsInteropSurface(orig))
                {
                    var loc = orig.Locations.FirstOrDefault() ?? Location.None;
                    diagnostics.Add(
                        new MetanoDiagnostic(
                            MetanoDiagnosticSeverity.Warning,
                            DiagnosticCodes.ReifiedParameterOnInteropSurface,
                            $"Member '{orig.ContainingType.Name}.{orig.Name}' gains hidden reified type parameter(s) ({string.Join(", ", reifiedList.Select(r => r.HiddenParameterName))}); JS callers may pass UnknownType.",
                            loc
                        )
                    );
                }
            }

            // Check static fields/properties on generic classes that cannot carry hidden params.
            foreach (var trigger in _triggers)
            {
                if (
                    trigger.Owner is IFieldSymbol or IPropertySymbol
                    && trigger.Owner.IsStatic
                    && !SymbolEqualityComparer.Default.Equals(
                        trigger.Owner,
                        trigger.EnclosingMember
                    )
                )
                {
                    _unreifiableSites.Add(trigger.Owner);
                    diagnostics.Add(
                        new MetanoDiagnostic(
                            MetanoDiagnosticSeverity.Error,
                            DiagnosticCodes.UnreifiableTypeParameter,
                            $"Static member '{trigger.Owner.Name}' on a generic class cannot carry a hidden type parameter for '{trigger.TypeParameter.Name}'.",
                            trigger.Location
                        )
                    );
                }
            }

            // W5-5j: check new T() call sites with concrete types that lack `create`.
            ValidateNewConstraintCallSites(reifiedByOwner, diagnostics);

            return new ReifiedTypeAnalysisResult(
                isEnabled: true,
                reifiedMethodParams: reifiedMethodParams,
                reifiedClassParams: reifiedClassParams,
                classSlots: classSlots,
                parameterlessCtors: parameterlessCtors,
                unreifiableMembers: _unreifiableSites,
                staticClassReifiedParams: staticClassReifiedParams
            );
        }

        private void ValidateNewConstraintCallSites(
            Dictionary<ISymbol, HashSet<ITypeParameterSymbol>> reifiedByOwner,
            List<MetanoDiagnostic> diagnostics
        )
        {
            if (_newConstraintTriggers.Count == 0)
                return;

            foreach (var call in _callEdges)
            {
                var calleeDef = call.Callee.OriginalDefinition;

                for (
                    var i = 0;
                    i < calleeDef.TypeParameters.Length && i < call.Callee.TypeArguments.Length;
                    i++
                )
                {
                    var tp = calleeDef.TypeParameters[i];

                    if (!_newConstraintTriggers.Contains(tp))
                        continue;

                    var arg = call.Callee.TypeArguments[i];

                    if (arg is ITypeParameterSymbol)
                        continue;

                    if (!HasRuntimeCreate(arg))
                    {
                        diagnostics.Add(
                            new MetanoDiagnostic(
                                MetanoDiagnosticSeverity.Error,
                                DiagnosticCodes.UnsupportedFeature,
                                $"Type '{arg.ToDisplayString()}' has no 'create' factory on its runtime descriptor and cannot be constructed via 'new {tp.Name}()'.",
                                call.Location
                            )
                        );
                    }
                }
            }
        }

        private bool HasRuntimeCreate(ITypeSymbol type)
        {
            if (type.IsValueType)
                return true;

            if (type is INamedTypeSymbol named)
            {
                if (Extraction.CollectionKindResolver.KindOf(named) is not null)
                    return true;

                if (
                    named.TypeKind is TypeKind.Interface or TypeKind.Delegate
                    || named.IsAbstract
                    || SymbolHelper.HasAttribute(named, "PlainObjectAttribute")
                    || SymbolHelper.HasAttribute(named, "ExternalAttribute")
                    || SymbolHelper.HasAttribute(named, "ImportAttribute")
                    || !SymbolHelper.IsTranspilable(
                        named.OriginalDefinition,
                        _assemblyWideTranspile,
                        _currentAssembly
                    )
                )
                {
                    return false;
                }

                return HasPublicParameterlessCtor(named.OriginalDefinition);
            }

            return false;
        }

        private static bool HasPublicParameterlessCtor(INamedTypeSymbol type)
        {
            if (type.IsValueType)
                return true;

            if (type.IsAbstract || type.TypeKind != TypeKind.Class)
                return false;

            return type.InstanceConstructors.Any(c =>
                c.Parameters.Length == 0
                && c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
            );
        }

        private bool IsUnreifiableMethod(IMethodSymbol method, out string reason)
        {
            if (
                SymbolHelper.HasAttribute(method, "EmitAttribute")
                || SymbolHelper.HasAttribute(method, "ImportAttribute")
            )
            {
                reason = "an [Emit] or [Import] extern method";
                return true;
            }

            if (SymbolHelper.HasAttribute(method.ContainingType, "ExternalAttribute"))
            {
                reason = "an [External] interface or type member";
                return true;
            }

            if (SymbolHelper.HasAttribute(method, "ModuleEntryPointAttribute"))
            {
                reason = "a [ModuleEntryPoint] method";
                return true;
            }

            if (method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion)
            {
                reason = "an operator";
                return true;
            }

            if (
                SymbolHelper.HasAttribute(method, "QueryableAttribute")
                || method.Parameters.Any(p => SymbolHelper.HasAttribute(p, "QueryableAttribute"))
            )
            {
                reason = "a [Queryable] method";
                return true;
            }

            if (
                method.MethodKind == MethodKind.AnonymousFunction
                && SymbolHelper.HasAttribute(method.ContainingType, "JsCallableAttribute")
            )
            {
                reason = "a [JsCallable] body";
                return true;
            }

            reason = "";
            return false;
        }

        private bool HasUnreifiableGroupMember(IMethodSymbol method, out string reason)
        {
            foreach (var (member, baseOrIface) in _groupEdges)
            {
                if (
                    !SymbolEqualityComparer.Default.Equals(member.OriginalDefinition, method)
                    && !SymbolEqualityComparer.Default.Equals(
                        baseOrIface.OriginalDefinition,
                        method
                    )
                )
                {
                    continue;
                }

                var other = SymbolEqualityComparer.Default.Equals(member.OriginalDefinition, method)
                    ? baseOrIface.OriginalDefinition
                    : member.OriginalDefinition;

                if (
                    !SymbolEqualityComparer.Default.Equals(
                        other.ContainingAssembly,
                        _currentAssembly
                    )
                )
                {
                    reason =
                        $"an override/implementation group with referenced-package member '{other.ContainingType.Name}.{other.Name}'";
                    return true;
                }

                if (IsUnreifiableMethod(other, out var otherReason))
                {
                    reason = $"an override/implementation group containing {otherReason}";
                    return true;
                }
            }

            reason = "";
            return false;
        }

        private void ReportUnreifiableTriggers(
            IMethodSymbol owner,
            string reason,
            List<MetanoDiagnostic> diagnostics
        )
        {
            var matched = false;

            foreach (var trigger in _triggers)
            {
                if (!SymbolEqualityComparer.Default.Equals(trigger.Owner, owner))
                    continue;

                matched = true;
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.UnreifiableTypeParameter,
                        $"Type parameter '{trigger.TypeParameter.Name}' cannot be reified inside {reason}.",
                        trigger.Location
                    )
                );
            }

            if (!matched)
            {
                var loc = owner.Locations.FirstOrDefault() ?? Location.None;
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.UnreifiableTypeParameter,
                        $"Member '{owner.ContainingType.Name}.{owner.Name}' cannot carry a hidden reified type parameter because it is {reason}.",
                        loc
                    )
                );
            }
        }

        private static bool IsExportedMember(IMethodSymbol method) =>
            method.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected
            && method.ContainingType.DeclaredAccessibility
                is Accessibility.Public
                    or Accessibility.Protected;

        private bool IsInteropSurface(IMethodSymbol method)
        {
            if (_jsCallableConversions.Contains(method))
                return true;

            if (SymbolHelper.HasAttribute(method.ContainingType, "ExportedAsModuleAttribute"))
                return method.DeclaredAccessibility is Accessibility.Public;

            if (SymbolHelper.HasAttribute(method.ContainingType, "JsxComponentBuilderAttribute"))
                return true;

            return false;
        }

        private string ResolveTsClassName(
            INamedTypeSymbol type,
            IReadOnlyDictionary<string, string>? typeNamesBySymbol
        )
        {
            var key = type.GetCrossAssemblyOriginKey();

            if (typeNamesBySymbol is not null && typeNamesBySymbol.TryGetValue(key, out var mapped))
                return mapped;

            return SymbolHelper.GetNameOverride(type, _target) ?? type.Name;
        }

        private static IEnumerable<ITypeParameterSymbol> CollectTypeParameters(ITypeSymbol type)
        {
            switch (type)
            {
                case ITypeParameterSymbol tp:
                    yield return tp;
                    break;
                case IArrayTypeSymbol arr:
                    foreach (var tp in CollectTypeParameters(arr.ElementType))
                        yield return tp;
                    break;
                case INamedTypeSymbol named:
                    foreach (var arg in named.TypeArguments)
                    {
                        foreach (var tp in CollectTypeParameters(arg))
                            yield return tp;
                    }

                    break;
            }
        }

        private sealed record TriggerSite(
            ISymbol? Owner,
            ITypeParameterSymbol TypeParameter,
            ISymbol EnclosingMember,
            Location Location,
            bool IsNewConstraint
        );

        private sealed record CallEdge(ISymbol Caller, IMethodSymbol Callee, Location Location);

        private sealed record NewEdge(
            ISymbol Caller,
            INamedTypeSymbol TargetType,
            Location Location
        );

        private sealed record DerivedEdge(INamedTypeSymbol DerivedType, INamedTypeSymbol BaseType);
    }
}

/// <summary>
/// Immutable result of <see cref="ReifiedTypeParameterAnalysis"/> consumed by
/// extraction and target bridges.
/// </summary>
public sealed class ReifiedTypeAnalysisResult
{
    private readonly IReadOnlyDictionary<
        IMethodSymbol,
        IReadOnlyList<IrReifiedTypeParameter>
    > _reifiedMethodParams;
    private readonly IReadOnlyDictionary<
        INamedTypeSymbol,
        IReadOnlyList<IrReifiedTypeParameter>
    > _reifiedClassParams;
    private readonly IReadOnlyDictionary<INamedTypeSymbol, string> _classSlots;
    private readonly IReadOnlySet<INamedTypeSymbol> _parameterlessCtors;
    private readonly IReadOnlySet<ISymbol> _unreifiableMembers;
    private readonly IReadOnlyDictionary<
        IMethodSymbol,
        IReadOnlyList<ITypeParameterSymbol>
    > _staticClassReifiedParams;

    public ReifiedTypeAnalysisResult(
        bool isEnabled,
        IReadOnlyDictionary<
            IMethodSymbol,
            IReadOnlyList<IrReifiedTypeParameter>
        > reifiedMethodParams,
        IReadOnlyDictionary<
            INamedTypeSymbol,
            IReadOnlyList<IrReifiedTypeParameter>
        > reifiedClassParams,
        IReadOnlyDictionary<INamedTypeSymbol, string> classSlots,
        IReadOnlySet<INamedTypeSymbol> parameterlessCtors,
        IReadOnlySet<ISymbol> unreifiableMembers,
        IReadOnlyDictionary<
            IMethodSymbol,
            IReadOnlyList<ITypeParameterSymbol>
        > staticClassReifiedParams
    )
    {
        IsEnabled = isEnabled;
        _reifiedMethodParams = reifiedMethodParams;
        _reifiedClassParams = reifiedClassParams;
        _classSlots = classSlots;
        _parameterlessCtors = parameterlessCtors;
        _unreifiableMembers = unreifiableMembers;
        _staticClassReifiedParams = staticClassReifiedParams;
    }

    public bool IsEnabled { get; }

    public bool IsUnreifiable(ISymbol member) =>
        _unreifiableMembers.Contains(member.OriginalDefinition);

    public IReadOnlyList<IrReifiedTypeParameter> ReifiedParametersOf(IMethodSymbol method) =>
        _reifiedMethodParams.TryGetValue(method.OriginalDefinition, out var list) ? list : [];

    public IReadOnlyList<IrReifiedTypeParameter> ReifiedParametersOf(INamedTypeSymbol type) =>
        _reifiedClassParams.TryGetValue(type.OriginalDefinition, out var list) ? list : [];

    public string? TypeArgumentSlotOf(INamedTypeSymbol type) =>
        _classSlots.TryGetValue(type.OriginalDefinition, out var slot) ? slot : null;

    public bool HasParameterlessCtor(INamedTypeSymbol type) =>
        _parameterlessCtors.Contains(type.OriginalDefinition);

    public IReadOnlyList<IrTypeRef>? ReifiedTypeArgumentsForCall(
        IMethodSymbol callee,
        Func<ITypeSymbol, IrTypeRef> mapType
    )
    {
        var orig = callee.OriginalDefinition;

        if (!_reifiedMethodParams.TryGetValue(orig, out var reified) || reified.Count == 0)
            return null;

        var args = new List<IrTypeRef>(reified.Count);

        if (
            orig.IsStatic
            && _staticClassReifiedParams.TryGetValue(orig, out var staticClassTps)
            && callee.ContainingType.IsGenericType
        )
        {
            var containingDef = orig.ContainingType;

            foreach (var ctp in staticClassTps)
            {
                var idx = containingDef.TypeParameters.IndexOf(ctp);
                args.Add(mapType(callee.ContainingType.TypeArguments[idx]));
            }
        }

        foreach (var rp in reified)
        {
            var tpIdx = orig.TypeParameters.ToList().FindIndex(t => t.Name == rp.Name);

            if (tpIdx >= 0 && tpIdx < callee.TypeArguments.Length)
                args.Add(mapType(callee.TypeArguments[tpIdx]));
        }

        return args.Count > 0 ? args : null;
    }

    public IReadOnlyList<IrTypeRef>? ReifiedTypeArgumentsForNew(
        INamedTypeSymbol targetType,
        Func<ITypeSymbol, IrTypeRef> mapType
    )
    {
        var def = targetType.OriginalDefinition;

        if (!_reifiedClassParams.TryGetValue(def, out var reified) || reified.Count == 0)
            return null;

        return targetType.TypeArguments.Select(mapType).ToList();
    }

    public (IrTypeParameterScope Scope, int SlotIndex, string? DeclaringTypeName) ResolveScope(
        ITypeParameterSymbol tp,
        ISymbol? enclosingMember,
        INamedTypeSymbol? enclosingType
    )
    {
        if (!IsEnabled)
            return (IrTypeParameterScope.None, -1, null);

        if (tp.DeclaringMethod is { } declaringMethod)
        {
            var reified = ReifiedParametersOf(declaringMethod);

            if (reified.Any(r => r.Name == tp.Name))
                return (IrTypeParameterScope.Method, -1, null);

            return (IrTypeParameterScope.None, -1, null);
        }

        if (tp.DeclaringType is { } declaringType)
        {
            if (enclosingMember is IMethodSymbol { IsStatic: true } staticMethod)
            {
                var reified = ReifiedParametersOf(staticMethod);

                if (reified.Any(r => r.Name == tp.Name))
                    return (IrTypeParameterScope.Method, -1, null);

                return (IrTypeParameterScope.None, -1, null);
            }

            if (
                enclosingMember is IMethodSymbol { MethodKind: MethodKind.Constructor }
                && _reifiedClassParams.TryGetValue(
                    declaringType.OriginalDefinition,
                    out var ctorClassParams
                )
            )
            {
                var idx = declaringType.OriginalDefinition.TypeParameters.IndexOf(tp);

                if (idx >= 0 && idx < ctorClassParams.Count)
                    return (IrTypeParameterScope.CtorParam, idx, null);
            }

            if (
                _reifiedClassParams.TryGetValue(
                    declaringType.OriginalDefinition,
                    out var classParams
                ) && _classSlots.TryGetValue(declaringType.OriginalDefinition, out var slotName)
            )
            {
                var idx = declaringType.OriginalDefinition.TypeParameters.IndexOf(tp);

                if (idx >= 0 && idx < classParams.Count)
                    return (IrTypeParameterScope.ClassSlot, idx, slotName);
            }
        }

        return (IrTypeParameterScope.None, -1, null);
    }
}
