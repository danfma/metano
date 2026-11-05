using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonDerivedType(typeof(Sq), "sq")]
public interface IShape;
