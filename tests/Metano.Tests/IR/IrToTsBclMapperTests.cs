using Metano.Compiler.IR;
using Metano.Transformation;
using Metano.TypeScript.AST;
using Metano.TypeScript.Bridge;

namespace Metano.Tests.IR;

/// <summary>
/// Unit coverage for <see cref="IrToTsBclMapper"/> — the IR-driven BCL mapper that
/// mirrors the legacy <see cref="BclMapper"/> but consumes <see cref="IrMemberOrigin"/>
/// instead of Roslyn symbols. Each test hand-builds a <see cref="DeclarativeMappingRegistry"/>
/// via <see cref="DeclarativeMappingRegistry.CreateForTests"/>, so the mapper's lookup
/// and rendering logic is exercised in isolation without a full compilation.
/// </summary>
public class IrToTsBclMapperTests
{
    [Test]
    public async Task NullOrigin_ReturnsNull()
    {
        var registry = DeclarativeMappingRegistry.CreateForTests(
            new Dictionary<(string, string), DeclarativeMappingEntry>()
        );
        var access = new IrMemberAccess(new IrIdentifier("xs"), "Count", Origin: null);

        var result = IrToTsBclMapper.TryMapMemberAccess(access, new TsIdentifier("xs"), registry);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task UnmappedOrigin_ReturnsNull()
    {
        var registry = DeclarativeMappingRegistry.CreateForTests(
            new Dictionary<(string, string), DeclarativeMappingEntry>()
        );
        var access = new IrMemberAccess(
            new IrIdentifier("xs"),
            "Foo",
            new IrMemberOrigin("Some.Unmapped.Type<T>", "Foo")
        );

        var result = IrToTsBclMapper.TryMapMemberAccess(access, new TsIdentifier("xs"), registry);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PropertyRename_LowersToPropertyAccess()
    {
        var registry = DeclarativeMappingRegistry.CreateForTests(
            new Dictionary<(string, string), DeclarativeMappingEntry>(),
            properties: new Dictionary<(string, string), DeclarativeMappingEntry>
            {
                {
                    ("System.Collections.Generic.List<T>", "Count"),
                    new DeclarativeMappingEntry(JsName: "length", JsTemplate: null)
                },
            }
        );
        var access = new IrMemberAccess(
            new IrIdentifier("xs"),
            "Count",
            new IrMemberOrigin("System.Collections.Generic.List<T>", "Count")
        );

        var result = IrToTsBclMapper.TryMapMemberAccess(access, new TsIdentifier("xs"), registry);
        var prop = result as TsPropertyAccess;
        await Assert.That(prop).IsNotNull();
        await Assert.That(((TsIdentifier)prop!.Object).Name).IsEqualTo("xs");
        await Assert.That(prop.Property).IsEqualTo("length");
    }

    [Test]
    public async Task StaticPropertyTemplate_DropsReceiver()
    {
        var registry = DeclarativeMappingRegistry.CreateForTests(
            new Dictionary<(string, string), DeclarativeMappingEntry>(),
            properties: new Dictionary<(string, string), DeclarativeMappingEntry>
            {
                {
                    ("System.DateTime", "Now"),
                    new DeclarativeMappingEntry(
                        JsName: null,
                        JsTemplate: "Temporal.Now.plainDateTimeISO()"
                    )
                },
            }
        );
        var access = new IrMemberAccess(
            new IrIdentifier("DateTime"),
            "Now",
            new IrMemberOrigin("System.DateTime", "Now", IsStatic: true)
        );

        var result = IrToTsBclMapper.TryMapMemberAccess(
            access,
            new TsIdentifier("DateTime"),
            registry
        );
        await Assert.That(result).IsTypeOf<TsTemplate>();
        var template = (TsTemplate)result!;
        await Assert.That(template.Receiver).IsNull();
    }

    [Test]
    public async Task MethodRename_LowersToMethodCall()
    {
        var registry = DeclarativeMappingRegistry.CreateForTests(
            new Dictionary<(string, string), DeclarativeMappingEntry>
            {
                {
                    ("System.Collections.Generic.List<T>", "Add"),
                    new DeclarativeMappingEntry(JsName: "push", JsTemplate: null)
                },
            }
        );
        var call = new IrCallExpression(
            Target: new IrMemberAccess(new IrIdentifier("xs"), "Add"),
            Arguments: [],
            TypeArguments: null,
            Origin: new IrMemberOrigin("System.Collections.Generic.List<T>", "Add")
        );

        var result = IrToTsBclMapper.TryMapCall(
            call,
            loweredReceiver: new TsIdentifier("xs"),
            loweredArgs: [new TsLiteral("1")],
            typeArgumentNames: [],
            registry
        );
        var callExpr = result as TsCallExpression;
        await Assert.That(callExpr).IsNotNull();
        var callee = callExpr!.Callee as TsPropertyAccess;
        await Assert.That(callee).IsNotNull();
        await Assert.That(callee!.Property).IsEqualTo("push");
    }

    [Test]
    public async Task MethodWithWrapReceiver_WrapsOnce()
    {
        var registry = DeclarativeMappingRegistry.CreateForTests(
            new Dictionary<(string, string), DeclarativeMappingEntry>
            {
                {
                    ("System.Linq.Enumerable", "Where"),
                    new DeclarativeMappingEntry(
                        JsName: "where",
                        JsTemplate: null,
                        WrapReceiver: "Enumerable.from"
                    )
                },
            }
        );
        var call = new IrCallExpression(
            Target: new IrMemberAccess(new IrIdentifier("xs"), "Where"),
            Arguments: [],
            TypeArguments: null,
            Origin: new IrMemberOrigin("System.Linq.Enumerable", "Where", IsStatic: true)
        );

        var result = IrToTsBclMapper.TryMapCall(
            call,
            loweredReceiver: new TsIdentifier("xs"),
            loweredArgs: [],
            typeArgumentNames: [],
            registry
        );
        // Expect: Enumerable.from(xs).where()
        var callExpr = (TsCallExpression)result!;
        var callee = (TsPropertyAccess)callExpr.Callee;
        await Assert.That(callee.Property).IsEqualTo("where");
        var wrapCall = (TsCallExpression)callee.Object;
        var wrapCallee = (TsPropertyAccess)wrapCall.Callee;
        await Assert.That(((TsIdentifier)wrapCallee.Object).Name).IsEqualTo("Enumerable");
        await Assert.That(wrapCallee.Property).IsEqualTo("from");
    }

    [Test]
    public async Task MethodArgFilter_OnlyMatchesWhenLiteralEquals()
    {
        var registry = DeclarativeMappingRegistry.CreateForTests(
            new Dictionary<(string, string), DeclarativeMappingEntry>
            {
                {
                    ("System.Guid", "ToString"),
                    new DeclarativeMappingEntry(
                        JsName: null,
                        JsTemplate: "$this.replaceAll('-', '')",
                        WhenArg0StringEquals: "N"
                    )
                },
            }
        );
        var call = new IrCallExpression(
            Target: new IrMemberAccess(new IrIdentifier("g"), "ToString"),
            Arguments: [],
            TypeArguments: null,
            Origin: new IrMemberOrigin("System.Guid", "ToString")
        );

        // Arg "N" → matches
        var matched = IrToTsBclMapper.TryMapCall(
            call,
            loweredReceiver: new TsIdentifier("g"),
            loweredArgs: [new TsStringLiteral("N")],
            typeArgumentNames: [],
            registry
        );
        await Assert.That(matched).IsTypeOf<TsTemplate>();

        // Arg "D" → no match, caller falls back
        var noMatch = IrToTsBclMapper.TryMapCall(
            call,
            loweredReceiver: new TsIdentifier("g"),
            loweredArgs: [new TsStringLiteral("D")],
            typeArgumentNames: [],
            registry
        );
        await Assert.That(noMatch).IsNull();
    }
}
