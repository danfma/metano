using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SampleJsonContracts;

namespace Metano.Tests.Json;

/// <summary>
/// The System.Text.Json side of the shared conformance manifest
/// (spec 009 User Story 5, contracts/wire-format.md): every manifest row
/// whose <c>sides</c> include <c>stj</c> runs through a real source-generated
/// context over the sample's types. Write and roundtrip rows compare the
/// serialized text byte for byte (<c>stjJson</c> for the divergent rows),
/// read and roundtrip rows assert the runtime type and the builder's field
/// values, and error rows assert the thrown exception class and message
/// prefix. A manifest id without a builder fails, and a builder without a
/// manifest id fails too, so the two sides cannot drift by omission.
///
/// <para>Setting <c>METANO_UPDATE_STJ_FIXTURES=1</c> rewrites the
/// <c>json</c> / <c>stjJson</c> text of the rows STJ writes differently
/// (local regeneration only; CI never sets it). Rows the TypeScript side
/// also checks get their STJ text in <c>stjJson</c>, keeping <c>json</c> as
/// the shared or TypeScript text.</para>
/// </summary>
public class StjConformanceFixtureTests
{
    private static readonly ConformanceCase[] Cases = LoadCases();

    private static readonly bool UpdateFixtures =
        Environment.GetEnvironmentVariable("METANO_UPDATE_STJ_FIXTURES") == "1";

    private static readonly Dictionary<string, JsonSerializerContext> Contexts = new()
    {
        ["StudioJsonContext"] = StjStudioJsonContext.Default,
        ["WebJsonContext"] = StjWebJsonContext.Default,
        ["OutOfOrderJsonContext"] = StjOutOfOrderJsonContext.Default,
        ["IgnoreJsonContext"] = StjIgnoreJsonContext.Default,
        ["SnakeCaseJsonContext"] = StjSnakeCaseJsonContext.Default,
        ["KebabCaseJsonContext"] = StjKebabCaseJsonContext.Default,
        ["MembersJsonContext"] = StjMembersJsonContext.Default,
    };

    private static readonly string[] ErrorRowIds =
    [
        "E1",
        "E2",
        "E3",
        "E4",
        "E5",
        "E5bTrue",
        "E5bFalse",
        "E5bObject",
        "E5bArray",
        "E6",
        "E7",
        "E10",
        "E11",
        "E13",
        "E16",
        "E17a",
        "E17b",
        "E17c",
        "E17d",
        "E17e",
        "E17f",
        "E18a",
        "E18b",
        "E18c",
        "E19c",
        "E20",
        "E22",
        "E23",
        "E24",
        "E25",
        "N3",
        "N4",
        "N5",
        "N6",
        "N7",
        "N8",
        "N9",
        "D4",
    ];

    private static readonly Dictionary<string, StjCaseBuilder> Builders = BuildBuilders();

