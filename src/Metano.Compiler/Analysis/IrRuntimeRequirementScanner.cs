using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// Walks an IR declaration (type or module) and returns the set of semantic runtime
/// helpers it depends on. Backends map each <see cref="IrRuntimeRequirement"/> to
/// concrete imports from their runtime library (e.g., the TypeScript target maps
/// <c>("HashCode", Hashing)</c> to <c>import { HashCode } from "metano-runtime"</c>).
/// <para>
/// Requirement names are <em>semantic</em>, not target-specific: the scanner reports
/// "this module uses the concept of a hashed set" via <see cref="IrRuntimeCategory.Collection"/>,
/// and each backend decides what to import.
/// </para>
/// </summary>
public static class IrRuntimeRequirementScanner
{
    public static IReadOnlySet<IrRuntimeRequirement> Scan(IrTypeDeclaration declaration)
    {
        var acc = new HashSet<IrRuntimeRequirement>();
        ScanType(declaration, acc);
        return acc;
    }

    /// <summary>
    /// The helpers a single type reference needs (e.g. <c>HashSet</c> for a set,
    /// <c>Temporal</c> for a date type), for a backend that emits a type reference
    /// outside any declaration it scans.
    /// </summary>
    public static IReadOnlySet<IrRuntimeRequirement> Scan(IrTypeRef type)
    {
        var acc = new HashSet<IrRuntimeRequirement>();
        ScanTypeRef(type, acc);
        return acc;
    }

    public static IReadOnlySet<IrRuntimeRequirement> Scan(IrModule module)
    {
        var acc = new HashSet<IrRuntimeRequirement>();
        foreach (var type in module.Types)
            ScanType(type, acc);
        if (module.Functions is not null)
            foreach (var fn in module.Functions)
                ScanFunction(fn, acc);
        return acc;
    }

    // -- Type-level walking --------------------------------------------------

    private static void ScanType(IrTypeDeclaration type, HashSet<IrRuntimeRequirement> acc)
    {
        switch (type)
        {
            case IrClassDeclaration c:
                ScanClass(c, acc);
                break;
            case IrInterfaceDeclaration i:
                ScanInterface(i, acc);
                break;
            case IrDelegateDeclaration d:
                ScanDelegate(d, acc);
                break;
            case IrEnumDeclaration:
                // enums have no runtime helper surface
                break;
        }
    }

    private static void ScanDelegate(IrDelegateDeclaration d, HashSet<IrRuntimeRequirement> acc)
    {
        if (d.ThisType is not null)
            ScanTypeRef(d.ThisType, acc);
        foreach (var p in d.Parameters)
            ScanTypeRef(p.Type, acc);
        ScanTypeRef(d.ReturnType, acc);
        if (d.TypeParameters is not null)
            foreach (var tp in d.TypeParameters)
                if (tp.Constraints is not null)
                    foreach (var c in tp.Constraints)
                        ScanTypeRef(c, acc);
    }

    private static void ScanClass(IrClassDeclaration c, HashSet<IrRuntimeRequirement> acc)
    {
        if (c.BaseType is not null)
            ScanTypeRef(c.BaseType, acc);
        if (c.Interfaces is not null)
            foreach (var i in c.Interfaces)
                ScanTypeRef(i, acc);
        if (c.Members is not null)
            foreach (var m in c.Members)
                ScanMember(m, acc);

        if (c.Constructor is not null)
            ScanConstructor(c.Constructor, acc);
        if (c.NestedTypes is not null)
            foreach (var nested in c.NestedTypes)
                ScanType(nested, acc);

        // Records and plain structs carry value equality, whose synthesized
        // hashCode needs the hashing helper (IrValueEqualitySynthesis says
        // which types are emitted as classes that synthesize it).
        if (IrValueEqualitySynthesis.SynthesizesEquality(c))
        {
            // A type that declares its own GetHashCode / Equals gets no
            // synthesized hashCode / equals, so nothing would use the helper.
            if (!IrValueEqualitySynthesis.DeclaresHashCode(c))
                acc.Add(new IrRuntimeRequirement("HashCode", IrRuntimeCategory.Hashing));

            if (IrValueEqualitySynthesis.SynthesizesRecordMembers(c))
                acc.Add(new IrRuntimeRequirement("RecordCopy", IrRuntimeCategory.RecordCopy));

            // The synthesized `equals` falls back to `valueEquals` for
            // every equality member whose type is not an integer, string,
            // boolean, enum or branded value (a double and a type parameter
            // included) so wrapper
            // types (Decimal, Temporal, nested records) compare via
            // their own `equals` contract instead of `===`. Mirror the
            // bridge's UseStrictEquality decision here so the import
            // line lands only when a non-strict field actually exists.
            if (!IrValueEqualitySynthesis.DeclaresEquals(c))
                AddEqualityHelpers(c, acc);
        }
    }

    private static void AddEqualityHelpers(IrClassDeclaration c, HashSet<IrRuntimeRequirement> acc)
    {
        var strategies = (c.EqualityMembers ?? []).Select(m =>
            IrEqualityClassifier.EqualityOf(m.Type)
        );

        foreach (var strategy in strategies.Distinct())
        {
            if (strategy is FieldEqualityKind.Value)
                acc.Add(new IrRuntimeRequirement("ValueEquals", IrRuntimeCategory.Equality));

            if (strategy is FieldEqualityKind.Default)
                acc.Add(new IrRuntimeRequirement("DefaultEquals", IrRuntimeCategory.Equality));
        }
    }

