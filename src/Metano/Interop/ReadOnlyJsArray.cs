using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Metano.Annotations;

namespace Metano.Interop;

/// <summary>
/// A read-only view of a native JavaScript array, for interop signatures
/// (<c>[Import]</c> facades, <c>[JsCallable]</c> callbacks, JSX props) and
/// zero-copy <c>AsReadOnlyJsArray()</c> views (spec 011 FR-004, FR-056a).
/// Implements only <see cref="IEnumerable{T}"/> (not <see cref="IReadOnlyList{T}"/>,
/// mirroring <see cref="ReadOnlySpan{T}"/>), exposing <see cref="Length"/> and a
/// get-only indexer as plain members, and carries a <see cref="JsonConverterAttribute"/>
/// so System.Text.Json reads and writes it as a JSON array (trimmed and
/// NativeAOT apps register the element type with <c>[JsonSerializable(typeof(T))]</c>). TypeScript emits
/// <c>readonly T[]</c>, exposing only non-mutating <c>ReadonlyArray</c> members;
/// <see cref="JsArray{T}"/> and <c>T[]</c> convert to it implicitly and nothing is
/// emitted. On .NET it wraps an <see cref="IReadOnlyList{T}"/> directly (or a
/// <see cref="List{T}"/> copy when constructed from an enumerable). TypeScript
/// only: any other target reports <c>MS0051</c>.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
[TargetSpecific(TargetLanguage.TypeScript)]
[CollectionBuilder(typeof(ReadOnlyJsArray), nameof(ReadOnlyJsArray.Create))]
[JsonConverter(typeof(ReadOnlyJsArrayJsonConverterFactory))]
public sealed class ReadOnlyJsArray<T> : IEnumerable<T>
{
    private readonly IReadOnlyList<T> _items;

    /// <summary>An empty read-only array (<c>[]</c>).</summary>
    public ReadOnlyJsArray() => _items = [];

    /// <summary>A read-only array holding <paramref name="items"/>, copied.</summary>
    public ReadOnlyJsArray(IEnumerable<T> items) => _items = [.. items];

    private ReadOnlyJsArray(IReadOnlyList<T> items, bool _) => _items = items;

    /// <summary><c>length</c>.</summary>
    public int Length => _items.Count;

    /// <summary><c>arr[index]</c>: read-only; no bounds check in JavaScript.</summary>
    public T this[int index] => _items[index];

    /// <summary>The array itself: <c>JsArray&lt;T&gt;</c> → <c>ReadOnlyJsArray&lt;T&gt;</c> emits nothing.</summary>
    public static implicit operator ReadOnlyJsArray<T>(JsArray<T> array) =>
        array is null ? null! : Wrap(array);

    /// <summary>The array itself: <c>T[]</c> → <c>ReadOnlyJsArray&lt;T&gt;</c> emits nothing.</summary>
    public static implicit operator ReadOnlyJsArray<T>(T[] array) =>
        array is null ? null! : Wrap(array);

    /// <summary><c>slice(start, end)</c>: a mutable copy of <c>[start, end)</c>.</summary>
    public JsArray<T> Slice(int start = 0, int? end = null)
    {
        var from = Clamp(start);
        var to = end is { } last ? Clamp(last) : _items.Count;

        return new JsArray<T>(_items.Skip(from).Take(Math.Max(0, to - from)));
    }

    /// <summary><c>indexOf(item)</c>.</summary>
    public int IndexOf(T item)
    {
        if (_items is List<T> list)
            return list.IndexOf(item);

        var comparer = EqualityComparer<T>.Default;

        for (var i = 0; i < _items.Count; i++)
        {
            if (comparer.Equals(_items[i], item))
                return i;
        }

        return -1;
    }

    /// <summary><c>includes(item)</c>.</summary>
    public bool Includes(T item) => IndexOf(item) >= 0;

    /// <summary><c>map(f)</c>.</summary>
    public JsArray<TResult> Map<TResult>(Func<T, TResult> selector) => new(_items.Select(selector));

    /// <summary><c>filter(p)</c>.</summary>
    public JsArray<T> Filter(Func<T, bool> predicate) => new(_items.Where(predicate));

    /// <summary><c>forEach(f)</c>.</summary>
    public void ForEach(Action<T> action)
    {
        foreach (var item in _items)
            action(item);
    }

    /// <summary><c>join(separator)</c>.</summary>
    public string Join(string separator = ",") => string.Join(separator, _items);

    /// <summary><c>at(index)</c>: a negative index counts from the end.</summary>
    public T? At(int index)
    {
        var position = index < 0 ? _items.Count + index : index;

        return position >= 0 && position < _items.Count ? _items[position] : default;
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal static ReadOnlyJsArray<T> Wrap(IReadOnlyList<T> items) => new(items, true);

    private int Clamp(int index) =>
        index < 0 ? Math.Max(0, _items.Count + index) : Math.Min(index, _items.Count);
}

/// <summary>Factories of <see cref="ReadOnlyJsArray{T}"/>.</summary>
[TargetSpecific(TargetLanguage.TypeScript)]
public static class ReadOnlyJsArray
{
    /// <summary><c>Array.from(items)</c>: a new read-only array view copied from any iterable.</summary>
    public static ReadOnlyJsArray<T> From<T>(IEnumerable<T> items) => new(items);

