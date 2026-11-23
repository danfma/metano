// Declarative BCL → JavaScript mappings for System.Lazy<T> (#686): the `metano-runtime` `Lazy`
// class. The factory constructors map (the `bool` thread-safety flag is dropped: JavaScript has
// one thread); the constructors that build `T` through its default constructor stay unmapped.

using Metano.Annotations;

[assembly: ExportFromBcl(typeof(Lazy<>), ExportedName = "Lazy", FromPackage = "metano-runtime")]
[assembly: MapProperty(typeof(Lazy<>), nameof(Lazy<int>.Value), JsProperty = "value")]
[assembly: MapProperty(
    typeof(Lazy<>),
    nameof(Lazy<int>.IsValueCreated),
    JsProperty = "isValueCreated"
)]
[assembly: MapMethod(
    typeof(Lazy<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new Lazy<$T0>($0)",
    RuntimeImports = "Lazy",
    WhenParameterTypes = [typeof(Func<>)]
)]
[assembly: MapMethod(
    typeof(Lazy<>),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "new Lazy<$T0>($0)",
    RuntimeImports = "Lazy",
    WhenParameterTypes = [typeof(Func<>), typeof(bool)]
)]
