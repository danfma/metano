using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SampleJsonContracts;

namespace Metano.Tests.Json;

/// <summary>
/// The System.Text.Json side of the shared conformance manifest
/// (spec 009 User Story 5, contracts/wire-format.md): every manifest row
/// whose <c>sides</c> include <c>stj</c> runs through the sample's own
/// contexts, whose <c>Default</c> instances the System.Text.Json source
/// generator builds. Write and roundtrip rows compare the serialized text
/// byte for byte (<c>stjJson</c> for the rows with a <c>divergence</c> note),
/// read and roundtrip rows assert the runtime type and the builder's field
/// values, and error rows assert the thrown exception class and the whole
/// message (<see cref="NormalizedStjError"/>). A row marked
/// <c>crossReadable</c> also reads the TypeScript text. A row that needs a
/// builder and has none fails, and a builder without a manifest row fails
/// too, so the two sides cannot drift by omission.
///
/// <para>Setting <c>METANO_UPDATE_STJ_FIXTURES=1</c> rewrites the
/// <c>json</c> / <c>stjJson</c> text of the rows STJ writes differently
/// (local regeneration only; CI never sets it). Rows the TypeScript side
/// also checks get their STJ text in <c>stjJson</c>, keeping <c>json</c> as
/// the shared or TypeScript text.</para>
/// </summary>
public class StjConformanceFixtureTests
{
    private const string StjSide = "stj";

    private const string TsSide = "ts";

    private const string SampleNamespace = "SampleJsonContracts.";

    private const string PathMarker = " Path: ";

    private const string PositionMarker = " | ";

    /// <summary>The <c>Guid</c> key of row V21.</summary>
    private const string V21Guid = "0f8fad5b-d9cb-469f-a165-70867728950e";

    private static readonly ConformanceCase[] Cases = LoadCases();

    private static readonly bool UpdateFixtures =
        Environment.GetEnvironmentVariable("METANO_UPDATE_STJ_FIXTURES") == "1";

    private static readonly Dictionary<string, JsonSerializerContext> Contexts = new()
    {
        ["StudioJsonContext"] = StudioJsonContext.Default,
        ["WebJsonContext"] = WebJsonContext.Default,
        ["OutOfOrderJsonContext"] = OutOfOrderJsonContext.Default,
        ["IgnoreJsonContext"] = IgnoreJsonContext.Default,
        ["SnakeCaseJsonContext"] = SnakeCaseJsonContext.Default,
        ["KebabCaseJsonContext"] = KebabCaseJsonContext.Default,
        ["MembersJsonContext"] = MembersJsonContext.Default,
    };

    private static readonly Dictionary<string, StjCaseBuilder> Builders = BuildBuilders();

    [Test]
    public async Task EveryStjRowThatNeedsABuilderHasOne()
    {
        var missing = StjRows()
            .Where(RequiresBuilder)
            .Select(row => row.Id)
            .Where(id => !Builders.ContainsKey(id))
            .ToList();

        await Assert.That(missing).IsEmpty();
    }

    /// <summary>
    /// Checked against the rows that need a builder: unlike the TypeScript
    /// side, which keeps a read error's builder for its <c>viaText</c> flag,
    /// this side has no per-row flag, so a read error never has a builder.
    /// </summary>
    [Test]
    public async Task EveryStjBuilderHasAManifestRow()
    {
        var rowIds = StjRows().Where(RequiresBuilder).Select(row => row.Id).ToHashSet();

        var extra = Builders.Keys.Where(id => !rowIds.Contains(id)).ToList();

        await Assert.That(extra).IsEmpty();
    }

    [Test]
    public async Task ConformanceRowsMatchSystemTextJson()
    {
        var failures = new List<string>();
        var anyTextUpdated = false;

        foreach (var row in StjRows())
        {
            try
            {
                anyTextUpdated |= await RunCase(row);
            }
            catch (Exception error)
            {
                failures.Add(
                    $"{row.Id} ({row.Direction} {row.Context}.{row.Getter}): {error.Message}"
                );
            }
        }

        if (anyTextUpdated)
        {
            WriteManifest();
        }

        await Assert.That(failures).IsEmpty();
    }

