using System.Collections.Immutable;

namespace Lib;

/// <summary>Compiled independently, without immutable runtime type questions: R4's erased factory path.</summary>
public static class UnslottedImmutableFactories
{
    public static object IntList() => ImmutableList.CreateRange(new int[0]);

    public static object StringList() => ImmutableList.CreateRange(new string[0]);

    public static object IntSet() => ImmutableHashSet.CreateRange(new int[0]);

    public static object StringSet() => ImmutableHashSet.CreateRange(new string[0]);

    public static object IntDictionary() => ImmutableDictionary.Create<int, int>(null);

    public static object StringDictionary() => ImmutableDictionary.Create<string, int>(null);
}
