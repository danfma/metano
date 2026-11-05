using System.Text.Json;
using System.Text.Json.Serialization;
using SampleJsonContracts;

namespace Metano.Tests.Json;

// The System.Text.Json source-generated fixtures of the shared conformance
// manifest (spec 009, contracts/wire-format.md). Each context mirrors the
// corresponding sample context member for member, except `Title` and
// `EpisodeRec` (FR-026): System.Text.Json writes a branded record struct as
// an object unless it gets a custom converter, so those rows are
// `sides: ["ts"]` and no branded member is declared here.

[JsonSerializable(typeof(Clip))]
[JsonSerializable(typeof(Timeline))]
[JsonSerializable(typeof(List<Clip>))]
[JsonSerializable(typeof(Dictionary<string, Clip>))]
[JsonSerializable(typeof(Group))]
[JsonSerializable(typeof(HBase))]
[JsonSerializable(typeof(IBase))]
[JsonSerializable(typeof(DBase))]
[JsonSerializable(typeof(SBase))]
[JsonSerializable(typeof(UFail))]
[JsonSerializable(typeof(UNear))]
[JsonSerializable(typeof(UBaseH))]
[JsonSerializable(typeof(NBase))]
[JsonSerializable(typeof(NMid))]
[JsonSerializable(typeof(OBase))]
[JsonSerializable(typeof(GBase<int>))]
[JsonSerializable(typeof(Feed))]
[JsonSerializable(typeof(IShape))]
[JsonSerializable(typeof(IShapeB))]
[JsonSerializable(typeof(Shape))]
[JsonSerializable(typeof(MixedBase))]
[JsonSerializable(typeof(EpisodeSummary))]
[JsonSerializable(typeof(IReadOnlyList<EpisodeSummary>))]
[JsonSerializable(typeof(EpisodeSummary[]))]
[JsonSerializable(typeof(Dictionary<string, EpisodeSummary>))]
[JsonSerializable(typeof(HashSet<string>))]
[JsonSerializable(typeof(IntMapHolder))]
[JsonSerializable(typeof(List<List<int>>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateOnly))]
[JsonSerializable(typeof(TimeOnly))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(Color))]
[JsonSerializable(typeof(Level))]
[JsonSerializable(typeof(Flavor))]
[JsonSerializable(typeof(SPoint))]
public partial class StjStudioJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Clip))]
[JsonSerializable(typeof(ImageClip))]
[JsonSerializable(typeof(IBase))]
[JsonSerializable(typeof(Nums))]
[JsonSerializable(typeof(SoftNums))]
[JsonSerializable(typeof(int))]
public partial class StjWebJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(Clip))]
public partial class StjOutOfOrderJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(IgnBase))]
[JsonSerializable(typeof(IgnAbs))]
public partial class StjIgnoreJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(Stats))]
public partial class StjSnakeCaseJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseLower)]
[JsonSerializable(typeof(Stats))]
public partial class StjKebabCaseJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(Blob))]
[JsonSerializable(typeof(Account))]
[JsonSerializable(typeof(OrdBase))]
[JsonSerializable(typeof(InitRec))]
[JsonSerializable(typeof(SetClass))]
public partial class StjMembersJsonContext : JsonSerializerContext;