    private static IEnumerable<ConformanceCase> StjRows() =>
        Cases.Where(row => RunsOn(row, StjSide));

    private static bool RunsOn(ConformanceCase row, string side) =>
        row.Sides is null || row.Sides.Contains(side);

    /// <summary>
    /// Every row needs a builder except a read error: its input is the
    /// manifest text and its expectation the manifest error.
    /// </summary>
    private static bool RequiresBuilder(ConformanceCase row) => !IsReadError(row);

    private static bool IsReadError(ConformanceCase row) =>
        row.Error is not null && row.Direction == "read";

    /// <summary>Runs one row; true when it recorded a text update.</summary>
    private static async Task<bool> RunCase(ConformanceCase row)
    {
        var typeInfo = TypeInfoOf(row);

        if (row.Error is { } error)
        {
            await AssertErrorRow(row, error, typeInfo);

            return false;
        }

        var builder = Builders[row.Id];

        var (text, updated) =
            row.Direction == "read"
                ? (row.Json!, false)
                : await AssertWrittenText(row, builder, typeInfo);

        if (row.Direction is "read" or "roundtrip")
        {
            await AssertReadValue(row, builder, typeInfo, text);
        }

        if (row.CrossReadable == true)
        {
            await AssertReadValue(row, builder, typeInfo, row.Json!);
        }

        return updated;
    }

    private static async Task AssertErrorRow(
        ConformanceCase row,
        JsonError error,
        JsonTypeInfo typeInfo
    )
    {
        var thrown = IsReadError(row)
            ? Catch(() => JsonSerializer.Deserialize(row.Json!, typeInfo))
            : Catch(() => JsonSerializer.Serialize(Builders[row.Id].Build!(), typeInfo));

        await Assert.That(thrown).IsNotNull();

        await Assert.That(thrown!.GetType().Name).IsEqualTo(error.Class);

        var (message, path) = NormalizedStjError(thrown, error.NamespaceInsensitive == true);

        await Assert.That(message).IsEqualTo(error.StjMessage ?? error.Message);

        if (error.StjPath is { } expectedPath)
        {
            await Assert.That(path).IsEqualTo(expectedPath);
        }
    }

    /// <summary>
    /// Serializes the builder's value once and compares it with the row's
    /// STJ text, or records the text when regenerating. Returns the written
    /// text, which a roundtrip row then reads.
    /// </summary>
    private static async Task<(string Text, bool Updated)> AssertWrittenText(
        ConformanceCase row,
        StjCaseBuilder builder,
        JsonTypeInfo typeInfo
    )
    {
        var text = JsonSerializer.Serialize(builder.Build!(), typeInfo);
        var expected = row.StjJson ?? row.Json!;

        if (UpdateFixtures && text != expected)
        {
            RecordTextUpdate(row, text);

            return (text, true);
        }

        await Assert.That(text).IsEqualTo(expected);

        return (text, false);
    }

    private static async Task AssertReadValue(
        ConformanceCase row,
        StjCaseBuilder builder,
        JsonTypeInfo typeInfo,
        string text
    )
    {
        var value = JsonSerializer.Deserialize(text, typeInfo);

        if (row.ExpectedClass is { } expectedClass)
        {
            await Assert.That(value?.GetType().Name).IsEqualTo(expectedClass);
        }

        if (builder.AssertRead is { } assertRead)
        {
            await assertRead(value);
        }
    }

    /// <summary>
    /// The message STJ threw, without the <c>Path: … | LineNumber: … |
    /// BytePositionInLine: …</c> suffix the TypeScript side never writes
    /// (DECIDED-5). A <c>namespaceInsensitive</c> row also drops the sample's
    /// namespace, which STJ prints in its polymorphic messages and the
    /// runtime does not (DECIDED-23 g); every other row compares it as
    /// written. The JSON path comes back separately, for the rows that pin it.
    /// </summary>
    private static (string Message, string? Path) NormalizedStjError(
        Exception error,
        bool namespaceInsensitive
    )
    {
        var (message, path) = SplitPath(error.Message);

        return (namespaceInsensitive ? message.Replace(SampleNamespace, "") : message, path);
    }

