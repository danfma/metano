namespace Conformance;

public static class ProbeUtil
{
    public static string Kind(Exception e) =>
        e is ArgumentNullException ? "ArgumentNullException"
        : e is ArgumentOutOfRangeException ? "ArgumentOutOfRangeException"
        : e is ArgumentException ? "ArgumentException"
        : e is FormatException ? "FormatException"
        : e is OverflowException ? "OverflowException"
        : e is DivideByZeroException ? "DivideByZeroException"
        : e is InvalidCastException ? "InvalidCastException"
        : e is NullReferenceException ? "NullReferenceException"
        : e is KeyNotFoundException ? "KeyNotFoundException"
        : e is IndexOutOfRangeException ? "IndexOutOfRangeException"
        : e is NotSupportedException ? "NotSupportedException"
        : e is InvalidOperationException ? "InvalidOperationException"
        : "Exception";
}
