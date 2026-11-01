namespace Metano.Tests;

/// <summary>
/// Calling a delegate-typed member through an explicit receiver
/// (<c>r.F()</c>, <c>r?.F()</c>, <c>Type.F()</c>) invokes the member's
/// value, so the emitted call keeps the member access (<c>r.f()</c>),
/// including a member that is itself named <c>Invoke</c>. Only the
/// delegate's own <c>.Invoke</c> is dropped (#337).
/// </summary>
public class DelegateMemberInvocationTests
{
    private const string Source = """
        using System;

        [Transpile]
        public delegate int Doubler(int x);

        [Transpile]
        public delegate void Listener([This] Holder self, int x);

        [Transpile]
        public record Holder(int X)
        {
            public Func<int> Get { get; } = () => X;
            public Func<int, int> AddField = y => X + y;
            public static Func<int> StaticGet = () => 42;
            public Action<int> Log { get; } = _ => { };
            public Doubler Twice { get; } = x => x * 2;

            public Func<int> Invoke { get; } = () => X;
            public Listener OnPing { get; } = (self, x) => { };

            [Name("renamedGet")]
            public Func<int> Other { get; } = () => X;
        }

        [Transpile]
        public static class Calls
        {
            public static int Run(Holder? maybe)
            {
                var holder = new Holder(1);
                var a = holder.Get();
                var b = holder.AddField(2);
                var c = Holder.StaticGet();
                holder.Log(5);
                var d = holder.Twice(4);
                var e = maybe?.Get() ?? -1;
                var f = holder.Get.Invoke();
                var g = holder.Other();
                var h = maybe?.Get.Invoke() ?? -1;
                var i = holder.Invoke();
                holder.OnPing(holder, 3);
                return a + b + c + d + e + f + g + h + i;
            }
        }
        """;

    [Test]
    public async Task DelegateMemberCalls_MatchTheGoldenOutput()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert
            .That(result["calls.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("delegate-member-invocation.ts"));
    }

    /// <summary>
    /// <c>[Name]</c> is left out: the Dart target does not apply member
    /// renames at read sites yet, with or without an invocation.
    /// </summary>
    [Test]
    public async Task DelegateMemberCalls_DartTarget_KeepTheMemberAccess()
    {
        var (files, _) = DartTranspileHelper.Transpile(Source);

        var dart = files["calls.dart"];

        await Assert.That(dart).Contains("holder.get()");
        await Assert.That(dart).Contains("holder.addField(2)");
        await Assert.That(dart).Contains("Holder.staticGet()");
        await Assert.That(dart).Contains("holder.log(5)");
        await Assert.That(dart).Contains("holder.twice(4)");
        await Assert.That(dart).Contains("maybe?.get()");
        await Assert.That(dart).Contains("holder.invoke()");
        await Assert.That(dart).DoesNotContain("holder()");
        await Assert.That(dart).DoesNotContain("maybe?.call()");
    }
}
