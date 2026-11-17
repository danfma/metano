namespace Conformance;

public class AppError : Exception
{
    public AppError(string m, Exception? inner = null)
        : base(m, inner) { }
}

public class SubAppError : AppError
{
    public SubAppError(string m)
        : base(m) { }
}

public static class CatchProbes
{
    static string? Null() => null;

    static void Thrower(int k)
    {
        switch (k)
        {
            case 0:
                throw new InvalidOperationException("ioe");
            case 1:
                throw new ArgumentException("arg");
            case 2:
                throw new AppError("app");
            case 3:
                throw new SubAppError("sub");
            case 4:
            {
                string? s = Null();
                var n = s!.Length;
                break;
            }
            case 5:
                throw new ArgumentNullException("p");
            case 6:
                throw new Exception("plain");
            case 7:
            {
                var a = new int[1];
                var l = new List<int>();
                var x = l[3];
                break;
            }
            case 8:
                throw new AppError("outer", new InvalidOperationException("inner"));
            case 9:
                throw new KeyNotFoundException("k");
            case 10:
            {
                var d = new Dictionary<string, int>();
                var x = d["missing"];
                break;
            }
            case 11:
                throw new NotSupportedException("ns");
            case 12:
                throw new FormatException("fmt");
            case 13:
                throw new OperationCanceledException("oc");
        }
    }

    static string Chain(int k)
    {
        try
        {
            Thrower(k);
            return "none";
        }
        catch (SubAppError e)
        {
            return "sub:" + e.Message;
        }
        catch (AppError e) when (e.InnerException != null)
        {
            return "appinner:" + e.InnerException.Message;
        }
        catch (AppError e)
        {
            return "app:" + e.Message;
        }
        catch (ArgumentNullException e)
        {
            return "argnull:" + e.ParamName;
        }
        catch (ArgumentException e)
        {
            return "arg:" + e.Message;
        }
        catch (InvalidOperationException e)
        {
            return "ioe:" + e.Message;
        }
        catch (NullReferenceException)
        {
            return "nre";
        }
        catch (KeyNotFoundException)
        {
            return "knf";
        }
        catch (NotSupportedException e)
        {
            return "ns:" + e.Message;
        }
        catch (FormatException e)
        {
            return "fmt:" + e.Message;
        }
        catch (OperationCanceledException e)
        {
            return "oc:" + e.Message;
        }
        catch (Exception e)
        {
            return "ex:" + e.Message;
        }
    }

    public static string All()
    {
        var r = "";
        for (var k = 0; k <= 13; k++)
            r += Chain(k) + ";";
        return r;
    }

    public static string CatchAllMessage()
    {
        try
        {
            Thrower(6);
            return "none";
        }
        catch (Exception e)
        {
            return e.Message
                + "|"
                + (e is Exception ? "T" : "F")
                + "|"
                + (e.InnerException == null ? "noinner" : "inner");
        }
    }

    public static string Bare()
    {
        try
        {
            Thrower(0);
            return "none";
        }
        catch
        {
            return "bare";
        }
    }

    public static string NreMessage()
    {
        try
        {
            Thrower(4);
            return "none";
        }
        catch (NullReferenceException e)
        {
            return "nre:" + (e is Exception ? "T" : "F") + (e.Message.Length > 0 ? "msg" : "nomsg");
        }
    }

    public static string NreAsException()
    {
        try
        {
            Thrower(4);
            return "none";
        }
        catch (Exception e)
        {
            return "x|"
                + (e is NullReferenceException ? "T" : "F")
                + (e is InvalidOperationException ? "T" : "F");
        }
    }

    public static string Rethrow()
    {
        try
        {
            try
            {
                Thrower(0);
            }
            catch (Exception)
            {
                throw;
            }
            return "none";
        }
        catch (InvalidOperationException e)
        {
            return "re:" + e.Message;
        }
    }

    public static string RethrowVar()
    {
        try
        {
            try
            {
                Thrower(2);
            }
            catch (Exception e)
            {
                if (e.Message == "app")
                    throw;
                return "inner";
            }
            return "none";
        }
        catch (AppError e)
        {
            return "re:" + e.Message;
        }
    }

    public static string RethrowIdentity()
    {
        Exception? first = null;
        try
        {
            try
            {
                Thrower(0);
            }
            catch (Exception e)
            {
                first = e;
                throw;
            }
        }
        catch (Exception e2)
        {
            return ReferenceEquals(first, e2) ? "same" : "different";
        }
        return "none";
    }

