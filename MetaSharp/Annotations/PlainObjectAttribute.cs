namespace MetaSharp.Annotations;

/// <summary>
/// Marks a record (or class) as a "plain object" / DTO type. The transpiler emits it
/// as a TypeScript <c>interface</c> with no class wrapper, no methods on the prototype,
/// and no equality helpers — what you see is the wire shape.
///
/// <para>Usage: data transfer types that flow through HTTP boundaries, message queues,
/// or anywhere <c>JSON.stringify</c> / <c>JSON.parse</c> is the de-facto serializer.
/// Without this attribute, a C# <c>record</c> becomes a TS <c>class</c>, which means
/// objects parsed from JSON aren't real instances of the type — they're plain objects
/// missing the <c>equals</c>/<c>hashCode</c>/<c>with</c> methods. Calling those methods
/// on a deserialized DTO is a silent runtime failure.</para>
///
/// <code>
/// [PlainObject]
/// public record CreateTodoDto(string Title, Priority Priority);
///
/// // C#:                                          // Transpiled TS:
/// var dto = new CreateTodoDto("Buy milk",         // const dto: CreateTodoDto = {
///     Priority.High);                             //   title: "Buy milk",
///                                                 //   priority: "high",
///                                                 // };
///
/// var bumped = dto with { Priority = Priority.Low };  // const bumped = { ...dto, priority: "low" };
/// </code>
///
/// <para>Behavior changes when the attribute is present:</para>
/// <list type="bullet">
///   <item>Type is emitted as <c>export interface T { … }</c> instead of a class</item>
///   <item><c>new T(args)</c> lowers to an object literal, with positional constructor
///   arguments matched to the property names declared on the record's primary
///   constructor</item>
///   <item><c>record with { X = expr }</c> lowers to <c>{ ...source, x: expr }</c>
///   instead of <c>source.with({ x: expr })</c></item>
///   <item>Imports of the type use <c>import type</c> (the type is erased at runtime)</item>
/// </list>
///
/// <para>Methods, inheritance, and complex property types are NOT blocked at this
/// stage — the user is trusted to know that anything beyond plain data won't survive
/// JSON round-tripping. A future revision may add opt-in helpers (e.g., methods that
/// take <c>self</c> as the first parameter and lower to standalone functions).</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class PlainObjectAttribute : Attribute { }
