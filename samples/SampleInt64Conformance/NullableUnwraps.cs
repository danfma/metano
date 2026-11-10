using System;
using System.Threading.Tasks;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The <c>Nullable&lt;T&gt;</c> unwrap rows (#629): a <c>Value</c> read and
/// an unwrap cast throw <c>InvalidOperationException</c> on a missing value
/// wherever nullable analysis calls the value present — after an
/// <c>await</c>, after <c>!</c>, and in nullable-disabled code. Each row
/// reports the throw as <c>null</c>, because the targets raise their own
/// error type for it, and adds to the value as a <c>double</c>, so an
/// unguarded read shows as a number (<c>null + 1</c> is <c>1</c> in
/// JavaScript) instead of failing some other way.
/// </summary>
[NoContainer]
public static class NullableUnwraps
{
#pragma warning disable CS8629 // the rows read a nullable value that may be missing
    public static async Task<double?> AwaitedValue(int? n)
    {
        try
        {
            int? read = await Task.FromResult(n);

            return read.Value + 1.0;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static async Task<double?> AwaitedCast(long? nl)
    {
        try
        {
            long? read = await Task.FromResult(nl);

            return (double)read;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static double? SuppressedValue(int? n)
    {
        try
        {
            return n!.Value + 1.0;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
#pragma warning restore CS8629

#nullable disable
    public static double? DisabledContextValue(int? n)
    {
        try
        {
            return n.Value + 1.0;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
#nullable restore
}
