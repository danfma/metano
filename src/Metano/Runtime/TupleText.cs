using Metano.Annotations;

[assembly: MapMethod(
    typeof(ValueTuple),
    "ToString",
    JsTemplate = "formatTuple($this)",
    RuntimeImports = "formatTuple",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ValueTuple<>),
    "ToString",
    JsTemplate = "formatTuple($this)",
    RuntimeImports = "formatTuple",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ValueTuple<,>),
    "ToString",
    JsTemplate = "formatTuple($this)",
    RuntimeImports = "formatTuple",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ValueTuple<,,>),
    "ToString",
    JsTemplate = "formatTuple($this)",
    RuntimeImports = "formatTuple",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ValueTuple<,,,>),
    "ToString",
    JsTemplate = "formatTuple($this)",
    RuntimeImports = "formatTuple",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ValueTuple<,,,,>),
    "ToString",
    JsTemplate = "formatTuple($this)",
    RuntimeImports = "formatTuple",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ValueTuple<,,,,,>),
    "ToString",
    JsTemplate = "formatTuple($this)",
    RuntimeImports = "formatTuple",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ValueTuple<,,,,,,>),
    "ToString",
    JsTemplate = "formatTuple($this)",
    RuntimeImports = "formatTuple",
    WhenArgCount = 0
)]
[assembly: MapMethod(
    typeof(ValueTuple<,,,,,,,>),
    "ToString",
    JsTemplate = "formatTuple($this)",
    RuntimeImports = "formatTuple",
    WhenArgCount = 0
)]
