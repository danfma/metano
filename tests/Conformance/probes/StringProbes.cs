namespace Conformance;

public class Person
{
    public string? Nick;
    public string Name = "Alice";
    public Person? Friend;
}

public static class StringProbes
{
    static string? Maybe(bool n) => n ? null : "Hello, World";

    static string N(object? o) => o == null ? "null" : o.ToString()!;

    public static string CondSubstring()
    {
        var s = Maybe(false);
        var n = Maybe(true);
        return N(s?.Substring(7))
            + "|"
            + N(n?.Substring(7))
            + "|"
            + N(s?.Substring(0, 5))
            + "|"
            + N(n?.Substring(0, 5));
    }

    public static string CondChain()
    {
        var s = Maybe(false);
        var n = Maybe(true);
        return N(s?.Trim().ToUpper())
            + "|"
            + N(n?.Trim().ToUpper())
            + "|"
            + N(s?.Substring(7).Length)
            + "|"
            + N(n?.Substring(7).Length)
            + "|"
            + N(s?.ToLower().Substring(1, 3).ToUpper());
    }

    public static string CondChainNested()
    {
        var s = Maybe(false);
        var n = Maybe(true);
        return N(s?.Substring(1)?.Length)
            + "|"
            + N(n?.Substring(1)?.Length)
            + "|"
            + N(s?.IndexOf("World"))
            + "|"
            + N(n?.IndexOf("World"))
            + "|"
            + N(s?.Contains("World"))
            + "|"
            + N(n?.Contains("x"));
    }

    public static string CondMember()
    {
        Person? p = new Person();
        Person? q = null;
        return N(p?.Name.Substring(1).Length)
            + "|"
            + N(q?.Name.Substring(1).Length)
            + "|"
            + N(p?.Nick?.Substring(1))
            + "|"
            + N(p?.Friend?.Name.Substring(1))
            + "|"
            + N(p.Name?.Substring(2))
            + "|"
            + N(p?.Name.Replace("A", "a").PadLeft(7, '*'));
    }

    public static string CondCoalesce()
    {
        var n = Maybe(true);
        var s = Maybe(false);
        return (n?.Substring(1) ?? "dflt")
            + "|"
            + (s?.Substring(1) ?? "dflt")
            + "|"
            + (n?.Length ?? -1)
            + "|"
            + (n?.StartsWith("H") ?? false)
            + "|"
            + (s?.StartsWith("H") == true)
            + "|"
            + (n?.Trim().Length > 0);
    }

    public static string CondSplit()
    {
        var s = Maybe(false);
        var n = Maybe(true);
        return N(s?.Split(',').Length)
            + "|"
            + N(n?.Split(',').Length)
            + "|"
            + N(s?.Split(',')[1].Trim())
            + "|"
            + N(s?.ToCharArray().Length)
            + "|"
            + N(s?[0])
            + "|"
            + N(n?[0]);
    }

    public static string CondInArg()
    {
        var s = Maybe(false);
        var n = Maybe(true);
        return "a"
            + s?.Substring(0, 1)
            + n?.Substring(0, 1)
            + "b"
            + $"{s?.Substring(0, 2)}{n?.Substring(0, 2)}";
    }

    public static string SubstringEdge()
    {
        var s = "abc";
        return s.Substring(3)
            + "|"
            + s.Substring(0, 0)
            + "|"
            + s.Substring(1, 2)
            + "|"
            + s.Substring(2);
    }

    public static string SubstringThrows()
    {
        return "abc".Substring(4);
    }

    public static string SubstringThrows2()
    {
        return "abc".Substring(1, 5);
    }

    public static string SubstringThrows3()
    {
        var i = -1;
        return "abc".Substring(i);
    }

    public static string IndexOfs()
    {
        var s = "hello hello";
        return s.IndexOf("l")
            + "|"
            + s.IndexOf("")
            + "|"
            + s.IndexOf('o', 5)
            + "|"
            + s.LastIndexOf("l")
            + "|"
            + s.LastIndexOf('h')
            + "|"
            + s.IndexOf("z")
            + "|"
            + s.IndexOf("lo", 4)
            + "|"
            + s.LastIndexOf("")
            + "|"
            + s.IndexOf("HELLO", StringComparison.OrdinalIgnoreCase);
    }

    public static string Tests()
    {
        var s = "Hello";
        return s.StartsWith("He")
            + "|"
            + s.EndsWith("lo")
            + "|"
            + s.Contains("ell")
            + "|"
            + s.StartsWith("")
            + "|"
            + s.StartsWith("he", StringComparison.OrdinalIgnoreCase)
            + "|"
            + s.Contains('H')
            + "|"
            + s.EndsWith('o')
            + "|"
            + string.IsNullOrEmpty(s)
            + "|"
            + string.IsNullOrWhiteSpace("  ");
    }

    public static string Trims()
    {
        var s = "  xxhixx  ";
        return "["
            + s.Trim()
            + "]["
            + s.TrimStart()
            + "]["
            + s.TrimEnd()
            + "]["
            + s.Trim().Trim('x')
            + "]["
            + s.Trim().TrimStart('x')
            + "]["
            + s.Trim().TrimEnd('x')
            + "]["
            + "abcba".Trim('a', 'b')
            + "]";
    }

    public static string Pads()
    {
        var s = "ab";
        return "["
            + s.PadLeft(5)
            + "]["
            + s.PadRight(5)
            + "]["
            + s.PadLeft(5, '0')
            + "]["
            + s.PadRight(1)
            + "]["
            + s.PadLeft(0)
            + "]";
    }