    [Test]
    public async Task EveryStjManifestRowHasABuilder()
    {
        var missing = StjRows()
            .Select(row => row.Id)
            .Where(id => !Builders.ContainsKey(id))
            .ToList();

        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task EveryStjBuilderHasAManifestRow()
    {
        var rowIds = StjRows().Select(row => row.Id).ToHashSet();

        var extra = Builders.Keys.Where(id => !rowIds.Contains(id)).ToList();

        await Assert.That(extra).IsEmpty();
    }

    [Test]
    public async Task ConformanceRowsMatchSystemTextJson()
    {
        var manifestPath = ManifestPath();
        var failures = new List<string>();

        foreach (var row in StjRows())
        {
            try
            {
                await RunCase(row, manifestPath);
            }
            catch (Exception error)
            {
                failures.Add(
                    $"{row.Id} ({row.Direction} {row.Context}.{row.Getter}): {error.Message}"
                );
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    private static IEnumerable<ConformanceCase> StjRows() =>
        Cases.Where(row => row.Sides is null || row.Sides.Contains("stj"));

    private static async Task RunCase(ConformanceCase row, string manifestPath)
    {
        var typeInfo = TypeInfoOf(row);
        var builder = Builders[row.Id];

        if (row.Error is { } error)
        {
            var thrown =
                row.Direction == "read"
                    ? Catch(() => JsonSerializer.Deserialize(row.Json!, typeInfo))
                    : Catch(() => JsonSerializer.Serialize(builder.Build!(), typeInfo));

            await Assert.That(thrown).IsNotNull();

            await Assert.That(thrown!.GetType().Name).IsEqualTo(error.Class);

            await Assert.That(thrown.Message).StartsWith(error.Message);

            return;
        }

        if (row.Direction is "write" or "roundtrip")
        {
            var text = JsonSerializer.Serialize(builder.Build!(), typeInfo);
            var expected = row.StjJson ?? row.Json!;

            if (UpdateFixtures && text != expected)
            {
                RecordTextUpdate(row, text, manifestPath);
            }
            else
            {
                await Assert.That(text).IsEqualTo(expected);
            }
        }

        if (row.Direction is "read" or "roundtrip")
        {
            var text =
                row.Direction == "read"
                    ? row.Json!
                    : JsonSerializer.Serialize(builder.Build!(), typeInfo);
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
    }

    private static void RecordTextUpdate(ConformanceCase row, string text, string manifestPath)
    {
        if (row.Sides is not null && row.Sides.Contains("ts"))
        {
            row.StjJson = text;
        }
        else
        {
            row.Json = text;
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(Cases, options) + "\n");
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

    private static Dictionary<string, StjCaseBuilder> BuildBuilders()
    {
        var builders = new Dictionary<string, StjCaseBuilder>
        {
            ["W1"] = new(
                () => new ImageClip("a.png", 1.5),
                async value =>
                {
                    var clip = (ImageClip)value!;

                    await Assert.That(clip.Path).IsEqualTo("a.png");

                    await Assert.That(clip.Seconds).IsEqualTo(1.5);
                }
            ),
            ["W2"] = new(
                () => new SilenceClip(2),
                async value => await Assert.That(((SilenceClip)value!).Seconds).IsEqualTo(2)
            ),
            ["W3"] = new(
                () => new ImageClip("a.png", 1.5),
                async value => await Assert.That(((ImageClip)value!).Path).IsEqualTo("a.png")
            ),
            ["W4"] = new(
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
            ["W5"] = new(
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
            ["W6"] = new(
                () => new Dictionary<string, Clip> { ["x"] = new SilenceClip(2) },
                async value =>
                {
                    var map = (Dictionary<string, Clip>)value!;

                    await Assert.That(map["x"]).IsTypeOf<SilenceClip>();

                    await Assert.That(((SilenceClip)map["x"]).Seconds).IsEqualTo(2);
                }
            ),
            ["W7"] = new(
                () => new HLeaf(1, 2, 3),
                async value =>
                {
                    var leaf = (HLeaf)value!;

                    await Assert.That(leaf.A).IsEqualTo(1);

                    await Assert.That(leaf.B).IsEqualTo(2);

                    await Assert.That(leaf.C).IsEqualTo(3);
                }
            ),
            ["W8"] = new(() => new HLeaf(1, 2, 3), null),
            ["W9"] = new(
                () => new IA(5),
                async value => await Assert.That(((IA)value!).X).IsEqualTo(5)
            ),
            ["W10"] = new(
                () => new IS(2),
                async value => await Assert.That(((IS)value!).Y).IsEqualTo(2)
            ),
            ["W11"] = new(() => new DBase(), async value => await Assert.That(value).IsNotNull()),
            ["W11b"] = new(
                () => new SBase(1),
                async value => await Assert.That(((SBase)value!).V).IsEqualTo(1)
            ),
            ["W11c"] = new(
                () => new DBer(2),
                async value => await Assert.That(((DBer)value!).W).IsEqualTo(2)
            ),
            ["W12"] = new(() => new UNearB(1, 2, 3), null),
            ["W13"] = new(() => new UBaseX(1, 2), null),
            ["W15"] = new(
                () => new[] { new EpisodeSummary("a", "b") },
                async value =>
                {
                    var summaries = (EpisodeSummary[])value!;

                    await Assert.That(summaries.Length).IsEqualTo(1);

                    await Assert.That(summaries[0].Id).IsEqualTo("a");

                    await Assert.That(summaries[0].Title).IsEqualTo("b");
                }
            ),
            ["W17"] = new(
                () => new NMid(1, 2),
                async value =>
                {
                    var mid = (NMid)value!;

                    await Assert.That(mid.A).IsEqualTo(1);

                    await Assert.That(mid.B).IsEqualTo(2);
                }
            ),
            ["W18"] = new(
                () => new NLeaf(1, 2, 3),
                async value =>
                {
                    var leaf = (NLeaf)value!;

                    await Assert.That(leaf.A).IsEqualTo(1);

                    await Assert.That(leaf.B).IsEqualTo(2);

                    await Assert.That(leaf.C).IsEqualTo(3);
                }
            ),
            ["W19"] = new(
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
            ["W20"] = new(
                () => new GDer(2),
                async value =>
                {
                    var der = (GDer)value!;

                    await Assert.That(der.Extra).IsEqualTo(2);

                    await Assert.That(der.Value).IsEqualTo(1);
                }
            ),
            ["W21"] = new(
                () => new Feed(new Page<EpisodeSummary>([new EpisodeSummary("a", "b")])),
                async value =>
                {
                    var feed = (Feed)value!;

                    await Assert.That(feed.Latest.Items.Count).IsEqualTo(1);

                    await Assert.That(feed.Latest.Items[0].Id).IsEqualTo("a");
                }
            ),
            ["W22"] = new(() => null, async value => await Assert.That(value).IsNull()),
            ["W22b"] = new(() => null, async value => await Assert.That(value).IsNull()),
            ["W24"] = new(
                () => new Sq(2),
                async value => await Assert.That(((Sq)value!).Side).IsEqualTo(2)
            ),
            ["W25"] = new(() => new OtherB(3), null),
            ["W26"] = new(
                () => new Point(1),
                async value => await Assert.That(((Point)value!).X).IsEqualTo(1)
            ),
            ["W27"] = new(
                () => new Point3(1, 2, 3),
                async value =>
                {
                    var point = (Point3)value!;

                    await Assert.That(point.X).IsEqualTo(1);

                    await Assert.That(point.Y).IsEqualTo(2);

                    await Assert.That(point.Z).IsEqualTo(3);
                }
            ),
            ["W28a"] = new(
                () => new MixedClass(2),
                async value => await Assert.That(((MixedClass)value!).W).IsEqualTo(2)
            ),
            ["W28b"] = new(
                () => new MixedPoint(5),
                async value => await Assert.That(((MixedPoint)value!).X).IsEqualTo(5)
            ),
            ["R1"] = new(
                null,
                async value =>
                {
                    var clip = (ImageClip)value!;

                    await Assert.That(clip.Path).IsEqualTo("a");

                    await Assert.That(clip.Seconds).IsEqualTo(1);
                }
            ),
            ["R2"] = new(null, async value => await Assert.That(((IgnBase)value!).V).IsEqualTo(1)),
            ["R3"] = new(
                null,
                async value => await Assert.That(((ImageClip)value!).Path).IsEqualTo("a")
            ),
            ["R4"] = new(
                null,
                async value =>
                {
                    var mid = (HMid)value!;

                    await Assert.That(mid.A).IsEqualTo(1);

                    await Assert.That(mid.B).IsEqualTo(2);
                }
            ),
            ["R5"] = new(
                null,
                async value =>
                {
                    var mid = (NMid)value!;

                    await Assert.That(mid.A).IsEqualTo(1);

                    await Assert.That(mid.B).IsEqualTo(2);
                }
            ),
            ["R6"] = new(null, async value => await Assert.That(((Point)value!).X).IsEqualTo(1)),
            ["R7"] = new(
                null,
                async value =>
                {
                    var set = (SetClass)value!;

                    await Assert.That(set.N).IsEqualTo(1);

                    await Assert.That(set.Count).IsEqualTo(5);
                }
            ),
            ["R8"] = new(
                null,
                async value =>
                {
                    var near = (UNearA)value!;

                    await Assert.That(near.A).IsEqualTo(1);

                    await Assert.That(near.B).IsEqualTo(2);
                }
            ),
            ["V1"] = new(() => "hello", async value => await Assert.That(value).IsEqualTo("hello")),
            ["V2"] = new(() => 42, async value => await Assert.That(value).IsEqualTo(42)),
            ["V3"] = new(() => 2.5, async value => await Assert.That(value).IsEqualTo(2.5)),
            ["V4"] = new(() => 7, async value => await Assert.That(value).IsEqualTo(7)),
            ["V5"] = new(() => true, async value => await Assert.That((bool)value!).IsTrue()),
            ["V6"] = new(
                () => new Guid("81473d0e-4b8d-4d63-b8c7-2f9e1a5d3c47"),
                async value =>
                    await Assert
                        .That(value)
                        .IsEqualTo(new Guid("81473d0e-4b8d-4d63-b8c7-2f9e1a5d3c47"))
            ),
            ["V7a"] = new(
                () => 79228162514264337593543950335m,
                async value =>
                    await Assert
                        .That(((decimal)value!).ToString(CultureInfo.InvariantCulture))
                        .IsEqualTo("79228162514264337593543950335")
            ),
            ["V7b"] = new(
                () => 0.0000001m,
                async value =>
                    await Assert
                        .That(((decimal)value!).ToString(CultureInfo.InvariantCulture))
                        .IsEqualTo("0.0000001")
            ),
            ["V8"] = new(
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
            ["V9"] = new(
                () => new DateOnly(2026, 1, 2),
                async value =>
                    await Assert
                        .That(((DateOnly)value!).ToString("O", CultureInfo.InvariantCulture))
                        .IsEqualTo("2026-01-02")
            ),
            ["V10"] = new(
                () => new TimeOnly(3, 4, 5, 678),
                async value =>
                    await Assert
                        .That(
                            ((TimeOnly)value!).ToString(
                                "HH:mm:ss.fff",
                                CultureInfo.InvariantCulture
                            )
                        )
                        .IsEqualTo("03:04:05.678")
            ),
            ["V11"] = new(
                () => new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                async value =>
                    await Assert.That(((DateTimeOffset)value!).Offset).IsEqualTo(TimeSpan.Zero)
            ),
            ["V12"] = new(
                () => new TimeSpan(1, 2, 3),
                async value => await Assert.That(((TimeSpan)value!).TotalSeconds).IsEqualTo(3723.0)
            ),
            ["V13"] = new(
                () => Color.Green,
                async value => await Assert.That(value).IsEqualTo(Color.Green)
            ),
            ["V14"] = new(
                () => Flavor.Sweet,
                async value => await Assert.That(value).IsEqualTo(Flavor.Sweet)
            ),
            ["V15"] = new(
                () => new HashSet<string> { "a", "b" },
                async value =>
                {
                    var set = (HashSet<string>)value!;

                    await Assert.That(set.Count).IsEqualTo(2);
                }
            ),
            ["V16"] = new(
                () =>
                    new Dictionary<string, EpisodeSummary> { ["x"] = new EpisodeSummary("a", "b") },
                async value =>
                {
                    var map = (Dictionary<string, EpisodeSummary>)value!;

                    await Assert.That(map["x"].Id).IsEqualTo("a");
                }
            ),
            ["V17"] = new(
                () => new IntMapHolder(new Dictionary<int, string> { [5] = "e", [1] = "a" }),
                async value =>
                {
                    var holder = (IntMapHolder)value!;

                    await Assert.That(holder.ByKey[5]).IsEqualTo("e");

                    await Assert.That(holder.ByKey[1]).IsEqualTo("a");
                }
            ),
            ["V18"] = new(
                () => new SPoint(1, 2),
                async value =>
                {
                    var point = (SPoint)value!;

                    await Assert.That(point.X).IsEqualTo(1);

                    await Assert.That(point.Y).IsEqualTo(2);
                }
            ),
            ["V19"] = new(
                () => new List<List<int>> { new List<int> { 0 } },
                async value =>
                {
                    var lists = (List<List<int>>)value!;

                    await Assert.That(lists[0][0]).IsEqualTo(0);
                }
            ),
            ["N1"] = new(() => null, async value => await Assert.That(value).IsNull()),
            ["N2"] = new(() => null, async value => await Assert.That(value).IsNull()),
            ["W14"] = new(
                () => new ImageClip("a", 1.5),
                async value =>
                {
                    var clip = (ImageClip)value!;

                    await Assert.That(clip.Path).IsEqualTo("a");

                    await Assert.That(clip.Seconds).IsEqualTo(1.5);
                }
            ),
            ["WEB1"] = new(
                null,
                async value =>
                {
                    var clip = (ImageClip)value!;

                    await Assert.That(clip.Path).IsEqualTo("a");

                    await Assert.That(clip.Seconds).IsEqualTo(1);
                }
            ),
            ["WEB2"] = new(
                null,
                async value => await Assert.That(((ImageClip)value!).Path).IsEqualTo("b")
            ),
            ["WEB3"] = new(
                null,
                async value =>
                {
                    var nums = (Nums)value!;

                    await Assert.That(nums.N).IsEqualTo(5);

                    await Assert.That(nums.M).IsEqualTo(7);

                    await Assert.That(nums.D).IsEqualTo(1.10m);

                    await Assert.That(nums.L.Count).IsEqualTo(2);

                    await Assert.That(nums.Map["k"]).IsEqualTo(3);
                }
            ),
            ["WEB3b"] = new(
                null,
                async value =>
                    await Assert.That(((Nums)value!).D).IsEqualTo(12345678901234567890.123456789m)
            ),
            ["WEB4a"] = new(
                null,
                async value => await Assert.That(((SoftNums)value!).N).IsEqualTo(5)
            ),
            ["WEB4b"] = new(
                null,
                async value => await Assert.That(((SoftNums)value!).N).IsEqualTo(5)
            ),
            ["WEB4c"] = new(
                null,
                async value => await Assert.That(((SoftNums)value!).N).IsEqualTo(7)
            ),
            ["WEB4d"] = new(
                null,
                async value => await Assert.That(((SoftNums)value!).N).IsEqualTo(-5)
            ),
            ["WEB5a"] = new(
                null,
                async value => await Assert.That(((SoftNums)value!).X).IsEqualTo(100)
            ),
            ["WEB5b"] = new(
                null,
                async value => await Assert.That(((SoftNums)value!).X).IsEqualTo(1.5)
            ),
            ["WEB5c"] = new(
                null,
                async value =>
                    await Assert.That(double.IsNaN(((SoftNums)value!).X ?? double.NaN)).IsTrue()
            ),
            ["WEB5d"] = new(
                null,
                async value =>
                    await Assert.That(((SoftNums)value!).X).IsEqualTo(double.PositiveInfinity)
            ),
            ["WEB5e"] = new(
                null,
                async value =>
                    await Assert.That(((SoftNums)value!).X).IsEqualTo(double.NegativeInfinity)
            ),
            ["WEB6"] = new(null, async value => await Assert.That(value).IsEqualTo(5)),
            ["WEB7"] = new(
                // The list and map parameters are non-nullable in C#, but STJ
                // writes the null defaults of WEB7's partial value as null.
                () => new Nums(5, 7, 1.10m, 2.5, null!, null!, Color.Red),
                null
            ),
            ["NAM1"] = new(
                () => new Stats(1, 2.5, "u"),
                async value => await Assert.That(((Stats)value!).UserName).IsEqualTo("u")
            ),
            ["NAM2"] = new(
                () => new Stats(1, 2.5, "u"),
                async value => await Assert.That(((Stats)value!).UserName).IsEqualTo("u")
            ),
            ["M1"] = new(
                () => new Blob([1, 2, 3], [255, 254], [1, 2]),
                async value =>
                {
                    var blob = (Blob)value!;

                    await Assert.That(blob.Data.SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();

                    await Assert.That(blob.Maybe!.SequenceEqual(new byte[] { 255, 254 })).IsTrue();

                    await Assert.That(blob.Numbers.SequenceEqual(new List<byte> { 1, 2 })).IsTrue();
                }
            ),
            ["M2"] = new(
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
            ["M3"] = new(
                () => new OrdBase(5, 1, 3),
                async value =>
                {
                    var ord = (OrdBase)value!;

                    await Assert.That(ord.First).IsEqualTo(1);

                    await Assert.That(ord.Second).IsEqualTo(5);

                    await Assert.That(ord.Third).IsEqualTo(3);
                }
            ),
            ["M4"] = new(
                () => new InitRec(1) { Note = "n" },
                async value =>
                {
                    var rec = (InitRec)value!;

                    await Assert.That(rec.A).IsEqualTo(1);

                    await Assert.That(rec.Note).IsEqualTo("n");
                }
            ),
            ["M5"] = new(
                () => new SetClass { N = 1 },
                async value =>
                {
                    var set = (SetClass)value!;

                    await Assert.That(set.N).IsEqualTo(1);

                    await Assert.That(set.Count).IsEqualTo(5);
                }
            ),
            ["E8"] = new(() => new UFailX(1), null),
            ["E9"] = new(() => new NLeaf(1, 2, 3), null),
            ["E12"] = new(
                () =>
                {
                    var members = new List<Clip>();
                    var group = new Group("g", members);

                    members.Add(group);

                    return group;
                },
                null
            ),
            ["E14"] = new(() => new Other(3), null),
            ["E19a"] = new(null, null),
            ["D2a"] = new(
                null,
                async value =>
                    await Assert
                        .That(((decimal)value!).ToString(CultureInfo.InvariantCulture))
                        .IsEqualTo("12345678901234567890.123456789")
            ),
            ["D2b"] = new(
                null,
                async value =>
                    await Assert
                        .That(((decimal)value!).ToString(CultureInfo.InvariantCulture))
                        .IsEqualTo("0.3333333333333333333333333333")
            ),
        };

        // Error rows carry no value builder; the entries exist so a manifest
        // id cannot lose its builder silently (User Story 5, scenario 3).
        foreach (var id in ErrorRowIds)
        {
            builders.Add(id, new StjCaseBuilder(null, null));
        }

        return builders;
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

        public JsonError? Error { get; set; }

        public string? Divergence { get; set; }
    }

    private sealed record JsonError(string Class, string Message);
}
