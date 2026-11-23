namespace Metano.Tests;

/// <summary>
/// A <c>[PlainObject]</c> instance method lowers to a free function whose receiver is
/// the explicit <c>self</c> parameter, so every <c>this</c> of its body names
/// <c>self</c>, inside any statement or expression (#652).
/// </summary>
public class PlainObjectMethodThisTests
{
    [Test]
    public async Task ThisInsideLoopsSwitchAndTry_LowersToSelf()
    {
        var output = TranspileHelper.Transpile(
            """
            using System;

            [Transpile, PlainObject]
            public record Pt(int X, int[] Items)
            {
                public int Sum()
                {
                    var t = 0;
                    for (var i = 0; i < this.Items.Length; i++) t += this.X;
                    foreach (var it in this.Items) t += it + this.X;
                    while (t < 0) t += this.X;
                    do { t += this.X; } while (t < 0);
                    switch (t) { case 1: t += this.X; break; default: t += this.X; break; }
                    try { t += this.X; }
                    catch (Exception) when (this.X > 0) { t += this.X; }
                    finally { t += this.X; }
                    Func<int> read = () => this.X;
                    return t + read();
                }
            }
            """
        )["pt.ts"];

        await Assert.That(output).Contains("export function sum(self: Pt): number {");
        await Assert.That(output).Contains("for (let i = 0; i < self.items.length; i++) {");
        await Assert.That(output).Contains("for (const it of self.items) {");
        await Assert.That(output).Contains("tryFilter(() => self.x > 0)");
        await Assert.That(output).Contains("const read = () => self.x;");
        await Assert.That(output).DoesNotContain("this.");
    }
}