    public static string Replaces()
    {
        var s = "a.b.c";
        return s.Replace(".", "-")
            + "|"
            + s.Replace('.', '/')
            + "|"
            + s.Replace("b", "")
            + "|"
            + "aaa".Replace("aa", "b")
            + "|"
            + "x$1y".Replace("$1", "$&")
            + "|"
            + "a.b".Replace(".", "$'");
    }

    public static string InsertRemove()
    {
        var s = "abcdef";
        return s.Insert(2, "XY")
            + "|"
            + s.Remove(2)
            + "|"
            + s.Remove(1, 2)
            + "|"
            + s.Insert(6, "!")
            + "|"
            + s.Insert(0, ">");
    }

    public static string Cases()
    {
        var s = "Straße İi";
        return "abc".ToUpper()
            + "|"
            + "ABC".ToLower()
            + "|"
            + "abc".ToUpperInvariant()
            + "|"
            + "ABC".ToLowerInvariant()
            + "|"
            + "ß".ToUpperInvariant()
            + "|"
            + "ß".ToUpper().Length;
    }

    public static string Splits()
    {
        var s = "a,b,,c";
        return s.Split(',').Length
            + "|"
            + s.Split(',', StringSplitOptions.RemoveEmptyEntries).Length
            + "|"
            + s.Split(',', 2)[1]
            + "|"
            + string.Join("+", s.Split(",,"))
            + "|"
            + string.Join("+", "a b  c".Split(' '))
            + "|"
            + string.Join("+", " a , b ".Split(',', StringSplitOptions.TrimEntries))
            + "|"
            + string.Join("+", "a1b2c".Split('1', '2'))
            + "|"
            + "".Split(',').Length;
    }

    public static string Compare()
    {
        return string.Compare("a", "b")
            + "|"
            + string.Compare("b", "a")
            + "|"
            + string.Compare("a", "a")
            + "|"
            + "a".CompareTo("b")
            + "|"
            + string.CompareOrdinal("a", "B")
            + "|"
            + string.Compare("a", "A", StringComparison.OrdinalIgnoreCase)
            + "|"
            + string.Compare("a", "B", StringComparison.Ordinal);
    }

    public static string Chars()
    {
        var s = "héllo";
        return s.Length
            + "|"
            + s[1]
            + "|"
            + (int)s[1]
            + "|"
            + s.ToCharArray().Length
            + "|"
            + new string('x', 3)
            + "|"
            + new string(new[] { 'a', 'b' })
            + "|"
            + s.First()
            + s.Last()
            + "|"
            + string.Concat(s.Reverse());
    }

    public static string Formatting()
    {
        return string.Format("{0}-{1}", 1, "a")
            + "|"
            + string.Format("{0,4}|{1,-4}|", 7, "x")
            + "|"
            + string.Join(", ", new[] { 1, 2, 3 })
            + "|"
            + string.Join("", new List<string> { "a", "b" })
            + "|"
            + string.Concat("a", "b", "c")
            + "|"
            + string.Empty.Length;
    }

    public static string NullChain()
    {
        Person? p = null;
        return (p?.Name.ToUpper().Trim() ?? "none")
            + "|"
            + (p?.Name.Length.ToString() ?? "none")
            + "|"
            + (p?.Name?.Substring(0, 1) ?? "none");
    }

    public static string OnLiteralAndCall()
    {
        return "Hello".Substring(1).Substring(1)
            + "|"
            + Maybe(false)!.Substring(7).ToUpper()
            + "|"
            + ("a" + "b").PadLeft(4, '.')
            + "|"
            + 5.ToString().PadLeft(3, '0');
    }

    public static string Lambdas()
    {
        var xs = new[] { "apple", "Banana", null };
        return string.Join(",", xs.Select(x => x?.Substring(0, 2) ?? "??"))
            + "|"
            + xs.Count(x => x?.StartsWith("a") == true)
            + "|"
            + string.Join(",", xs.Where(x => x != null).Select(x => x!.ToUpper().Substring(1, 2)));
    }

    public static string ContainsIgnoreCase()
    {
        return "Hello".Contains("ELL", StringComparison.OrdinalIgnoreCase)
            + "|"
            + "Hello".Contains("ELL")
            + "|"
            + "Hello".EndsWith("LO", StringComparison.OrdinalIgnoreCase);
    }

    public static string ReplaceIgnoreCase()
    {
        return "Hello".Replace("L", "x", StringComparison.OrdinalIgnoreCase);
    }

    public static string EqualsCalls()
    {
        var s = "Hello";
        return s.Equals("Hello")
            + "|"
            + s.Equals("hello")
            + "|"
            + s.Equals("hello", StringComparison.OrdinalIgnoreCase)
            + "|"
            + string.Equals(s, "HELLO", StringComparison.OrdinalIgnoreCase)
            + "|"
            + string.Equals(s, "Hello")
            + "|"
            + (s == "Hello");
    }

    public static string EmptyAndNullArgs()
    {
        try
        {
            return "abc".Contains((string)null!).ToString();
        }
        catch (ArgumentNullException)
        {
            return "ane";
        }
    }

    public static string LengthAndIndex()
    {
        string s = "abc";
        try
        {
            return s[5].ToString();
        }
        catch (IndexOutOfRangeException)
        {
            return "ioor";
        }
    }

    public static string TrimSpecial()
    {
        return "[" + " \tx \n".Trim() + "][" + "﻿x".Trim().Length + "]";
    }
}
