using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType)]
[JsonDerivedType(typeof(SqB), "sqb")]
public interface IShapeB
{
    double Area { get; }
}
