using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The shapes spec 013 DECIDED-10 keeps out of the overload machinery
/// (<c>[External]</c>, <c>[PlainObject]</c>, <c>[JsCallable]</c>) get no
/// factories, selectors or fast paths, so a call to their overloaded members
/// must keep the dispatcher name instead of lowering to a fast path the type
/// never declares (<c>DispatchedMethodCalls.IsEmittedContainingType</c>,
/// issue #536). A control class pins that the same call does lower to a fast
/// path, and so does a <c>[Branded]</c> / <c>[InlineWrapper]</c> companion,
/// whose static groups export their fast paths since #569.
/// </summary>
public class OverloadDirectCallExclusionTests
{
    private static async Task<string> CallerOf(
        string declarations,
        string calls,
        string parameters = ""
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using Metano.Annotations.TypeScript;

            {{declarations}}

            [Transpile]
            public class Caller
            {
                public int Calls({{parameters}}) => {{calls}};
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity == MetanoDiagnosticSeverity.Error))
            .IsEmpty();

        return files["caller.ts"];
    }

    private static string ExpectedCaller(string text) => text + "\n";

    [Test]
    public async Task Control_TranspiledClass_CallsTheFastPaths()
    {
        var caller = await CallerOf(
            """
            [Transpile]
            public class Shape
            {
                public static int Pick(int x) => x;
                public static int Pick(string s) => s.Length;
            }
            """,
            """Shape.Pick(1) + Shape.Pick("a")"""
        );

        await Assert
            .That(caller)
            .IsEqualTo(
                ExpectedCaller(
                    """
                    import { Shape } from "./shape";

                    export class Caller {
                      constructor() { }

                      calls(): number {
                        return Shape.pickX(1) + Shape.pickS("a");
                      }
                    }
                    """
                )
            );
    }

    [Test]
    public async Task External_KeepsTheDispatcher()
    {
        var caller = await CallerOf(
            """
            [assembly: TranspileAssembly]

            [External]
            public static class Shape
            {
                public static int Pick(int x) => x;
                public static int Pick(string s) => s.Length;
            }
            """,
            """Shape.Pick(1) + Shape.Pick("a")"""
        );

        await Assert
            .That(caller)
            .IsEqualTo(
                ExpectedCaller(
                    """
                    export class Caller {
                      constructor() { }

                      calls(): number {
                        return Shape.pick(1) + Shape.pick("a");
                      }
                    }
                    """
                )
            );
    }

    [Test]
    public async Task Branded_CallsTheCompanionFastPath()
    {
        var caller = await CallerOf(
            """
            [Transpile, Branded]
            public readonly record struct Shape(int Value)
            {
                public static int Pick(int x) => x;
                public static int Pick(string s) => s.Length;
            }
            """,
            """Shape.Pick(1) + Shape.Pick("a")"""
        );

        await Assert
            .That(caller)
            .IsEqualTo(
                ExpectedCaller(
                    """
                    import { Shape } from "./shape";

                    export class Caller {
                      constructor() { }

                      calls(): number {
                        return Shape.pickX(1) + Shape.pickS("a");
                      }
                    }
                    """
                )
            );
    }

    [Test]
    public async Task InlineWrapper_CallsTheCompanionFastPath()
    {
        var caller = await CallerOf(
            """
            [Transpile, InlineWrapper]
            public readonly record struct Shape(string Value)
            {
                public static int Pick(int x) => x;
                public static int Pick(string s) => s.Length;
            }
            """,
            """Shape.Pick(1) + Shape.Pick("a")"""
        );

        await Assert
            .That(caller)
            .IsEqualTo(
                ExpectedCaller(
                    """
                    import { Shape } from "./shape";

                    export class Caller {
                      constructor() { }

                      calls(): number {
                        return Shape.pickX(1) + Shape.pickS("a");
                      }
                    }
                    """
                )
            );
    }

    // The dispatcher name is the pinned part: a [PlainObject] keeps only its
    // interface, so `Shape.pick` itself does not type-check (its static
    // overloads are dropped silently, tracked as #566).
    [Test]
    public async Task PlainObject_KeepsTheDispatcher()
    {
        var caller = await CallerOf(
            """
            [Transpile, PlainObject]
            public record Shape(int Value)
            {
                public static int Pick(int x) => x;
                public static int Pick(string s) => s.Length;
            }
            """,
            """Shape.Pick(1) + Shape.Pick("a")"""
        );

        await Assert
            .That(caller)
            .IsEqualTo(
                ExpectedCaller(
                    """
                    import { Shape } from "./shape";

                    export class Caller {
                      constructor() { }

                      calls(): number {
                        return Shape.pick(1) + Shape.pick("a");
                      }
                    }
                    """
                )
            );
    }

    // Pins MethodOverloadGroups.IsTranspiledInterface: [JsCallable] only applies
    // to an interface, which reaches the interface branch of the gate.
    [Test]
    public async Task JsCallableInterface_KeepsTheDispatcherThroughIsTranspiledInterface()
    {
        var caller = await CallerOf(
            """
            [Transpile, JsCallable]
            public interface IShape
            {
                int Invoke(int value);
                int Invoke(string value);
            }
            """,
            """s.Invoke(1) + s.Invoke("a")""",
            """IShape s"""
        );

        await Assert
            .That(caller)
            .IsEqualTo(
                ExpectedCaller(
                    """
                    export class Caller {
                      constructor() { }

                      calls(s: ((value: number) => number) & ((value: string) => number)): number {
                        return s(1) + s("a");
                      }
                    }
                    """
                )
            );
    }
}
