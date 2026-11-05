using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonSerializable(typeof(Blob))]
[JsonSerializable(typeof(Account))]
[JsonSerializable(typeof(OrdBase))]
[JsonSerializable(typeof(InitRec))]
[JsonSerializable(typeof(SetClass))]
public partial class MembersJsonContext : JsonSerializerContext;
