using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

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
            case IrEnumDeclaration:
                // enums have no runtime helper surface
                break;
        }
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

        // Records carry value-equality semantics that require a hashing helper.
        // [PlainObject] records are emitted as bare interfaces / object literals — no
        // synthesized equals/hashCode/with — so the helper isn't needed.
        if (c.Semantics.IsRecord && !c.Semantics.IsPlainObject)
            acc.Add(new IrRuntimeRequirement("HashCode", IrRuntimeCategory.Hashing));
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

        // Overloaded methods drive an overload dispatcher whose body emits a
        // primitive type check per parameter (`isInt32(args[0])`, etc.). The
        // scanner records the requirement up front so the import walker can
        // stop guessing at function names.
        if (method.Overloads is { Count: > 0 } overloads)
        {
            CollectDispatcherTypeChecks(method.Parameters, acc);
            foreach (var o in overloads)
            {
                CollectDispatcherTypeChecks(o.Parameters, acc);
                ScanMethod(o, acc);
            }
        }
    }

    private static void ScanConstructor(
        IrConstructorDeclaration ctor,
        HashSet<IrRuntimeRequirement> acc
    )
    {
        foreach (var p in ctor.Parameters)
            ScanTypeRef(p.Parameter.Type, acc);

        if (ctor.Overloads is { Count: > 0 } overloads)
        {
            CollectDispatcherTypeChecks(ctor.Parameters.Select(p => p.Parameter), acc);
            foreach (var o in overloads)
            {
                CollectDispatcherTypeChecks(o.Parameters.Select(p => p.Parameter), acc);
                ScanConstructor(o, acc);
            }
        }
    }

    private static void CollectDispatcherTypeChecks(
        IEnumerable<IrParameter> parameters,
        HashSet<IrRuntimeRequirement> acc
    )
    {
        foreach (var p in parameters)
        {
            var helper = ResolveTypeCheckHelper(p.Type);
            if (helper is not null)
                acc.Add(new IrRuntimeRequirement(helper, IrRuntimeCategory.PrimitiveTypeCheck));
        }
    }

    /// <summary>
    /// Mirrors <c>IrTypeCheckBuilder.PrimitiveCheck</c> on the TS side. Returns
    /// <c>null</c> when the parameter type maps to a structural check
    /// (Array.isArray / instanceof Map / instanceof Set / instanceof Promise) —
    /// those don't pull a runtime helper.
    /// </summary>
    private static string? ResolveTypeCheckHelper(IrTypeRef type) =>
        type switch
        {
            IrNullableTypeRef n => ResolveTypeCheckHelper(n.Inner),
            IrPrimitiveTypeRef p => PrimitiveTypeCheckHelper(p.Primitive),
            IrNamedTypeRef { Semantics.Kind: IrNamedTypeKind.NumericEnum } => "isInt32",
            IrNamedTypeRef { Semantics.Kind: IrNamedTypeKind.StringEnum } => "isString",
            IrNamedTypeRef
            {
                Semantics: { Kind: IrNamedTypeKind.InlineWrapper, InlineWrappedPrimitive: var pr },
            } => pr is null ? null : PrimitiveTypeCheckHelper(pr.Value),
            _ => null,
        };

    private static string? PrimitiveTypeCheckHelper(IrPrimitive p) =>
        p switch
        {
            IrPrimitive.Char => "isChar",
            IrPrimitive.String => "isString",
            IrPrimitive.Byte => "isByte",
            IrPrimitive.Int16 => "isInt16",
            IrPrimitive.Int32 => "isInt32",
            IrPrimitive.Int64 => "isInt64",
            IrPrimitive.Float32 => "isFloat32",
            IrPrimitive.Float64 => "isFloat64",
            IrPrimitive.Decimal => "isFloat64",
            IrPrimitive.Boolean => "isBool",
            IrPrimitive.BigInteger => "isBigInt",
            IrPrimitive.Guid => "isString",
            _ => null,
        };

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
