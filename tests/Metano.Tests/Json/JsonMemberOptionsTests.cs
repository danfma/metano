using System.Text.RegularExpressions;
using Metano.Compiler.Diagnostics;

namespace Metano.Tests.Json;

/// <summary>
/// The member options the generated context mirrors since spec 009 slice S6b
/// (FR-046, DECIDED-25, #268): <c>[JsonInclude]</c> fields and non-public
/// properties, <c>byte[]</c> as base64, <c>[JsonPropertyOrder]</c>, the
/// <c>Never</c> condition of <c>[JsonIgnore]</c>, and the members a read
/// assigns after construction. The golden is
/// <c>json-member-options-context.ts</c>.
///
/// Measured with a source-generated System.Text.Json context (.NET SDK
/// 10.0.401, runtime 10.0.12) over the golden's types:
/// <code>
/// Account { Name = "n", Version = 3, Id = "x", Hits = 4, Unlisted = 9, Kept = 5 }
///   → {"Id":"x","Name":"n","Version":3,"Kept":5,"Hits":4}
/// {"Id":"y","Name":"m","Version":7,"Kept":8,"Hits":6,"Unlisted":1} → m,7,y,6,0,8
/// Blob([1,2,3], [255,254], [1,2]) → {"Data":"AQID","Maybe":"//4=","Numbers":[1,2]}
/// byte[] { 0, 16, 255 } → "ABD/"
/// OrdDer(1, 10) { Base2 = 2, Der2 = 20 } → {"Der2":20,"Der1":10,"Base1":1,"Base2":2}
/// {"Der2":21,"Base1":11,"Der1":12,"Base2":13} → OrdDer { Base1 = 11, Base2 = 13, Der1 = 12, Der2 = 21 }
/// Init (set accessors with initializers): {} → {"Note":"init","Count":5,"Items":[9]},
///   {"Count":1} → {"Note":"init","Count":1,"Items":[9]}, {"Note":null,"Items":null} → {"Note":null,"Count":5,"Items":null}
/// Noted(1) (an init member with an initializer): {"A":1} → {"A":1,"Note":null}
/// </code>
/// Accessibility under source generation: <c>internal</c> and
/// <c>protected internal</c> <c>[JsonInclude]</c> members are written and
/// read; a <c>private</c> or <c>protected</c> one, or a public property with
/// a <c>private</c> setter, raises SYSLIB1038 and makes the contract throw
/// <c>InvalidOperationException</c> ("The property 'Secret' on type
/// 'P.IncPrivate' which is annotated with 'JsonIncludeAttribute' is not
/// accesible by the source generator."); a public field is serialized only
/// with <c>[JsonInclude]</c>; each type lists its properties, then its fields
/// (<c>{"DProp":5,"DProtInt":7,"GetInternalSet":8,"DField":4,"BProp":1,"BInternal":3,"BField":2}</c>);
/// a property with an <c>internal</c> setter and no <c>[JsonInclude]</c> is
/// written but not read. <c>List&lt;byte&gt;</c> stays a number array.
/// <c>[JsonPropertyOrder]</c> sorts the whole hierarchy stably over the
/// FR-009 order, and an override without the attribute has order 0
/// (<c>{"V":3,"Z":4,"W":2}</c>, the base declaration's order 2 is not
/// inherited). The base64 rows are quoted in the runtime's
/// <c>byte-array-converter.test.ts</c>.
///
/// The golden's TypeScript compiles under <c>tsgo --strict</c> and, bundled
/// with <c>bun build</c> and run under Node, wrote every row above byte for
/// byte and read every payload back into the expected values.
/// </summary>
public class JsonMemberOptionsTests
{
    private const string ContextPath = "test-app/json-context.ts";

    private const string ContextBoilerplate = """
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    private const string GoldenDeclarations = """
        [Transpile]
        public sealed record Blob(byte[] Data, byte[]? Maybe, List<byte> Numbers);

        [Transpile]
        public sealed class Account
        {
            public string Name { get; set; } = "";

            [JsonInclude]
            internal int Version { get; set; }

            [JsonInclude, JsonPropertyOrder(-1)]
            internal string Id = "";

            [JsonInclude]
            public int Hits;

            public int Unlisted;

            [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            public int Kept { get; set; }
        }

        [Transpile]
        public record OrdBase(int Base1)
        {
            [JsonPropertyOrder(5)]
            public int Base2 { get; init; }
        }

