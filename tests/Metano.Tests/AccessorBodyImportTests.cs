namespace Metano.Tests;

/// <summary>
/// #768: a type referenced only inside an accessor body (a setter or an init accessor,
/// static or instance, block or expression bodied) is imported, so the module does not
/// throw a ReferenceError on the first write. Each shape asserts the whole module and
/// type-checks it with tsgo, which reports a missing import as TS2304.
/// </summary>
public class AccessorBodyImportTests
{
    private const string Sink = """
        using System;
        using Metano.Annotations;

        [Transpile]
        public static class Sink
        {
            public static void Hit(object value) { }
        }

        """;

    private static async Task<string> ModuleOf(string source, string fileName)
    {
        var files = TranspileHelper.Transpile(Sink + source);

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);

        return files[fileName];
    }

    [Test]
    public async Task BlockBodiedSetter_ImportsItsReferences()
    {
        var module = await ModuleOf(
            """
            [Transpile]
            public class BlockSetter
            {
                private int _value;

                public int Value
                {
                    get => _value;
                    set
                    {
                        _value = value;
                        Sink.Hit(this);
                    }
                }
            }
            """,
            "block-setter.ts"
        );

        await Assert
            .That(module)
            .IsEqualTo(
                """
                import { Sink } from "./sink.js";

                export class BlockSetter {
                  private _value: number = 0;

                  constructor() { }

                  get value(): number {
                    return this._value;
                  }

                  set value(value: number) {
                    this._value = value;
                    Sink.hit(this);
                  }
                }

                """
            );
    }

    [Test]
    public async Task ExpressionBodiedSetter_ImportsItsReferences()
    {
        var module = await ModuleOf(
            """
            [Transpile]
            public class ExpressionSetter
            {
                public int Value
                {
                    get => 0;
                    set => Sink.Hit(value);
                }
            }
            """,
            "expression-setter.ts"
        );

        await Assert
            .That(module)
            .IsEqualTo(
                """
                import { Sink } from "./sink.js";

                export class ExpressionSetter {
                  constructor() { }

                  get value(): number {
                    return 0;
                  }

                  set value(value: number) {
                    Sink.hit(value);
                  }
                }

                """
            );
    }

    [Test]
    public async Task StaticSetter_ImportsItsReferences()
    {
        var module = await ModuleOf(
            """
            [Transpile]
            public class StaticSetter
            {
                private static int _value;

                public static int Value
                {
                    get => _value;
                    set
                    {
                        _value = value;
                        Sink.Hit(value);
                    }
                }
            }
            """,
            "static-setter.ts"
        );

        await Assert
            .That(module)
            .IsEqualTo(
                """
                import { Sink } from "./sink.js";

                export class StaticSetter {
                  private static _value: number = 0;

                  constructor() { }

                  static get value(): number {
                    return StaticSetter._value;
                  }

                  static set value(value: number) {
                    StaticSetter._value = value;
                    Sink.hit(value);
                  }
                }

                """
            );
    }

    [Test]
    public async Task InitAccessor_ImportsItsReferences()
    {
        var module = await ModuleOf(
            """
            [Transpile]
            public class InitAccessor
            {
                private readonly int _value;

                public int Value
                {
                    get => _value;
                    init
                    {
                        _value = value;
                        Sink.Hit(value);
                    }
                }
            }
            """,
            "init-accessor.ts"
        );

        await Assert
            .That(module)
            .IsEqualTo(
                """
                import { Sink } from "./sink.js";

                export class InitAccessor {
                  private _value: number = 0;

                  constructor() { }

                  get value(): number {
                    return this._value;
                  }

                  set value(value: number) {
                    this._value = value;
                    Sink.hit(value);
                  }
                }

                """
            );
    }

    [Test]
    public async Task SetterThrowingBclExceptions_ImportsTheRuntimeExceptions()
    {
        // Reported by Metanui: an [ExportFromBcl] exception thrown only from a setter
        // body had no import either (ReferenceError: InvalidOperationException).
        var module = await ModuleOf(
            """
            [Transpile]
            public class Guarded
            {
                private int _value;

                public int Value
                {
                    get => _value;
                    set
                    {
                        if (value < 0)
                            throw new InvalidOperationException("negative");

                        _value = value;
                    }
                }

                public static int Shared
                {
                    get => 0;
                    set => throw new ArgumentOutOfRangeException(nameof(value));
                }
            }
            """,
            "guarded.ts"
        );

        await Assert
            .That(module)
            .IsEqualTo(
                """
                import { throwError } from "metano-runtime";
                import { ArgumentOutOfRangeException } from "metano-runtime/system/exceptions/argument-out-of-range-exception";
                import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";

                export class Guarded {
                  private _value: number = 0;

                  constructor() { }

                  get value(): number {
                    return this._value;
                  }

                  set value(value: number) {
                    if (value < 0) {
                      throw new InvalidOperationException("negative");
                    }

                    this._value = value;
                  }

                  static get shared(): number {
                    return 0;
                  }

                  static set shared(value: number) {
                    throwError(new ArgumentOutOfRangeException("value"));
                  }
                }

                """
            );
    }

    [Test]
    public async Task EventWithAccessorBodies_ReportsUnsupported()
    {
        // The class lowering emits every event as a field-like instance event, so
        // custom add/remove bodies would be dropped without a word.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Sink
                + """
                [Transpile]
                public class Notifier
                {
                    public event Action Changed
                    {
                        add => Sink.Hit(value);
                        remove { }
                    }
                }
                """
        );

        await Assert
            .That(diagnostics.Select(d => d.Message))
            .Contains(
                "The event 'Notifier.Changed' is declared with custom add/remove accessors, "
                    + "which TypeScript emission does not support: it is emitted as a "
                    + "field-like instance event."
            );
    }

    [Test]
    public async Task StaticEvent_ReportsUnsupported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;
            using Metano.Annotations;

            [Transpile]
            public class Clock
            {
                public static event Action? Ticked;
            }
            """
        );

        await Assert
            .That(diagnostics.Select(d => d.Message))
            .Contains(
                "The event 'Clock.Ticked' is static, which TypeScript emission does not "
                    + "support: it is emitted as a field-like instance event."
            );
    }
}