    private static void ScanInterface(IrInterfaceDeclaration i, HashSet<IrRuntimeRequirement> acc)
    {
        if (i.BaseInterfaces is not null)
            foreach (var b in i.BaseInterfaces)
                ScanTypeRef(b, acc);
        if (i.Members is not null)
            foreach (var m in i.Members)
                ScanMember(m, acc);
    }

    // -- Member walking -------------------------------------------------------

    private static void ScanMember(IrMemberDeclaration member, HashSet<IrRuntimeRequirement> acc)
    {
        switch (member)
        {
            case IrFieldDeclaration f:
                ScanTypeRef(f.Type, acc);
                break;
            case IrPropertyDeclaration p:
                ScanTypeRef(p.Type, acc);
                break;
            case IrMethodDeclaration m:
                ScanMethod(m, acc);
                break;
            case IrEventDeclaration e:
                // C# event accessors lower to two helper-call shapes via the
                // backend (delegateAdd / delegateRemove). The scanner records
                // the dependency so backends drop their walk-the-AST heuristic.
                acc.Add(new IrRuntimeRequirement("delegateAdd", IrRuntimeCategory.EventHandling));
                acc.Add(
                    new IrRuntimeRequirement("delegateRemove", IrRuntimeCategory.EventHandling)
                );
                ScanTypeRef(e.HandlerType, acc);
                break;
        }
    }

    private static void ScanMethod(IrMethodDeclaration method, HashSet<IrRuntimeRequirement> acc)
    {
        ScanTypeRef(method.ReturnType, acc);
        foreach (var p in method.Parameters)
            ScanTypeRef(p.Type, acc);

        // An overload dispatcher's argument checks are rendered by the target
        // (spec 011 NFR-004: one classification), which references their
        // helpers itself, so the scanner records none.
        foreach (var overload in method.Overloads ?? [])
            ScanMethod(overload, acc);
    }

    private static void ScanConstructor(
        IrConstructorDeclaration ctor,
        HashSet<IrRuntimeRequirement> acc
    )
    {
        foreach (var p in ctor.Parameters)
            ScanTypeRef(p.Parameter.Type, acc);

        foreach (var o in ctor.Overloads ?? [])
            ScanConstructor(o, acc);
    }

    private static void ScanFunction(IrModuleFunction fn, HashSet<IrRuntimeRequirement> acc)
    {
        ScanTypeRef(fn.ReturnType, acc);
        foreach (var p in fn.Parameters)
            ScanTypeRef(p.Type, acc);
    }

    // -- Type-ref walking -----------------------------------------------------

    private static void ScanTypeRef(IrTypeRef type, HashSet<IrRuntimeRequirement> acc)
    {
        switch (type)
        {
            case IrPrimitiveTypeRef p:
                AddForPrimitive(p.Primitive, acc);
                break;
            case IrNullableTypeRef n:
                ScanTypeRef(n.Inner, acc);
                break;
            case IrArrayTypeRef a:
                ScanTypeRef(a.ElementType, acc);
                break;
            case IrMapTypeRef m:
                ScanTypeRef(m.KeyType, acc);
                ScanTypeRef(m.ValueType, acc);
                break;
            case IrSetTypeRef s:
                acc.Add(new IrRuntimeRequirement("HashSet", IrRuntimeCategory.Collection));
                ScanTypeRef(s.ElementType, acc);
                break;
            case IrTupleTypeRef t:
                foreach (var e in t.Elements)
                    ScanTypeRef(e, acc);
                break;
            case IIrCompositeTypeRef composite:
                foreach (var component in composite.ComponentTypes)
                    ScanTypeRef(component, acc);
                break;
            case IrFunctionTypeRef f:
                if (f.ThisType is { } thisType)
                    ScanTypeRef(thisType, acc);
                foreach (var p in f.Parameters)
                    ScanTypeRef(p.Type, acc);
                ScanTypeRef(f.ReturnType, acc);
                break;
            case IrPromiseTypeRef pr:
                ScanTypeRef(pr.ResultType, acc);
                break;
            case IrGeneratorTypeRef g:
                ScanTypeRef(g.YieldType, acc);
                break;
            case IrIterableTypeRef it:
                ScanTypeRef(it.ElementType, acc);
                break;
            case IrIteratorTypeRef iterator:
                ScanTypeRef(iterator.ElementType, acc);
                break;
            case IrKeyValuePairTypeRef kv:
                ScanTypeRef(kv.KeyType, acc);
                ScanTypeRef(kv.ValueType, acc);
                break;
            case IrGroupingTypeRef gr:
                acc.Add(new IrRuntimeRequirement("Grouping", IrRuntimeCategory.Collection));
                ScanTypeRef(gr.KeyType, acc);
                ScanTypeRef(gr.ElementType, acc);
                break;
            case IrNamedTypeRef named:
                if (named.TypeArguments is not null)
                    foreach (var a in named.TypeArguments)
                        ScanTypeRef(a, acc);
                break;
        }
    }

    private static void AddForPrimitive(IrPrimitive p, HashSet<IrRuntimeRequirement> acc)
    {
        switch (p)
        {
            case IrPrimitive.Guid:
                acc.Add(new IrRuntimeRequirement("UUID", IrRuntimeCategory.BrandedType));
                break;
            case IrPrimitive.DateTime:
            case IrPrimitive.DateTimeOffset:
            case IrPrimitive.DateOnly:
            case IrPrimitive.TimeOnly:
            case IrPrimitive.TimeSpan:
                acc.Add(new IrRuntimeRequirement("Temporal", IrRuntimeCategory.Temporal));
                break;
        }
    }
}
