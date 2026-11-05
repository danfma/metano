using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ImageClip), "image")]
[JsonDerivedType(typeof(SilenceClip), "silence")]
[JsonDerivedType(typeof(Group), "group")]
public abstract record Clip(double Seconds);