        [Transpile]
        public sealed record OrdDer(int Base1, int Der1) : OrdBase(Base1)
        {
            [JsonPropertyOrder(-5)]
            public int Der2 { get; init; }
        }

        [Transpile]
        public sealed class Init
        {
            public string? Note { get; set; } = "init";

            public int Count { get; set; } = 5;

            public List<int>? Items { get; set; } = new() { 9 };
        }

        [Transpile]
        public sealed record Noted(int A)
        {
            public string? Note { get; init; } = "x";
        }

        [Transpile]
        [JsonSerializable(typeof(Blob))]
        [JsonSerializable(typeof(Account))]
        [JsonSerializable(typeof(OrdDer))]
        [JsonSerializable(typeof(byte[]))]
        [JsonSerializable(typeof(Init))]
        [JsonSerializable(typeof(Noted))]
        """;

    [Test]
    public async Task MirroredMemberOptions_MatchTheGolden()
    {
        var ts = TranspileContext(GoldenDeclarations);

        await Assert
            .That(ts)
            .IsEqualTo(TranspileHelper.ReadExpected("json-member-options-context.ts"));
    }

    [Test]
    public async Task ByteArray_IsABytesDescriptorButAByteListIsNot()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Bins(byte[] A, List<byte> B, byte[][] C, Dictionary<string, byte[]> D);

            [Transpile]
            [JsonSerializable(typeof(Bins))]
            [JsonSerializable(typeof(byte[]))]
            [JsonSerializable(typeof(List<byte[]>))]
            """
        );

        await Assert.That(PropertyTypeOf(ts, "a")).IsEqualTo("""{ kind: "bytes" }""");

        await Assert
            .That(PropertyTypeOf(ts, "b"))
            .IsEqualTo("""{ kind: "array", element: { kind: "primitive" } }""");

        await Assert
            .That(PropertyTypeOf(ts, "c"))
            .IsEqualTo("""{ kind: "array", element: { kind: "bytes" } }""");

        await Assert
            .That(PropertyTypeOf(ts, "d"))
            .IsEqualTo("""{ kind: "map", key: { kind: "primitive" }, value: { kind: "bytes" } }""");

