namespace Metano.Tests;

/// <summary>
/// An explicit <c>.Invoke</c> on a delegate whose first parameter carries
/// <c>[This]</c> lowers like the implicit call: the <c>.Invoke</c> segment is
/// dropped and the receiver is rebound through <c>.call</c> (#362).
/// </summary>
public class ThisDelegateExplicitInvokeTests
{
    private const string Source = """
        using System;

        [Transpile]
        public delegate void Listener([This] Holder self, int x);

        [Transpile]
        public record Holder(int X)
        {
            public Listener OnPing { get; } = (self, x) => { };
            public Listener? MaybePing { get; }
            public Listener Invoke { get; } = (self, x) => { };
        }

        [Transpile]
        public static class Calls
        {
            public static void Run(Holder h, Listener listener, Listener? maybe)
            {
                h.OnPing(h, 20);
                h.OnPing.Invoke(h, 20);
                h.OnPing.Invoke(self: h, x: 21);
                h.OnPing.Invoke(x: 28, self: h);
                h.MaybePing?.Invoke(h, 22);
                listener.Invoke(h, 23);
                maybe?.Invoke(h, 24);
                listener(h, 25);
                h.Invoke(h, 26);
                (maybe ?? listener).Invoke(h, 27);
            }
        }
        """;

    [Test]
    public async Task ExplicitInvoke_OnThisDelegate_DropsTheInvokeSegment()
    {
        var lines = EmittedLines(TranspileHelper.Transpile(Source)["calls.ts"]);

        await Assert.That(lines).Contains("h.onPing.call(h, 20);");
        await Assert.That(lines).Contains("h.onPing.call(h, 21);");
        await Assert.That(lines).Contains("h.onPing.call(h, 28);");
        await Assert.That(lines).Contains("listener.call(h, 23);");
        await Assert.That(lines).Contains("listener.call(h, 25);");
        await Assert.That(lines.Any(line => line.Contains("onPing.invoke"))).IsFalse();
        await Assert.That(lines.Any(line => line.Contains("listener.invoke"))).IsFalse();
    }

    [Test]
    public async Task ExplicitInvoke_OnThisDelegate_MatchesTheImplicitForm()
    {
        var lines = EmittedLines(TranspileHelper.Transpile(Source)["calls.ts"]);

        await Assert.That(lines.Count(line => line == "h.onPing.call(h, 20);")).IsEqualTo(2);
    }

    [Test]
    public async Task NullConditionalInvoke_OnThisDelegate_KeepsTheOptionalCall()
    {
        var lines = EmittedLines(TranspileHelper.Transpile(Source)["calls.ts"]);

        await Assert.That(lines).Contains("h.maybePing?.call(h, 22);");
        await Assert.That(lines).Contains("maybe?.call(h, 24);");
    }

    /// <summary>
    /// A member that is itself named <c>Invoke</c> and holds a <c>[This]</c>
    /// delegate keeps its member access; only the delegate's own
    /// <c>Invoke</c> is dropped.
    /// </summary>
    [Test]
    public async Task InvokeNamedMember_OnThisDelegate_KeepsTheMemberAccess()
    {
        var lines = EmittedLines(TranspileHelper.Transpile(Source)["calls.ts"]);

        await Assert.That(lines).Contains("h.invoke.call(h, 26);");
    }

    [Test]
    public async Task ExplicitInvoke_OnParenthesizedThisDelegate_KeepsTheParentheses()
    {
        var lines = EmittedLines(TranspileHelper.Transpile(Source)["calls.ts"]);

        await Assert.That(lines).Contains("(maybe ?? listener).call(h, 27);");
    }

    [Test]
    public async Task ExplicitInvoke_OnThisDelegate_DartTarget_DropsTheInvokeSegment()
    {
        var (files, _) = DartTranspileHelper.Transpile(Source);

        var dart = files["calls.dart"];

        await Assert.That(dart).Contains("h.onPing.call(h, 20);");
        await Assert.That(dart).Contains("listener.call(h, 23);");
        await Assert.That(dart).Contains("maybe?.call(h, 24);");
        await Assert.That(dart).DoesNotContain("onPing.invoke");
        await Assert.That(dart).DoesNotContain("listener.invoke");
    }

    private static List<string> EmittedLines(string output) =>
        output.Split('\n').Select(line => line.Trim()).ToList();
}
