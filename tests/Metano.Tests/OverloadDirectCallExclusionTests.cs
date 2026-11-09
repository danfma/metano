namespace Metano.Tests;

/// <summary>
/// The shapes spec 013 DECIDED-10 keeps out of the overload machinery
/// (<c>[External]</c>, <c>[Branded]</c> /
/// <c>[InlineWrapper]</c>, <c>[PlainObject]</c>, <c>[JsCallable]</c>) get no
/// factories, selectors or fast paths, so a call to their overloaded members
/// must keep the dispatcher name instead of lowering to a fast path the type
/// never declares (<c>DispatchedMethodCalls.IsEmittedContainingType</c>,
/// issue #536).
/// </summary>
public class OverloadDirectCallExclusionTests
{
    [Test]
    public async Task ExcludedShapes_KeepTheDispatcherAtTheirCallSites()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [External]
            public class Ambient
            {
                public static int Pick(int x) => x;
                public static int Pick(string s) => s.Length;
            }

            [Transpile, Branded]
            public readonly record struct Id(int Value)
            {
                public static int Pick(int x) => x;
                public static int Pick(string s) => s.Length;
            }

            [Transpile, InlineWrapper]
            public readonly record struct Tag(string Value)
            {
                public static int Pick(int x) => x;
                public static int Pick(string s) => s.Length;
            }

            [Transpile, PlainObject]
            public record Dto(int Value)
            {
                public static int Pick(int x) => x;
                public static int Pick(string s) => s.Length;
            }

            [Transpile, JsCallable]
            public interface ISetter
            {
                void Invoke(int value);
                void Invoke(string value);
            }

            [Transpile]
            public class Plain
            {
                public static int Pick(int x) => x;
                public static int Pick(string s) => s.Length;
            }

            [Transpile]
            public class Caller
            {
                public int Calls(ISetter setter)
                {
                    setter.Invoke(1);
                    setter.Invoke("a");

                    return Ambient.Pick(1)
                        + Ambient.Pick("a")
                        + Id.Pick(1)
                        + Id.Pick("a")
                        + Tag.Pick(1)
                        + Tag.Pick("a")
                        + Dto.Pick(1)
                        + Dto.Pick("a")
                        + Plain.Pick(1)
                        + Plain.Pick("a");
                }
            }
            """
        );

        var caller = result["caller.ts"];

        await Assert
            .That(caller)
            .Contains(
                "return Ambient.pick(1) + Ambient.pick(\"a\") + Id.pick(1) + Id.pick(\"a\") "
                    + "+ Tag.pick(1) + Tag.pick(\"a\") + Dto.pick(1) + Dto.pick(\"a\") "
                    + "+ Plain.pickX(1) + Plain.pickS(\"a\");"
            );
        await Assert.That(caller).Contains("setter(1);");
        await Assert.That(caller).Contains("setter(\"a\");");
    }
}
