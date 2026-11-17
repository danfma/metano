// Declarative BCL → JavaScript mappings for the System.Exception hierarchy.
// Read at compile time by the Metano transpiler (BclExportReader, DeclarativeMappingRegistry).
//
// Each [ExportFromBcl] binds a BCL exception type to its `metano-runtime` class (spec 011
// FR-013, FR-014): `new InvalidOperationException("x")` emits
// `new InvalidOperationException("x")` imported from that class's own module, a user
// exception deriving from it extends that class, the type position names it, and `is`,
// `as`, patterns and `catch` test `instanceof` on it. `FromPackage` is the class's module
// specifier, so a program that throws one exception loads (and bundles) that module only.
// A BCL exception without an entry here has no runtime class: a construction reports
// MS0001 and emits the runtime `Exception` carrying the C# type name as its identity
// (`namedException`), and a type test reports MS0045.
//
// The runtime classes extend `Error`, so `Message` lines up by camelCase (`message`); the
// inner exception travels as the ES2022 `cause` option and the stack trace as `stack`.
// The two members below stay mapped onto `cause` / `stack` rather than the runtime's
// `innerException` / `stackTrace`, so they also read a plain `Error` caught from
// JavaScript. Both are optional on the JS side (`cause` is typed `unknown`), so the
// templates narrow them back to the C# contract — an `Error` (or string) or `null`. They
// use conditionals rather than `?? null`, which TypeScript flags as always nullish
// (TS2871) once a caller adds its own `?? fallback`.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Metano.Annotations;

[assembly: ExportFromBcl(
    typeof(Exception),
    ExportedName = "Exception",
    FromPackage = "metano-runtime/system/exceptions/exception"
)]
[assembly: ExportFromBcl(
    typeof(SystemException),
    ExportedName = "SystemException",
    FromPackage = "metano-runtime/system/exceptions/system-exception"
)]
[assembly: ExportFromBcl(
    typeof(InvalidOperationException),
    ExportedName = "InvalidOperationException",
    FromPackage = "metano-runtime/system/exceptions/invalid-operation-exception"
)]
[assembly: ExportFromBcl(
    typeof(ArgumentException),
    ExportedName = "ArgumentException",
    FromPackage = "metano-runtime/system/exceptions/argument-exception"
)]
[assembly: ExportFromBcl(
    typeof(ArgumentNullException),
    ExportedName = "ArgumentNullException",
    FromPackage = "metano-runtime/system/exceptions/argument-null-exception"
)]
[assembly: ExportFromBcl(
    typeof(ArgumentOutOfRangeException),
    ExportedName = "ArgumentOutOfRangeException",
    FromPackage = "metano-runtime/system/exceptions/argument-out-of-range-exception"
)]
[assembly: ExportFromBcl(
    typeof(KeyNotFoundException),
    ExportedName = "KeyNotFoundException",
    FromPackage = "metano-runtime/system/exceptions/key-not-found-exception"
)]
[assembly: ExportFromBcl(
    typeof(IndexOutOfRangeException),
    ExportedName = "IndexOutOfRangeException",
    FromPackage = "metano-runtime/system/exceptions/index-out-of-range-exception"
)]
[assembly: ExportFromBcl(
    typeof(FormatException),
    ExportedName = "FormatException",
    FromPackage = "metano-runtime/system/exceptions/format-exception"
)]
[assembly: ExportFromBcl(
    typeof(ArithmeticException),
    ExportedName = "ArithmeticException",
    FromPackage = "metano-runtime/system/exceptions/arithmetic-exception"
)]
[assembly: ExportFromBcl(
    typeof(OverflowException),
    ExportedName = "OverflowException",
    FromPackage = "metano-runtime/system/exceptions/overflow-exception"
)]
[assembly: ExportFromBcl(
    typeof(DivideByZeroException),
    ExportedName = "DivideByZeroException",
    FromPackage = "metano-runtime/system/exceptions/divide-by-zero-exception"
)]
[assembly: ExportFromBcl(
    typeof(NotSupportedException),
    ExportedName = "NotSupportedException",
    FromPackage = "metano-runtime/system/exceptions/not-supported-exception"
)]
[assembly: ExportFromBcl(
    typeof(NotImplementedException),
    ExportedName = "NotImplementedException",
    FromPackage = "metano-runtime/system/exceptions/not-implemented-exception"
)]
[assembly: ExportFromBcl(
    typeof(InvalidCastException),
    ExportedName = "InvalidCastException",
    FromPackage = "metano-runtime/system/exceptions/invalid-cast-exception"
)]
[assembly: ExportFromBcl(
    typeof(NullReferenceException),
    ExportedName = "NullReferenceException",
    FromPackage = "metano-runtime/system/exceptions/null-reference-exception"
)]
[assembly: ExportFromBcl(
    typeof(MissingMethodException),
    ExportedName = "MissingMethodException",
    FromPackage = "metano-runtime/system/exceptions/missing-method-exception"
)]
[assembly: ExportFromBcl(
    typeof(ObjectDisposedException),
    ExportedName = "ObjectDisposedException",
    FromPackage = "metano-runtime/system/exceptions/object-disposed-exception"
)]
[assembly: ExportFromBcl(
    typeof(OperationCanceledException),
    ExportedName = "OperationCanceledException",
    FromPackage = "metano-runtime/system/exceptions/operation-canceled-exception"
)]
[assembly: ExportFromBcl(
    typeof(TaskCanceledException),
    ExportedName = "TaskCanceledException",
    FromPackage = "metano-runtime/system/exceptions/task-canceled-exception"
)]
[assembly: ExportFromBcl(
    typeof(JsonException),
    ExportedName = "JsonException",
    FromPackage = "metano-runtime/system/exceptions/json-exception"
)]
[assembly: ExportFromBcl(
    typeof(TimeoutException),
    ExportedName = "TimeoutException",
    FromPackage = "metano-runtime/system/exceptions/timeout-exception"
)]
[assembly: ExportFromBcl(
    typeof(SwitchExpressionException),
    ExportedName = "SwitchExpressionException",
    FromPackage = "metano-runtime/system/exceptions/switch-expression-exception"
)]
[assembly: ExportFromBcl(
    typeof(TypeInitializationException),
    ExportedName = "TypeInitializationException",
    FromPackage = "metano-runtime/system/exceptions/type-initialization-exception"
)]

