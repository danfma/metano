using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript names a class's user-written members declare, per table —
/// the first two steps of the spec 013 claim pass (contracts/naming.md § Claim
/// pass): user members, then the dispatchers of overloaded groups, which keep
/// their C# names. A TypeScript class has one static and one instance
/// namespace, so a static and an instance member may share a name. Each name
/// maps to the C# member that declares it, which diagnostics name and locate.
/// The names are read from the IR with the functions the class emitter
/// declares the members with.
/// </summary>
internal sealed class TsClassMemberTable
{
    private readonly Dictionary<string, string> _statics = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _instances = new(StringComparer.Ordinal);

    private TsClassMemberTable() { }

    /// <summary>Static member name → the C# member declaring it.</summary>
    public IReadOnlyDictionary<string, string> Statics => _statics;

    /// <summary>Instance member name → the C# member declaring it.</summary>
    public IReadOnlyDictionary<string, string> Instances => _instances;

    public static TsClassMemberTable Of(IrClassDeclaration ir)
    {
        var table = new TsClassMemberTable();

        foreach (var name in IrToTsClassBridge.PromotedMemberNames(ir.Constructor))
            table.Declare(isStatic: false, name, name);

        foreach (var member in ir.Members ?? [])
            table.DeclareMember(member);

        if (IrToTsRecordSynthesisBridge.ShouldSynthesize(ir))
        {
            foreach (var name in IrToTsRecordSynthesisBridge.SynthesizedMemberNames)
                table.Declare(isStatic: false, name, ir.Name);
        }

        return table;
    }

    private void DeclareMember(IrMemberDeclaration member)
    {
        switch (member)
        {
            case IrFieldDeclaration field:
                DeclareDataMember(field, field.Attributes);
                break;

            case IrPropertyDeclaration property:
                DeclareDataMember(property, property.Attributes);
                break;

            case IrEventDeclaration evt:
                DeclareEvent(evt);
                break;

            case IrMethodDeclaration { Semantics.IsOperator: true } op:
                DeclareOperator(op);
                break;

            case IrMethodDeclaration method:
                Declare(method.IsStatic, MethodNameOf(method), method.Name);
                break;
        }
    }

    private void DeclareDataMember(
        IrMemberDeclaration member,
        IReadOnlyList<IrAttribute>? attributes
    ) =>
        Declare(
            member.IsStatic,
            IrToTsNamingPolicy.ToDeclaredDataMemberName(member.Name, attributes),
            member.Name
        );

    private void DeclareEvent(IrEventDeclaration evt)
    {
        var name = TypeScriptNaming.ToCamelCaseMember(evt.Name);

        Declare(evt.IsStatic, name, evt.Name);
        Declare(evt.IsStatic, $"{name}$add", evt.Name);
        Declare(evt.IsStatic, $"{name}$remove", evt.Name);
    }

    /// <summary>
    /// An operator declares its static helper (<c>__add</c>, the dispatcher of
    /// an overloaded group) and its instance forwarder (<c>$add</c>).
    /// </summary>
    private void DeclareOperator(IrMethodDeclaration op)
    {
        if (OperatorNameOf(op) is not { } name)
            return;

        Declare(isStatic: true, OperatorDispatcherName(name), op.Name);
        Declare(isStatic: false, $"${name}", op.Name);
    }

    private void Declare(bool isStatic, string name, string declaredBy)
    {
        var table = isStatic ? _statics : _instances;

        table.TryAdd(name, declaredBy);
    }

    /// <summary>
    /// The name a method declares: an overloaded group's dispatcher keeps the
    /// camelCased C# name; a single method takes its own naming policy.
    /// </summary>
    public static string MethodNameOf(IrMethodDeclaration method) =>
        method.Overloads is { Count: > 0 }
            ? TypeScriptNaming.ToCamelCaseMember(method.Name)
            : IrToTsNamingPolicy.ToMethodName(method);

    /// <summary>
    /// The name an operator lowers to (<c>add</c>, <c>negate</c>): its
    /// <c>[Name]</c> override, else the name of its operator kind; <c>null</c>
    /// when the kind has none.
    /// </summary>
    public static string? OperatorNameOf(IrMethodDeclaration op) =>
        IrToTsNamingPolicy.FindNameOverride(op.Attributes)
        ?? (
            op.Semantics.OperatorKind is { } kind
                ? IrToTsClassBridge.MapOperatorKindToName(kind)
                : null
        );

    /// <summary>The static helper of an operator: <c>__</c> + its name.</summary>
    public static string OperatorDispatcherName(string operatorName) => $"__{operatorName}";
}
