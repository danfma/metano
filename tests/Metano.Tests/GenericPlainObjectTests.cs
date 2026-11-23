namespace Metano.Tests;

/// <summary>
/// A generic <c>[PlainObject]</c> declares its type parameters on the interface, and
/// each instance-method helper declares them ahead of its own, since a free function
/// does not inherit them from the C# type (#451).
/// </summary>
public class GenericPlainObjectTests
{
    [Test]
    public async Task GenericPlainObject_DeclaresItsTypeParameters()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;

            [Transpile, PlainObject]
            public record GB<T>(T V, int N)
            {
                public T First() => this.V;
                public U Map<U>(Func<T, U> f) => f(V);
            }

            [Transpile]
            public static class UseGb
            {
                public static GB<string> Make() => new GB<string>("a", 1);
            }
            """
        );

        var output = result["gb.ts"];

        await Assert.That(output).Contains("export interface GB<T> {");
        await Assert.That(output).Contains("export function first<T>(self: GB<T>): T {");

        await Assert
            .That(output)
            .Contains("export function map<T, U>(self: GB<T>, f: (arg: T) => U): U {");

        await Assert.That(result["use-gb.ts"]).Contains("static make(): GB<string> {");
    }
}