    private static (string Message, string? Path) SplitPath(string message)
    {
        var pathStart = message.IndexOf(PathMarker, StringComparison.Ordinal);

        if (pathStart < 0)
        {
            return (message, null);
        }

        var location = message[(pathStart + PathMarker.Length)..];
        var positionStart = location.IndexOf(PositionMarker, StringComparison.Ordinal);
        var path = positionStart < 0 ? location.TrimEnd('.') : location[..positionStart];

        return (message[..pathStart], path);
    }

    private static void RecordTextUpdate(ConformanceCase row, string text)
    {
        if (RunsOn(row, TsSide))
        {
            row.StjJson = text;
        }
        else
        {
            row.Json = text;
        }
    }

    private static void WriteManifest()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        File.WriteAllText(ManifestPath(), JsonSerializer.Serialize(Cases, options) + "\n");
    }

    private static JsonTypeInfo TypeInfoOf(ConformanceCase row)
    {
        var context = Contexts[row.Context];

        var property =
            context.GetType().GetProperty(PascalCase(row.Getter))
            ?? throw new InvalidOperationException(
                $"no STJ property '{PascalCase(row.Getter)}' on {context.GetType().Name} (case {row.Id})"
            );

        return (JsonTypeInfo)property.GetValue(context)!;
    }

    private static string PascalCase(string getter) =>
        char.ToUpperInvariant(getter[0]) + getter[1..];

    private static Exception? Catch(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            return error;
        }

        return null;
    }

    private static ConformanceCase[] LoadCases()
    {
        var manifest = File.ReadAllText(ManifestPath());

        var cases = JsonSerializer.Deserialize<ConformanceCase[]>(
            manifest,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );

        return cases
            ?? throw new InvalidOperationException("the conformance manifest parsed to null");
    }

    private static string ManifestPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "targets",
                "js",
                "sample-json-contracts",
                "test",
                "fixtures",
                "stj",
                "cases.json"
            );

            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("cases.json not found above the test output directory");
    }

    private static StjCaseBuilder WriteOnly(Func<object?> build) => new(build, null);

    /// <summary>A write row whose <c>crossReadable</c> read of the TypeScript text is checked.</summary>
    private static StjCaseBuilder WriteAndCrossRead(
        Func<object?> build,
        Func<object?, Task> assertCrossRead
    ) => new(build, assertCrossRead);

    private static StjCaseBuilder ReadOnly(Func<object?, Task> assertRead) => new(null, assertRead);

    private static StjCaseBuilder Roundtrip(Func<object?> build, Func<object?, Task> assertRead) =>
        new(build, assertRead);

    /// <summary>The builders, in manifest order.</summary>
    private static Dictionary<string, StjCaseBuilder> BuildBuilders() =>
        new()
        {
            ["W1"] = Roundtrip(
                () => new ImageClip("a.png", 1.5),
                async value =>
                {
                    var clip = (ImageClip)value!;

                    await Assert.That(clip.Path).IsEqualTo("a.png");

                    await Assert.That(clip.Seconds).IsEqualTo(1.5);
                }
            ),
            ["W2"] = Roundtrip(
                () => new SilenceClip(2),
                async value => await Assert.That(((SilenceClip)value!).Seconds).IsEqualTo(2)
            ),
            ["W3"] = Roundtrip(
                () => new ImageClip("a.png", 1.5),
                async value => await Assert.That(((ImageClip)value!).Path).IsEqualTo("a.png")
            ),
            ["W4"] = Roundtrip(
                () => new Timeline("t", new ImageClip("a.png", 1.5), [new SilenceClip(2)]),
                async value =>
                {
                    var timeline = (Timeline)value!;

                    await Assert.That(timeline.Name).IsEqualTo("t");

                    await Assert.That(timeline.Main).IsTypeOf<ImageClip>();

                    await Assert.That(timeline.Clips.Count).IsEqualTo(1);

                    await Assert.That(timeline.Clips[0]).IsTypeOf<SilenceClip>();
                }
            ),
            ["W5"] = Roundtrip(
                () => new List<Clip> { new ImageClip("a", 1.5), new SilenceClip(2) },
                async value =>
                {
                    var clips = (List<Clip>)value!;

                    await Assert.That(clips.Count).IsEqualTo(2);

                    await Assert.That(clips[0]).IsTypeOf<ImageClip>();

                    await Assert.That(((ImageClip)clips[0]).Path).IsEqualTo("a");

                    await Assert.That(clips[1]).IsTypeOf<SilenceClip>();
                }
            ),
            ["W6"] = Roundtrip(
                () => new Dictionary<string, Clip> { ["x"] = new SilenceClip(2) },
                async value =>
                {
                    var map = (Dictionary<string, Clip>)value!;

                    await Assert.That(map["x"]).IsTypeOf<SilenceClip>();

                    await Assert.That(((SilenceClip)map["x"]).Seconds).IsEqualTo(2);
                }
            ),
            ["W7"] = Roundtrip(
                () => new HLeaf(1, 2, 3),
                async value =>
                {
                    var leaf = (HLeaf)value!;

                    await Assert.That(leaf.A).IsEqualTo(1);

                    await Assert.That(leaf.B).IsEqualTo(2);

                    await Assert.That(leaf.C).IsEqualTo(3);
                }
            ),
            ["W8"] = WriteOnly(() => new HLeaf(1, 2, 3)),
            ["W9"] = Roundtrip(
                () => new IA(5),
                async value => await Assert.That(((IA)value!).X).IsEqualTo(5)
            ),
            ["W10"] = Roundtrip(
                () => new IS(2),
                async value => await Assert.That(((IS)value!).Y).IsEqualTo(2)
            ),
            ["W11"] = Roundtrip(
                () => new DBase(),
                async value => await Assert.That(value).IsNotNull()
            ),
            ["W11b"] = Roundtrip(
                () => new SBase(1),
                async value => await Assert.That(((SBase)value!).V).IsEqualTo(1)
            ),
            ["W11c"] = Roundtrip(
                () => new DBer(2),
                async value => await Assert.That(((DBer)value!).W).IsEqualTo(2)
            ),
            ["W12"] = WriteOnly(() => new UNearB(1, 2, 3)),
            ["W13"] = WriteOnly(() => new UBaseX(1, 2)),
            ["W15"] = Roundtrip(
                () => new[] { new EpisodeSummary("a", "b") },
                async value =>
                {
                    var summaries = (EpisodeSummary[])value!;

                    await Assert.That(summaries.Length).IsEqualTo(1);

                    await Assert.That(summaries[0].Id).IsEqualTo("a");

                    await Assert.That(summaries[0].Title).IsEqualTo("b");
                }
            ),
            ["W17"] = Roundtrip(
                () => new NMid(1, 2),
                async value =>
                {
                    var mid = (NMid)value!;

                    await Assert.That(mid.A).IsEqualTo(1);

                    await Assert.That(mid.B).IsEqualTo(2);
                }
            ),
            ["W18"] = Roundtrip(
                () => new NLeaf(1, 2, 3),
                async value =>
                {
                    var leaf = (NLeaf)value!;

                    await Assert.That(leaf.A).IsEqualTo(1);

                    await Assert.That(leaf.B).IsEqualTo(2);

                    await Assert.That(leaf.C).IsEqualTo(3);
                }
            ),
            ["W19"] = Roundtrip(
                () =>
                    new ODer
                    {
                        X = 2,
                        Name = "x",
                        Base = 1,
                    },
                async value =>
                {
                    var der = (ODer)value!;

                    await Assert.That(der.X).IsEqualTo(2);

                    await Assert.That(der.Name).IsEqualTo("x");

                    await Assert.That(der.Base).IsEqualTo(1);
                }
            ),
            ["W20"] = Roundtrip(
                () => new GDer(2),
                async value =>
                {
                    var der = (GDer)value!;

                    await Assert.That(der.Extra).IsEqualTo(2);

                    await Assert.That(der.Value).IsEqualTo(1);
                }
            ),
            ["W21"] = Roundtrip(
                () => new Feed(new Page<EpisodeSummary>([new EpisodeSummary("a", "b")])),
                async value =>
                {
                    var feed = (Feed)value!;

                    await Assert.That(feed.Latest.Items.Count).IsEqualTo(1);

                    await Assert.That(feed.Latest.Items[0].Id).IsEqualTo("a");
                }
            ),
            ["W22"] = Roundtrip(() => null, async value => await Assert.That(value).IsNull()),
            ["W22b"] = Roundtrip(() => null, async value => await Assert.That(value).IsNull()),
            // A lazy sequence, as the TypeScript side writes a generator (FR-038).
            ["W23"] = WriteOnly(() => Enumerable.Repeat<Clip>(new SilenceClip(2), 1)),
            ["W24"] = Roundtrip(
                () => new Sq(2),
                async value => await Assert.That(((Sq)value!).Side).IsEqualTo(2)
            ),
            ["W25"] = WriteOnly(() => new OtherB(3)),
            ["W26"] = Roundtrip(
                () => new Point(1),
                async value => await Assert.That(((Point)value!).X).IsEqualTo(1)
            ),
            ["W27"] = Roundtrip(
                () => new Point3(1, 2, 3),
                async value =>
                {
                    var point = (Point3)value!;

                    await Assert.That(point.X).IsEqualTo(1);

                    await Assert.That(point.Y).IsEqualTo(2);

                    await Assert.That(point.Z).IsEqualTo(3);
                }
            ),
            ["W28a"] = Roundtrip(
                () => new MixedClass(2),
                async value => await Assert.That(((MixedClass)value!).W).IsEqualTo(2)
            ),
            ["W28b"] = Roundtrip(
                () => new MixedPoint(5),
                async value => await Assert.That(((MixedPoint)value!).X).IsEqualTo(5)
            ),
            ["W29"] = Roundtrip(
                () => new Rect(1, 2),
                async value =>
                {
                    var rect = (Rect)value!;

                    await Assert.That(rect.W).IsEqualTo(1);

                    await Assert.That(rect.H).IsEqualTo(2);
                }
            ),
            ["W30"] = Roundtrip(
                () => new SqB(4, 2),
                async value =>
                {
                    var square = (SqB)value!;

                    await Assert.That(square.Area).IsEqualTo(4);

                    await Assert.That(square.Side).IsEqualTo(2);
                }
            ),
            ["R1"] = ReadOnly(async value =>
            {
                var clip = (ImageClip)value!;

                await Assert.That(clip.Path).IsEqualTo("a");

                await Assert.That(clip.Seconds).IsEqualTo(1);
            }),
            ["R2"] = ReadOnly(async value => await Assert.That(((IgnBase)value!).V).IsEqualTo(1)),
            ["R3"] = ReadOnly(async value =>
                await Assert.That(((ImageClip)value!).Path).IsEqualTo("a")
            ),
            ["R4"] = ReadOnly(async value =>
            {
                var mid = (HMid)value!;

                await Assert.That(mid.A).IsEqualTo(1);

                await Assert.That(mid.B).IsEqualTo(2);
            }),
            ["R5"] = ReadOnly(async value =>
            {
                var mid = (NMid)value!;

                await Assert.That(mid.A).IsEqualTo(1);

                await Assert.That(mid.B).IsEqualTo(2);
            }),
            ["R6"] = ReadOnly(async value => await Assert.That(((Point)value!).X).IsEqualTo(1)),
            ["R7"] = ReadOnly(async value =>
            {
                var set = (SetClass)value!;

                await Assert.That(set.N).IsEqualTo(1);

                await Assert.That(set.Count).IsEqualTo(5);
            }),
            ["R8"] = ReadOnly(async value =>
            {
                var near = (UNearA)value!;

                await Assert.That(near.A).IsEqualTo(1);

                await Assert.That(near.B).IsEqualTo(2);
            }),
            ["V1"] = Roundtrip(
                () => "hello",
                async value => await Assert.That(value).IsEqualTo("hello")
            ),
            ["V2"] = Roundtrip(() => 42, async value => await Assert.That(value).IsEqualTo(42)),
            ["V3"] = Roundtrip(() => 2.5, async value => await Assert.That(value).IsEqualTo(2.5)),
            ["V4"] = Roundtrip(() => 7, async value => await Assert.That(value).IsEqualTo(7)),
            ["V5"] = Roundtrip(() => true, async value => await Assert.That((bool)value!).IsTrue()),
            ["V6"] = Roundtrip(
                () => new Guid("81473d0e-4b8d-4d63-b8c7-2f9e1a5d3c47"),
                async value =>
                    await Assert
                        .That(value)
                        .IsEqualTo(new Guid("81473d0e-4b8d-4d63-b8c7-2f9e1a5d3c47"))
            ),
            ["V7a"] = Roundtrip(
                () => 79228162514264337593543950335m,
                async value => await AssertDecimal(value, "79228162514264337593543950335")
            ),
            ["V7b"] = Roundtrip(
                () => 0.0000001m,
                async value => await AssertDecimal(value, "0.0000001")
            ),
            ["V8"] = Roundtrip(
                () => new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Unspecified),
                async value =>
                    await Assert
                        .That(
                            ((DateTime)value!).ToString(
                                "yyyy-MM-ddTHH:mm:ss.fff",
                                CultureInfo.InvariantCulture
                            )
                        )
                        .IsEqualTo("2026-01-02T03:04:05.678")
            ),
            ["V9"] = Roundtrip(
                () => new DateOnly(2026, 1, 2),
                async value =>
                    await Assert
                        .That(((DateOnly)value!).ToString("O", CultureInfo.InvariantCulture))
                        .IsEqualTo("2026-01-02")
            ),
            ["V10"] = Roundtrip(
                () => new TimeOnly(3, 4, 5, 678),
                async value =>
                    await Assert
                        .That(
                            ((TimeOnly)value!).ToString(
                                "HH:mm:ss.fffffff",
                                CultureInfo.InvariantCulture
                            )
                        )
                        .IsEqualTo("03:04:05.6780000")
            ),
            ["V11"] = Roundtrip(
                () => new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                async value =>
                    await Assert.That(((DateTimeOffset)value!).Offset).IsEqualTo(TimeSpan.Zero)
            ),
            ["V12"] = Roundtrip(
                () => new TimeSpan(1, 2, 3),
                async value => await Assert.That(((TimeSpan)value!).TotalSeconds).IsEqualTo(3723.0)
            ),
            ["V13"] = Roundtrip(
                () => Color.Green,
                async value => await Assert.That(value).IsEqualTo(Color.Green)
            ),
            ["V14"] = Roundtrip(
                () => Flavor.Sweet,
                async value => await Assert.That(value).IsEqualTo(Flavor.Sweet)
            ),
            ["V15"] = Roundtrip(
                () => new HashSet<string> { "a", "b" },
                async value => await Assert.That(((HashSet<string>)value!).Count).IsEqualTo(2)
            ),
            ["V16"] = Roundtrip(
                () =>
                    new Dictionary<string, EpisodeSummary> { ["x"] = new EpisodeSummary("a", "b") },
                async value =>
                    await Assert
                        .That(((Dictionary<string, EpisodeSummary>)value!)["x"].Id)
                        .IsEqualTo("a")
            ),
            ["V17"] = Roundtrip(
                () => new IntMapHolder(new Dictionary<int, string> { [5] = "e", [1] = "a" }),
                async value =>
                {
                    var holder = (IntMapHolder)value!;

                    await Assert.That(holder.ByKey[5]).IsEqualTo("e");

                    await Assert.That(holder.ByKey[1]).IsEqualTo("a");
                }
            ),
            ["V18"] = Roundtrip(
                () => new SPoint(1, 2),
                async value =>
                {
                    var point = (SPoint)value!;

                    await Assert.That(point.X).IsEqualTo(1);

                    await Assert.That(point.Y).IsEqualTo(2);
                }
            ),
            ["V19"] = Roundtrip(
                () => new List<List<int>> { new List<int> { 0 } },
                async value => await Assert.That(((List<List<int>>)value!)[0][0]).IsEqualTo(0)
            ),
            ["V20"] = Roundtrip(
                () =>
                    new LevelMapHolder(
                        new Dictionary<Level, string> { [Level.High] = "h", [Level.Low] = "l" }
                    ),
                async value =>
                {
                    var holder = (LevelMapHolder)value!;

                    await Assert.That(holder.ByLevel[Level.High]).IsEqualTo("h");

                    await Assert.That(holder.ByLevel[Level.Low]).IsEqualTo("l");
                }
            ),
            ["V21"] = Roundtrip(
                () => new GuidMapHolder(new Dictionary<Guid, string> { [new Guid(V21Guid)] = "a" }),
                async value =>
                {
                    var holder = (GuidMapHolder)value!;

                    await Assert.That(holder.ById[new Guid(V21Guid)]).IsEqualTo("a");
                }
            ),
            ["N1"] = Roundtrip(() => null, async value => await Assert.That(value).IsNull()),
            ["N2"] = Roundtrip(() => null, async value => await Assert.That(value).IsNull()),
            ["E8"] = WriteOnly(() => new UFailX(1)),
            ["E9"] = WriteOnly(() => new NLeaf(1, 2, 3)),
            ["E12"] = WriteOnly(() =>
            {
                var members = new List<Clip>();
                var group = new Group("g", members);

                members.Add(group);

                return group;
            }),
            ["E14"] = WriteOnly(() => new Other(3)),
            ["R9"] = ReadOnly(async value => await Assert.That(GroupDepth(value)).IsEqualTo(32)),
            ["D2a"] = ReadOnly(async value =>
                await AssertDecimal(value, "12345678901234567890.123456789")
            ),
            ["D2b"] = ReadOnly(async value =>
                await AssertDecimal(value, "0.3333333333333333333333333333")
            ),
            ["W14"] = Roundtrip(
                () => new ImageClip("a", 1.5),
                async value =>
                {
                    var clip = (ImageClip)value!;

                    await Assert.That(clip.Path).IsEqualTo("a");

                    await Assert.That(clip.Seconds).IsEqualTo(1.5);
                }
            ),
            ["WEB1"] = ReadOnly(async value =>
            {
                var clip = (ImageClip)value!;

                await Assert.That(clip.Path).IsEqualTo("a");

                await Assert.That(clip.Seconds).IsEqualTo(1);
            }),
            ["WEB2"] = ReadOnly(async value =>
                await Assert.That(((ImageClip)value!).Path).IsEqualTo("b")
            ),
            ["WEB3"] = ReadOnly(async value =>
            {
                var nums = (Nums)value!;

                await Assert.That(nums.N).IsEqualTo(5);

                await Assert.That(nums.M).IsEqualTo(7);

                await Assert.That(nums.D).IsEqualTo(1.10m);

                await Assert.That(nums.L.Count).IsEqualTo(2);

                await Assert.That(nums.Map["k"]).IsEqualTo(3);
            }),
            ["WEB3b"] = ReadOnly(async value =>
                await Assert.That(((Nums)value!).D).IsEqualTo(12345678901234567890.123456789m)
            ),
            ["WEB4a"] = ReadOnly(async value =>
                await Assert.That(((SoftNums)value!).N).IsEqualTo(5)
            ),
            ["WEB4b"] = ReadOnly(async value =>
                await Assert.That(((SoftNums)value!).N).IsEqualTo(5)
            ),
            ["WEB4c"] = ReadOnly(async value =>
                await Assert.That(((SoftNums)value!).N).IsEqualTo(7)
            ),
            ["WEB4d"] = ReadOnly(async value =>
                await Assert.That(((SoftNums)value!).N).IsEqualTo(-5)
            ),
            ["WEB5a"] = ReadOnly(async value =>
                await Assert.That(((SoftNums)value!).X).IsEqualTo(100)
            ),
            ["WEB5b"] = ReadOnly(async value =>
                await Assert.That(((SoftNums)value!).X).IsEqualTo(1.5)
            ),
            ["WEB5c"] = ReadOnly(async value =>
                await Assert.That(double.IsNaN(((SoftNums)value!).X ?? 0)).IsTrue()
            ),
            ["WEB5d"] = ReadOnly(async value =>
                await Assert.That(((SoftNums)value!).X).IsEqualTo(double.PositiveInfinity)
            ),
            ["WEB5e"] = ReadOnly(async value =>
                await Assert.That(((SoftNums)value!).X).IsEqualTo(double.NegativeInfinity)
            ),
            ["WEB6"] = ReadOnly(async value => await Assert.That(value).IsEqualTo(5)),
            // The list and map parameters are non-nullable in C#, but STJ
            // writes the null defaults of WEB7's partial value as null.
            ["WEB7"] = WriteAndCrossRead(
                () => new Nums(5, 7, 1.10m, 2.5, null!, null!, Color.Red),
                async value =>
                {
                    var nums = (Nums)value!;

                    await Assert.That(nums.D).IsEqualTo(1.1m);

                    await Assert.That(nums.X).IsEqualTo(2.5);
                }
            ),
            ["NAM1"] = Roundtrip(
                () => new Stats(1, 2.5, "u"),
                async value => await Assert.That(((Stats)value!).UserName).IsEqualTo("u")
            ),
            ["NAM2"] = Roundtrip(
                () => new Stats(1, 2.5, "u"),
                async value => await Assert.That(((Stats)value!).UserName).IsEqualTo("u")
            ),
            ["M1"] = Roundtrip(
                () => new Blob([1, 2, 3], [255, 254], [1, 2]),
                async value =>
                {
                    var blob = (Blob)value!;

                    await Assert.That(blob.Data.SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();

                    await Assert.That(blob.Maybe!.SequenceEqual(new byte[] { 255, 254 })).IsTrue();

                    await Assert.That(blob.Numbers.SequenceEqual(new List<byte> { 1, 2 })).IsTrue();
                }
            ),
            ["M2"] = Roundtrip(
                () =>
                    new Account
                    {
                        Name = "n",
                        Version = 3,
                        Id = "x",
                        Hits = 4,
                    },
                async value =>
                {
                    var account = (Account)value!;

                    await Assert.That(account.Name).IsEqualTo("n");

                    await Assert.That(account.Version).IsEqualTo(3);

                    await Assert.That(account.Id).IsEqualTo("x");

                    await Assert.That(account.Hits).IsEqualTo(4);
                }
            ),
            ["M3"] = Roundtrip(
                () => new OrdBase(5, 1, 3),
                async value =>
                {
                    var ord = (OrdBase)value!;

                    await Assert.That(ord.First).IsEqualTo(1);

                    await Assert.That(ord.Second).IsEqualTo(5);

                    await Assert.That(ord.Third).IsEqualTo(3);
                }
            ),
            ["M4"] = Roundtrip(
                () => new InitRec(1) { Note = "n" },
                async value =>
                {
                    var rec = (InitRec)value!;

                    await Assert.That(rec.A).IsEqualTo(1);

                    await Assert.That(rec.Note).IsEqualTo("n");
                }
            ),
            ["M5"] = Roundtrip(
                () => new SetClass { N = 1 },
                async value =>
                {
                    var set = (SetClass)value!;

                    await Assert.That(set.N).IsEqualTo(1);

                    await Assert.That(set.Count).IsEqualTo(5);
                }
            ),
        };

    private static async Task AssertDecimal(object? value, string text) =>
        await Assert.That(((decimal)value!).ToString(CultureInfo.InvariantCulture)).IsEqualTo(text);

    /// <summary>
    /// The number of nested groups of a read <see cref="Group"/> chain,
    /// whose innermost group has no members (manifest row R9).
    /// </summary>
    private static int GroupDepth(object? value)
    {
        var depth = 0;
        var current = value as Group;

        while (current is not null)
        {
            depth++;
            current = current.Members.Count == 0 ? null : (Group)current.Members[0];
        }

        return depth;
    }

    private sealed record StjCaseBuilder(Func<object?>? Build, Func<object?, Task>? AssertRead);

    private sealed record ConformanceCase
    {
        public string Id { get; set; } = "";

        public string Context { get; set; } = "";

        public string Getter { get; set; } = "";

        public string Direction { get; set; } = "";

        public string[]? Sides { get; set; }

        public string? ExpectedClass { get; set; }

        public string? Json { get; set; }

        public string? StjJson { get; set; }

        public bool? CrossReadable { get; set; }

        public JsonError? Error { get; set; }

        public string? Divergence { get; set; }
    }

    /// <summary>
    /// An error row's expectation: <c>message</c> when both sides throw the
    /// same normalized text, otherwise <c>stjMessage</c> / <c>tsMessage</c>;
    /// <c>stjPath</c> pins STJ's JSON path where its message names only the
    /// declaring type; <c>namespaceInsensitive</c> compares without the
    /// sample's namespace (DECIDED-23 g).
    /// </summary>
    private sealed record JsonError(
        string Class,
        string? Message,
        string? StjMessage,
        string? TsMessage,
        string? StjPath,
        bool? NamespaceInsensitive
    );
}