    public static string ThrowE()
    {
        try
        {
            try
            {
                Thrower(0);
            }
            catch (Exception e)
            {
                throw e;
            }
            return "none";
        }
        catch (InvalidOperationException e)
        {
            return "te:" + e.Message;
        }
    }

    public static string Wrap()
    {
        try
        {
            try
            {
                Thrower(0);
            }
            catch (Exception e)
            {
                throw new AppError("wrapped", e);
            }
            return "none";
        }
        catch (AppError e)
        {
            return e.Message
                + ">"
                + e.InnerException!.Message
                + ">"
                + (e.InnerException is InvalidOperationException ? "IOE" : "other");
        }
    }

    public static string WhenFilter()
    {
        var r = "";
        for (var k = 0; k < 3; k++)
        {
            try
            {
                Thrower(k);
            }
            catch (Exception e) when (e.Message.StartsWith("a"))
            {
                r += "a:" + e.Message + ";";
            }
            catch (Exception e)
            {
                r += "other:" + e.Message + ";";
            }
        }
        return r;
    }

    public static string WhenNoVar()
    {
        var flag = true;
        try
        {
            Thrower(0);
            return "none";
        }
        catch (InvalidOperationException) when (flag)
        {
            return "flag";
        }
    }

    public static string Unmatched()
    {
        try
        {
            try
            {
                Thrower(1);
                return "none";
            }
            catch (InvalidOperationException)
            {
                return "wrong";
            }
        }
        catch (ArgumentException e)
        {
            return "outer:" + e.Message;
        }
    }

    public static string UnmatchedNre()
    {
        try
        {
            try
            {
                Thrower(4);
                return "none";
            }
            catch (InvalidOperationException)
            {
                return "wrong";
            }
        }
        catch (NullReferenceException)
        {
            return "outer-nre";
        }
    }

    public static string Finally()
    {
        var r = "";
        try
        {
            try
            {
                Thrower(0);
            }
            finally
            {
                r += "f";
            }
        }
        catch (Exception e)
        {
            r += "c" + e.Message;
        }
        return r;
    }

    public static string TypeName()
    {
        try
        {
            Thrower(3);
            return "none";
        }
        catch (Exception e)
        {
            return "x|" + (e is SubAppError ? "S" : "n") + (e is AppError ? "T" : "F");
        }
    }

    public static string IndexOut()
    {
        try
        {
            Thrower(7);
            return "none";
        }
        catch (ArgumentOutOfRangeException)
        {
            return "aoor";
        }
        catch (Exception e)
        {
            return "ex:"
                + (
                    e is InvalidOperationException ? "ioe"
                    : e is NullReferenceException ? "nre"
                    : "other"
                );
        }
    }

    public static string DictMissing()
    {
        try
        {
            Thrower(10);
            return "none";
        }
        catch (KeyNotFoundException)
        {
            return "knf";
        }
        catch (Exception e)
        {
            return "ex:"
                + (
                    e is InvalidOperationException ? "ioe"
                    : e is NullReferenceException ? "nre"
                    : "other"
                );
        }
    }

    public static string StackOrToString()
    {
        try
        {
            Thrower(6);
            return "none";
        }
        catch (Exception e)
        {
            return e.ToString().StartsWith("System.Exception: plain")
                ? "ok"
                : "differs:" + e.ToString().Split('\n')[0];
        }
    }

    public static string CatchInLambda()
    {
        Func<int, string> f = k =>
        {
            try
            {
                Thrower(k);
                return "none";
            }
            catch (AppError e)
            {
                return "app:" + e.Message;
            }
            catch (Exception e)
            {
                return "ex:" + e.Message;
            }
        };
        return f(2) + f(0);
    }

    public static string NestedSameVar()
    {
        try
        {
            Thrower(0);
            return "none";
        }
        catch (Exception e)
        {
            try
            {
                Thrower(1);
                return "none2";
            }
            catch (Exception e2)
            {
                return e.Message + e2.Message;
            }
        }
    }

    public static string OperationCanceled()
    {
        try
        {
            Thrower(13);
            return "none";
        }
        catch (OperationCanceledException e)
        {
            return "oc:" + e.Message;
        }
    }

    public static string ExceptionProps()
    {
        try
        {
            Thrower(0);
            return "none";
        }
        catch (Exception e)
        {
            var m = e.Message;
            Exception same = e;
            return m + (same == e ? "T" : "F") + (e.InnerException?.Message ?? "noinner");
        }
    }
}