// The argument guards (`ThrowIfNull`, `ThrowIfNullOrEmpty`, the `ThrowIf*` comparisons) lower
// to the runtime's `argument-guards` functions; the extractor passes the
// `[CallerArgumentExpression]` text C# supplies as their last argument. An exported
// exception type binds its class, not its static members, so any other static member of
// these types (`ObjectDisposedException.ThrowIf`, which needs .NET's type names) reports
// MS0001.
[assembly: MapMethod(
    typeof(ArgumentNullException),
    nameof(ArgumentNullException.ThrowIfNull),
    WhenParameterTypes = [typeof(object), typeof(string)],
    JsTemplate = "throwIfNull($0, $1)",
    RuntimeImports = "throwIfNull"
)]
[assembly: MapMethod(
    typeof(ArgumentException),
    nameof(ArgumentException.ThrowIfNullOrEmpty),
    JsTemplate = "throwIfNullOrEmpty($0, $1)",
    RuntimeImports = "throwIfNullOrEmpty"
)]
[assembly: MapMethod(
    typeof(ArgumentException),
    nameof(ArgumentException.ThrowIfNullOrWhiteSpace),
    JsTemplate = "throwIfNullOrWhiteSpace($0, $1)",
    RuntimeImports = "throwIfNullOrWhiteSpace"
)]
[assembly: MapMethod(
    typeof(ArgumentOutOfRangeException),
    nameof(ArgumentOutOfRangeException.ThrowIfZero),
    JsTemplate = "throwIfZero($0, $1)",
    RuntimeImports = "throwIfZero"
)]
[assembly: MapMethod(
    typeof(ArgumentOutOfRangeException),
    nameof(ArgumentOutOfRangeException.ThrowIfNegative),
    JsTemplate = "throwIfNegative($0, $1)",
    RuntimeImports = "throwIfNegative"
)]
[assembly: MapMethod(
    typeof(ArgumentOutOfRangeException),
    nameof(ArgumentOutOfRangeException.ThrowIfNegativeOrZero),
    JsTemplate = "throwIfNegativeOrZero($0, $1)",
    RuntimeImports = "throwIfNegativeOrZero"
)]
[assembly: MapMethod(
    typeof(ArgumentOutOfRangeException),
    nameof(ArgumentOutOfRangeException.ThrowIfEqual),
    JsTemplate = "throwIfEqual($0, $1, $2)",
    RuntimeImports = "throwIfEqual"
)]
[assembly: MapMethod(
    typeof(ArgumentOutOfRangeException),
    nameof(ArgumentOutOfRangeException.ThrowIfNotEqual),
    JsTemplate = "throwIfNotEqual($0, $1, $2)",
    RuntimeImports = "throwIfNotEqual"
)]
[assembly: MapMethod(
    typeof(ArgumentOutOfRangeException),
    nameof(ArgumentOutOfRangeException.ThrowIfGreaterThan),
    JsTemplate = "throwIfGreaterThan($0, $1, $2)",
    RuntimeImports = "throwIfGreaterThan"
)]
[assembly: MapMethod(
    typeof(ArgumentOutOfRangeException),
    nameof(ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual),
    JsTemplate = "throwIfGreaterThanOrEqual($0, $1, $2)",
    RuntimeImports = "throwIfGreaterThanOrEqual"
)]
[assembly: MapMethod(
    typeof(ArgumentOutOfRangeException),
    nameof(ArgumentOutOfRangeException.ThrowIfLessThan),
    JsTemplate = "throwIfLessThan($0, $1, $2)",
    RuntimeImports = "throwIfLessThan"
)]
[assembly: MapMethod(
    typeof(ArgumentOutOfRangeException),
    nameof(ArgumentOutOfRangeException.ThrowIfLessThanOrEqual),
    JsTemplate = "throwIfLessThanOrEqual($0, $1, $2)",
    RuntimeImports = "throwIfLessThanOrEqual"
)]
[assembly: MapProperty(
    typeof(Exception),
    nameof(Exception.InnerException),
    JsTemplate = "($this.cause instanceof Error ? $this.cause : null)"
)]
[assembly: MapProperty(
    typeof(Exception),
    nameof(Exception.StackTrace),
    JsTemplate = "($this.stack !== undefined ? $this.stack : null)"
)]