    /// <summary>
    /// The builder of a collection expression (<c>ReadOnlyJsArray&lt;int&gt; a = [1, 2]</c>),
    /// which TypeScript emits as the array literal itself.
    /// </summary>
    public static ReadOnlyJsArray<T> Create<T>(ReadOnlySpan<T> items) => new(items.ToArray());
}

/// <summary>
/// <see cref="JsonConverterFactory"/> that serializes and deserializes
/// <see cref="ReadOnlyJsArray{T}"/> as a JSON array on .NET, matching
/// TypeScript's <c>array</c> JSON descriptor.
/// </summary>
[TargetSpecific(TargetLanguage.TypeScript)]
public sealed class ReadOnlyJsArrayJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(ReadOnlyJsArray<>);

    /// <inheritdoc />
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "Reference-type element instantiations share canonical code and "
            + "resolve under NativeAOT. Value-type element types need "
            + "ReadOnlyJsArrayJsonConverter<T> registered in JsonSerializerOptions.Converters "
            + "or JsonSourceGenerationOptions.Converters (FR-056a); options converters take "
            + "precedence over this factory, so it never runs for them."
    )]
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var elementType = typeToConvert.GetGenericArguments()[0];

        try
        {
            var converterType = typeof(ReadOnlyJsArrayJsonConverter<>).MakeGenericType(elementType);

            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }
        catch (NotSupportedException error)
        {
            throw new NotSupportedException(
                $"ReadOnlyJsArray<{elementType.Name}> needs "
                    + $"ReadOnlyJsArrayJsonConverter<{elementType.Name}> registered in "
                    + "JsonSerializerOptions.Converters or JsonSourceGenerationOptions.Converters "
                    + "under NativeAOT.",
                error
            );
        }
    }
}

/// <summary>
/// The System.Text.Json converter for <see cref="ReadOnlyJsArray{T}"/>, created by
/// <see cref="ReadOnlyJsArrayJsonConverterFactory"/>. NativeAOT apps register it once
/// per value-type element type (for example in
/// <c>[JsonSourceGenerationOptions(Converters = [typeof(ReadOnlyJsArrayJsonConverter&lt;int&gt;)])]</c>),
/// because the factory cannot instantiate it over a value type without dynamic code.
/// </summary>
[TargetSpecific(TargetLanguage.TypeScript)]
public sealed class ReadOnlyJsArrayJsonConverter<T> : JsonConverter<ReadOnlyJsArray<T>>
{
    private static readonly ConditionalWeakTable<
        JsonSerializerOptions,
        JsonSerializerOptions
    > FallbackOptions = new();

    public override ReadOnlyJsArray<T>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException(
                $"Expected start of JSON array for {typeToConvert}, got {reader.TokenType}."
            );
        }

        var elementTypeInfo = ResolveElementTypeInfo(options);
        var items = new List<T>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return ReadOnlyJsArray<T>.Wrap(items);

            items.Add(JsonSerializer.Deserialize(ref reader, elementTypeInfo)!);
        }

        throw new JsonException("Unexpected end of JSON while reading array.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        ReadOnlyJsArray<T> value,
        JsonSerializerOptions options
    )
    {
        var elementTypeInfo = ResolveElementTypeInfo(options);

        writer.WriteStartArray();

        for (var i = 0; i < value.Length; i++)
            JsonSerializer.Serialize(writer, value[i], elementTypeInfo);

        writer.WriteEndArray();
    }

    private static JsonTypeInfo<T> ResolveElementTypeInfo(JsonSerializerOptions options)
    {
        if (options.TryGetTypeInfo(typeof(T), out var typeInfo))
            return (JsonTypeInfo<T>)typeInfo;

        if (!JsonSerializer.IsReflectionEnabledByDefault)
            return (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

        return ResolveThroughReflection(options);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Reached only when JsonSerializer.IsReflectionEnabledByDefault is "
            + "true; trimmed and AOT apps default it to false and take the "
            + "GetTypeInfo path, which reports unregistered element types."
    )]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "Reached only when JsonSerializer.IsReflectionEnabledByDefault is "
            + "true; trimmed and AOT apps default it to false."
    )]
    private static JsonTypeInfo<T> ResolveThroughReflection(JsonSerializerOptions options)
    {
        var fallback = FallbackOptions.GetValue(options, CreateReflectionOptions);

        return (JsonTypeInfo<T>)fallback.GetTypeInfo(typeof(T));
    }

    [RequiresUnreferencedCode("Uses DefaultJsonTypeInfoResolver for unregistered element types.")]
    [RequiresDynamicCode("Uses DefaultJsonTypeInfoResolver for unregistered element types.")]
    private static JsonSerializerOptions CreateReflectionOptions(JsonSerializerOptions source)
    {
        var reflection = new DefaultJsonTypeInfoResolver();

        return new JsonSerializerOptions(source)
        {
            TypeInfoResolver = source.TypeInfoResolver is null
                ? reflection
                : JsonTypeInfoResolver.Combine(source.TypeInfoResolver, reflection),
        };
    }
}
