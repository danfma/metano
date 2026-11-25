using System.Text.Json;
using System.Text.Json.Serialization;
using Metano.Annotations;

[assembly: MapMethod(
    typeof(Conformance.ClosedFamilyUnionProbes),
    "WireContext",
    JsTemplate = "$T0.default"
)]

namespace Conformance
{
    /// <summary>
    /// A polymorphic closed family (spec 010 FR-C5): its TypeScript union shape and the
    /// <c>kind</c> discriminant leave ToString, equality, hash, <c>with</c> and JSON as
    /// they are for the abstract family.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(CfPlay), "play")]
    [JsonDerivedType(typeof(CfSeek), "seek")]
    [JsonDerivedType(typeof(CfStop), "stop")]
    public closed record CfCommand
    {
        public CfCommand Self() => this;
    }

    public sealed record CfPlay(string Clip) : CfCommand;

    public sealed record CfSeek(int Position) : CfCommand;

    public sealed record CfStop : CfCommand;

    public sealed record CfEnvelope(CfCommand Command, int Id);

    [JsonSerializable(typeof(CfEnvelope))]
    public partial class CfWireContext : JsonSerializerContext;

    public static class ClosedFamilyUnionProbes
    {
        private static T WireContext<T>()
            where T : JsonSerializerContext => (T)(object)CfWireContext.Default;

        public static string ClosedFamilyToString() =>
            new CfPlay("intro") + "|" + new CfSeek(12) + "|" + new CfStop();

        public static string ClosedFamilyEquality()
        {
            CfCommand play = new CfPlay("intro");

            return play.Equals(new CfPlay("intro"))
                + "|"
                + play.Equals(new CfPlay("outro"))
                + "|"
                + (play.GetHashCode() == new CfPlay("intro").GetHashCode())
                + "|"
                + new CfStop().Equals(new CfStop())
                + "|"
                + play.Self().Equals(play);
        }

        public static string ClosedFamilyWith()
        {
            var seek = new CfSeek(3);
            var moved = seek with { Position = 9 };

            return moved + "|" + seek + "|" + (moved == new CfSeek(9));
        }

        public static string ClosedFamilyJson()
        {
            var context = WireContext<CfWireContext>();
            var envelope = new CfEnvelope(new CfSeek(12), 7);
            var json = JsonSerializer.Serialize(envelope, context.CfEnvelope);
            var back = JsonSerializer.Deserialize(
                """{"Command":{"type":"play","Clip":"intro"},"Id":2}""",
                context.CfEnvelope
            )!;

            return json + "|" + back + "|" + back.Command.Equals(new CfPlay("intro"));
        }
    }
}
