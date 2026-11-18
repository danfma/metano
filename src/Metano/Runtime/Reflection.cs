// Declarative BCL → JavaScript mappings for System.Type (spec 011 FR-025).
//
// A `Type` lowers to a `metano-runtime` RuntimeType descriptor, a data literal with
// `name`, `fullName`, `namespace` and `kind`. The supported surface is the type's
// identity: names, interface / class kind, the instance and assignability tests,
// equality and ToString. Every other member (GetMethod, GetProperties, …) reports
// MS0049, since descriptors carry no member metadata. `Name` is declared on
// MemberInfo, so it is mapped there. The kind tests widen `kind` to a string: a
// `typeof` operand is typed as its own descriptor kind (`InterfaceType`), whose
// literal kind TypeScript would otherwise reject comparing to another.

using System.Reflection;
using Metano.Annotations;

[assembly: MapProperty(typeof(MemberInfo), nameof(MemberInfo.Name), JsProperty = "name")]
[assembly: MapProperty(typeof(Type), nameof(Type.Name), JsProperty = "name")]
[assembly: MapProperty(typeof(Type), nameof(Type.FullName), JsProperty = "fullName")]
[assembly: MapProperty(typeof(Type), nameof(Type.Namespace), JsProperty = "namespace")]
[assembly: MapProperty(
    typeof(Type),
    nameof(Type.IsInterface),
    JsTemplate = "(($this.kind as string) === \"interface\")"
)]
[assembly: MapProperty(
    typeof(Type),
    nameof(Type.IsClass),
    JsTemplate = "(($this.kind as string) === \"class\")"
)]
[assembly: MapMethod(
    typeof(Type),
    nameof(Type.IsInstanceOfType),
    JsTemplate = "isType($0, $this)",
    RuntimeImports = "isType"
)]
[assembly: MapMethod(
    typeof(Type),
    nameof(Type.IsAssignableFrom),
    JsTemplate = "isAssignableFrom($this, $0)",
    RuntimeImports = "isAssignableFrom"
)]
[assembly: MapMethod(typeof(Type), nameof(Type.Equals), JsTemplate = "($this === $0)")]
[assembly: MapMethod(typeof(Type), nameof(Type.ToString), JsTemplate = "$this.fullName")]