        await Assert.That(ts).Contains("get byteArray(): ValueSpec<number[] | null> {");
        await Assert.That(ts).Contains("get listByteArray(): ValueSpec<number[][] | null> {");
    }

    [Test]
    public async Task JsonInclude_CollectsReachableMembersOnly()
    {
        var (files, diagnostics) = Transpile(
            """
            [Transpile]
            public sealed class Inc
            {
                public int Pub { get; set; }

                [JsonInclude]
                internal int Internal { get; set; }

                [JsonInclude]
                protected internal int ProtectedInternal { get; set; }

                [JsonInclude]
                private int Secret { get; set; }

                [JsonInclude]
                protected int Guarded { get; set; }

                [JsonInclude]
                public int PublicField;

                public int PlainField;

                [JsonInclude]
                internal readonly int ReadOnlyField;

                internal int NotIncluded { get; set; }

                [JsonInclude]
                public static int Shared { get; set; }
            }

            [Transpile]
            [JsonSerializable(typeof(Inc))]
            """
        );

        var ts = files[ContextPath];

        await Assert
            .That(TsNamesOf(ts))
            .IsEqualTo("pub, internal, protectedInternal, publicField, readOnlyField");

        await Assert.That(ts).Contains("factory: (p: Record<string, unknown>) => new Inc(),");

        await Assert
            .That(AssignedIfPresentOf(ts))
            .IsEqualTo("pub, internal, protectedInternal, publicField");

        await Assert
            .That(UnmirroredMessagesOf(diagnostics))
            .IsEqualTo(
                string.Join(
                    "\n",
                    [
                        "'Inc.Secret' has [JsonInclude], but the System.Text.Json source generator cannot "
                            + "access it (SYSLIB1038), so its contract throws InvalidOperationException; "
                            + "the TypeScript context does not mirror that.",
                        "'Inc.Guarded' has [JsonInclude], but the System.Text.Json source generator "
                            + "cannot access it (SYSLIB1038), so its contract throws "
                            + "InvalidOperationException; the TypeScript context does not mirror that.",
                        "'Inc.ReadOnlyField' has [JsonInclude] on a read-only field: "
                            + "System.Text.Json writes it and throws InvalidOperationException for a "
                            + "payload that carries it, the TypeScript context does not mirror that.",
                    ]
                )
            );
    }

    [Test]
    public async Task JsonInclude_OnAPublicPropertyWithAPrivateSetter_IsWrittenAndReported()
    {
        var (files, diagnostics) = Transpile(
            """
            [Transpile]
            public sealed class Counter
            {
                [JsonInclude]
                public int Count { get; private set; }

                public int Step { get; internal set; }

                [JsonInclude]
                public int Limit { get; internal set; }
            }

            [Transpile]
            [JsonSerializable(typeof(Counter))]
            """
        );

        var ts = files[ContextPath];

        await Assert.That(TsNamesOf(ts)).IsEqualTo("count, step, limit");

        await Assert.That(ts).Contains("factory: (p: Record<string, unknown>) => new Counter(),");
        await Assert.That(AssignedIfPresentOf(ts)).IsEqualTo("limit");

        await Assert
            .That(UnmirroredMessagesOf(diagnostics))
            .IsEqualTo(
                string.Join(
                    "\n",
                    [
                        "'Counter.Count' has [JsonInclude], but the System.Text.Json source generator "
                            + "cannot access it (SYSLIB1038), so its contract throws "
                            + "InvalidOperationException; the TypeScript context does not mirror that.",
                    ]
                )
            );
    }

    [Test]
    public async Task Fields_FollowTheTypesProperties()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public class IncBase
            {
                public int BProp { get; set; }

                [JsonInclude]
                public int BField;

                [JsonInclude]
                internal int BInternal { get; set; }
            }

            [Transpile]
            public sealed class IncDer : IncBase
            {
                [JsonInclude]
                public int DField;

                public int DProp { get; set; }
            }

            [Transpile]
            [JsonSerializable(typeof(IncDer))]
            """
        );

        await Assert.That(PropertiesOfGetter(ts, "incDer")).IsEqualTo("dProp, dField");

        await Assert.That(PropertiesOfGetter(ts, "incBase")).IsEqualTo("bProp, bInternal, bField");
    }

    [Test]
    public async Task PropertyOrder_IsEmittedOnlyWhenNotZero()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed class Ord
            {
                public int A { get; set; }

                [JsonPropertyOrder(-1)]
                public int B { get; set; }

                [JsonPropertyOrder(0)]
                public int C { get; set; }

                [JsonPropertyOrder(2)]
                public int D { get; set; }
            }

            [Transpile]
            [JsonSerializable(typeof(Ord))]
            """
        );

        await Assert.That(OrdersOf(ts)).IsEqualTo("b: -1, d: 2");
    }

    [Test]
    public async Task IgnoreConditions_AlwaysHidesNeverKeeps()
    {
        var (files, diagnostics) = Transpile(
            """
            [Transpile]
            public sealed class Flags
            {
                [JsonIgnore]
                public int Hidden { get; set; }

                [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
                public int AlsoHidden { get; set; }

                [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
                public int Shown { get; set; }

                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                public string? Note { get; set; }
            }

            [Transpile]
            [JsonSerializable(typeof(Flags))]
            """
        );

        await Assert.That(TsNamesOf(files[ContextPath])).IsEqualTo("shown, note");

        await Assert
            .That(UnmirroredMessagesOf(diagnostics))
            .IsEqualTo(
                string.Join(
                    "\n",
                    [
                        "'Flags.Note' has [JsonIgnore(Condition = WhenWritingNull)]: the TypeScript "
                            + "context always writes and reads it.",
                    ]
                )
            );
    }

    [Test]
    public async Task ExtensionData_IsLeftOutAndReported()
    {
        var (files, diagnostics) = Transpile(
            """
            [Transpile]
            public sealed class Open
            {
                public int Id { get; set; }

                [JsonExtensionData]
                public Dictionary<string, object>? Extra { get; set; }
            }

            [Transpile]
            [JsonSerializable(typeof(Open))]
            """
        );

        await Assert.That(TsNamesOf(files[ContextPath])).IsEqualTo("id");

        await Assert
            .That(UnmirroredMessagesOf(diagnostics))
            .IsEqualTo(
                string.Join(
                    "\n",
                    [
                        "'Open.Extra' is [JsonExtensionData]: System.Text.Json writes its entries as "
                            + "properties of the object and collects unmapped ones into it, the "
                            + "TypeScript context leaves it out.",
                    ]
                )
            );
    }

    // A record's positional members are bound by its constructor; only the
    // other settable members are assigned, inherited ones included.
    [Test]
    public async Task ConstructorBoundMembers_AreNotAssignedAgain()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Point(double X, double Y)
            {
                public double Z { get; init; }

                public double Length => X + Y;
            }

            [Transpile]
            [JsonSerializable(typeof(Point))]
            """
        );

        await Assert
            .That(ts)
            .Contains(
                "factory: (p: Record<string, unknown>) => Object.assign(new Point(p.x as number, p.y as number), { z: p.z as number }),"
            );
    }

    // STJ: `public int A { private get; set; }`, `{ protected get; set; }` and
    // `{ internal get; set; }` are neither written nor read; `{ get; private
    // set; }` is written, not read; `[JsonInclude]` with an internal getter is
    // written and read.
    [Test]
    public async Task PropertyWithANonPublicGetter_IsLeftOutUnlessIncluded()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed class Getters
            {
                public int Hidden { private get; set; }

                public int Guarded { protected get; set; }

                public int Internal { internal get; set; }

                [JsonInclude]
                public int Included { internal get; set; }

                public int ReadOnly { get; private set; }

                public int Z { get; set; }
            }

            [Transpile]
            [JsonSerializable(typeof(Getters))]
            """
        );

        await Assert.That(TsNamesOf(ts)).IsEqualTo("included, readOnly, z");
        await Assert.That(AssignedIfPresentOf(ts)).IsEqualTo("included, z");
    }

    // STJ, a context of another assembly: an internal [JsonInclude] member of
    // a library without InternalsVisibleTo is skipped (`{"A":1}`), a
    // protected internal one throws; with InternalsVisibleTo both are written
    // and read (`{"A":1,"B":2,"F":4}`, `{"A":1,"C":3}`).
    [Test]
    [Arguments("", "a", 3)]
    [Arguments(
        "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TestConsumer\")]",
        "a, b, c, f",
        0
    )]
    public async Task IncludedInternalsOfAnotherAssembly_NeedInternalsVisibleTo(
        string grant,
        string tsNames,
        int warningCount
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            $$"""
            using System.Text.Json.Serialization;

            {{grant}}
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("shared", Version = "^2.0.0")]

            namespace Shared;

            public class XInc
            {
                public int A { get; set; }

                [JsonInclude]
                internal int B { get; set; }

                [JsonInclude]
                protected internal int C { get; set; }

                [JsonInclude]
                internal int F;
            }
            """,
            WrapInNamespace(
                """
                [Transpile]
                [JsonSerializable(typeof(Shared.XInc))]
                """
            )
        );

        await Assert.That(TsNamesOf(files[ContextPath])).IsEqualTo(tsNames);

        await Assert
            .That(
                diagnostics.Count(diagnostic =>
                    diagnostic.Code == DiagnosticCodes.UnmirroredJsonOption
                )
            )
            .IsEqualTo(warningCount);
    }

    [Test]
    public async Task IncludedInternalOfAnotherAssembly_IsReportedAsSkipped()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            """
            using System.Text.Json.Serialization;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("shared", Version = "^2.0.0")]

            namespace Shared;

            public class XInc
            {
                public int A { get; set; }

                [JsonInclude]
                internal int B { get; set; }
            }
            """,
            WrapInNamespace(
                """
                [Transpile]
                [JsonSerializable(typeof(Shared.XInc))]
                """
            )
        );

        await Assert
            .That(UnmirroredMessagesOf(diagnostics))
            .IsEqualTo(
                "'XInc.B' has [JsonInclude], but it is internal to 'TestLibrary', which grants "
                    + "the context's assembly no InternalsVisibleTo: System.Text.Json skips it, "
                    + "and so does the TypeScript context."
            );
    }

    // STJ throws InvalidOperationException for an extension data member bound
    // to a constructor parameter ("cannot bind with a parameter in the
    // deserialization constructor"); the factory passes null.
    [Test]
    public async Task ConstructorBoundExtensionData_IsPassedNull()
    {
        var (files, _) = Transpile(
            """
            [Transpile]
            public sealed record Ext(int A, [property: JsonExtensionData] Dictionary<string, object>? Extra);

            [Transpile]
            [JsonSerializable(typeof(Ext))]
            """
        );

        await Assert
            .That(files[ContextPath])
            .Contains("factory: (p: Record<string, unknown>) => new Ext(p.a as number, null),");
    }

    // A base declaration hidden by a [JsonIgnore] override is never read, so
    // it is not assigned either (it would overwrite the default with
    // undefined).
    [Test]
    public async Task IgnoredOverride_IsNotAssigned()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public class Root
            {
                public virtual string Name { get; init; } = "";

                public int Base { get; init; }
            }

            [Transpile]
            public sealed class Derived : Root
            {
                [JsonIgnore]
                public override string Name { get; init; } = "d";

                public int X { get; init; }
            }

            [Transpile]
            [JsonSerializable(typeof(Derived))]
            """
        );

        await Assert
            .That(ts)
            .Contains(
                "factory: (p: Record<string, unknown>) => Object.assign(new Derived(), { base: p.base as number, x: p.x as number }),"
            );
    }

    // FR-040: a record whose members are all bound by its constructor keeps
    // the plain `new T(...)` factory.
    [Test]
    public async Task PositionalRecord_KeepsThePlainFactory()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            public sealed record Point(double X, double Y);

            [Transpile]
            [JsonSerializable(typeof(Point))]
            """
        );

        await Assert
            .That(ts)
            .Contains(
                "factory: (p: Record<string, unknown>) => new Point(p.x as number, p.y as number),"
            );
    }

    private static string WrapInNamespace(string declarations) =>
        $$"""
            using System.Collections.Generic;
            using System.Text.Json.Serialization;

            namespace TestApp;

            {{declarations}}

            {{ContextBoilerplate}}
            """;

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) Transpile(string declarations) =>
        TranspileHelper.TranspileWithDiagnostics(WrapInNamespace(declarations));

    private static string TranspileContext(string declarations)
    {
        var (files, diagnostics) = Transpile(declarations);

        if (diagnostics.Count > 0)
        {
            var formatted = string.Join("; ", diagnostics.Select(d => d.Format()));

            throw new InvalidOperationException("Unexpected diagnostics: " + formatted);
        }

        return files[ContextPath];
    }

    /// <summary>The <c>MS0037</c> messages, one per line, in report order.</summary>
    private static string UnmirroredMessagesOf(IReadOnlyList<MetanoDiagnostic> diagnostics) =>
        string.Join(
            "\n",
            diagnostics
                .Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnmirroredJsonOption)
                .Select(diagnostic => diagnostic.Message)
        );

    /// <summary>The <c>ts</c> names of every property spec, in emission order.</summary>
    private static string TsNamesOf(string ts) =>
        string.Join(
            ", ",
            Regex.Matches(ts, "ts: \"([^\"]+)\"").Select(match => match.Groups[1].Value)
        );

    /// <summary>The <c>ts</c> names of the property specs of one getter.</summary>
    private static string PropertiesOfGetter(string ts, string getter)
    {
        var start = ts.IndexOf($"  get {getter}(", StringComparison.Ordinal);
        var end = ts.IndexOf("\n  get ", start + 1, StringComparison.Ordinal);

        return TsNamesOf(end < 0 ? ts[start..] : ts[start..end]);
    }

    /// <summary>The <c>ts</c> names of the property specs with <c>assignIfPresent: true</c>.</summary>
    private static string AssignedIfPresentOf(string ts) =>
        string.Join(
            ", ",
            Regex
                .Matches(ts, "ts: \"([^\"]+)\"(?:(?!ts: )[\\s\\S])*?assignIfPresent: true")
                .Select(match => match.Groups[1].Value)
        );

    /// <summary><c>ts: order</c> for every property spec with an <c>order</c>.</summary>
    private static string OrdersOf(string ts) =>
        string.Join(
            ", ",
            Regex
                .Matches(ts, "ts: \"([^\"]+)\"(?:(?!ts: )[\\s\\S])*?order: (-?\\d+)")
                .Select(match => $"{match.Groups[1].Value}: {match.Groups[2].Value}")
        );

    /// <summary>
    /// The <c>type</c> descriptor of the property spec named
    /// <paramref name="tsName"/>, on one line.
    /// </summary>
    private static string PropertyTypeOf(string ts, string tsName)
    {
        var start = ts.IndexOf($"ts: \"{tsName}\"", StringComparison.Ordinal);
        var typeStart = ts.IndexOf("type: ", start, StringComparison.Ordinal) + "type: ".Length;
        var depth = 0;
        var end = typeStart;

        do
        {
            depth += ts[end] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0,
            };

            end++;
        } while (depth > 0);

        var collapsed = Regex.Replace(ts[typeStart..end], "\\s+", " ");

        return collapsed.Replace(", }", " }");
    }
}
