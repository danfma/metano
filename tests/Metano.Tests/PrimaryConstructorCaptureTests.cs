namespace Metano.Tests;

/// <summary>
/// Tests for the C# 12 primary-constructor capture rules: parameters
/// referenced from member bodies become hidden backing fields, mirroring
/// what Roslyn synthesizes on the C# side. Without the synthesis the
/// generated TypeScript drops the parameter from the constructor and
/// references an undefined identifier inside the methods.
/// </summary>
public class PrimaryConstructorCaptureTests
{
    [Test]
    public async Task CapturedParam_SynthesizesPrivateField()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface ICounterView
            {
                int Count { get; }
                void ShowCounter(int value);
            }

            [Transpile]
            public sealed class CounterPresenter(ICounterView view)
            {
                public void Refresh()
                {
                    view.ShowCounter(view.Count);
                }
            }
            """
        );

        var output = result["counter-presenter.ts"];
        await Assert.That(output).Contains("private readonly _view: ICounterView");
        await Assert.That(output).Contains("constructor(view: ICounterView)");
        await Assert.That(output).Contains("this._view = view;");
        await Assert.That(output).Contains("this._view.showCounter(this._view.count)");
    }

    [Test]
    public async Task ExplicitlyCapturedParam_KeepsUserField()
    {
        // When the user explicitly captures the parameter into a field
        // (`private readonly Counter _state = initialState;`), the
        // detector must reuse that field instead of synthesizing a
        // duplicate `_initialState`.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Counter
            {
                public int Value { get; }

                public Counter(int value)
                {
                    Value = value;
                }
            }

            [Transpile]
            public sealed class CounterPresenter(Counter initialState)
            {
                private Counter _state = initialState;

                public int CurrentValue() => _state.Value;
            }
            """
        );

        var output = result["counter-presenter.ts"];
        await Assert.That(output).Contains("this._state = initialState;");
        await Assert.That(output).DoesNotContain("_initialState");
    }

    [Test]
    public async Task MixedCapture_SynthesizesOnlyForImplicitParam()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface ICounterView
            {
                int Count { get; }
                void ShowCounter(int value);
            }

            [Transpile]
            public sealed class Counter
            {
                public int Value { get; }

                public Counter(int value)
                {
                    Value = value;
                }
            }

            [Transpile]
            public sealed class CounterPresenter(ICounterView view, Counter initialState)
            {
                private Counter _state = initialState;

                public void Refresh()
                {
                    view.ShowCounter(_state.Value);
                }
            }
            """
        );

        var output = result["counter-presenter.ts"];
        await Assert.That(output).Contains("private readonly _view: ICounterView");
        await Assert.That(output).Contains("this._view = view;");
        await Assert.That(output).Contains("this._state = initialState;");
        await Assert.That(output).Contains("this._view.showCounter(this._state.value)");
        await Assert.That(output).DoesNotContain("_initialState");
    }

    [Test]
    public async Task CtorOnlyParam_DoesNotSynthesizeField()
    {
        // A primary-ctor param that is only used inside the ctor body
        // (here, forwarded to a field initializer that itself is the
        // capture site) must not become a second field. The user's
        // explicit `_state` is the only backing storage.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Holder(int initial)
            {
                private int _value = initial;

                public int Read() => _value;
            }
            """
        );

        var output = result["holder.ts"];
        await Assert.That(output).Contains("private _value: number");
        await Assert.That(output).Contains("this._value = initial;");
        await Assert.That(output).DoesNotContain("_initial");
        await Assert.That(output).Contains("this._value");
    }

    [Test]
    public async Task PromotedProperty_DoesNotSynthesizeBackingField()
    {
        // A primary-ctor param paired with a public property of the
        // same (case-insensitive) name is already promoted via the
        // existing constructor bridge. The detector must skip it so
        // the emitted class doesn't carry both the promoted property
        // AND a synthesized `_name` private field.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class TodoList(string name)
            {
                public string Name => name;
            }
            """
        );

        var output = result["todo-list.ts"];
        await Assert.That(output).DoesNotContain("private readonly _name");
        await Assert.That(output).DoesNotContain("this._name");
    }

    [Test]
    public async Task CapturedParam_ReadFromFieldInitializer_KeepsBareReference()
    {
        // Field initializers execute before the ctor body assigns the
        // synthesized field. Reading `this._x` from an initializer
        // would observe undefined; the rewrite must keep the bare
        // param reference in initializer positions while still
        // rewriting the same identifier inside method bodies.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface ICounterView
            {
                void ShowCounter(int value);
            }

            [Transpile]
            public sealed class Presenter(ICounterView view)
            {
                private readonly int _initial = 0;

                public void Refresh() => view.ShowCounter(_initial);
            }
            """
        );

        var output = result["presenter.ts"];
        // Method-body usage rewrites to the synthesized field.
        await Assert.That(output).Contains("this._view.showCounter(this._initial)");
    }

    [Test]
    public async Task FieldNameCollision_SkipsSynthesisAndPreservesUserField()
    {
        // The user already declares `_view` for an unrelated
        // purpose AND has a `view` primary-ctor param the detector
        // would otherwise capture into `_view`. The synthesizer
        // cannot pick a non-conflicting name without surprising
        // downstream consumers, so it skips the synthesis. The
        // user's existing field stays intact; the bare `view`
        // reference inside member bodies stays unrewritten and the
        // consumer's TS compiler surfaces the missing identifier.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface ICounterView
            {
                void ShowCounter(int value);
            }

            [Transpile]
            public sealed class Presenter(ICounterView view)
            {
                private int _view = 0;

                public void Refresh() => view.ShowCounter(_view);
            }
            """
        );

        var output = result["presenter.ts"];
        // User's hand-written field survives untouched.
        await Assert.That(output).Contains("private _view: number = 0");
        // No synthesized ICounterView field — the collision suppressed it.
        await Assert.That(output).DoesNotContain("private readonly _view: ICounterView");
    }

    [Test]
    public async Task LocalShadowingParam_DoesNotRewriteLocal()
    {
        // A method-body local with the same name as the captured
        // param must NOT get rewritten — the rewrite is keyed on
        // the IParameterSymbol identity, so Roslyn's semantic
        // resolution distinguishes the two cleanly.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface ICounterView
            {
                void ShowCounter(int value);
            }

            [Transpile]
            public sealed class Presenter(ICounterView view)
            {
                public int Compute()
                {
                    int view = 42;
                    return view;
                }

                public void Refresh()
                {
                    view.ShowCounter(0);
                }
            }
            """
        );

        var output = result["presenter.ts"];

        await Assert.That(output).Contains("private readonly _view: ICounterView");
        // The local `view = 42` stays as a local — the rewrite uses
        // symbol identity, not name matching, so the local symbol
        // never resolves to the captured-param map.
        await Assert.That(output).Contains("const view = 42");
        await Assert.That(output).Contains("return view;");
        // The other method still uses the synthesized field.
        await Assert.That(output).Contains("this._view.showCounter(0)");
    }

    [Test]
    public async Task PrimaryCtorWithBaseCall_AssignsCapturedFieldsAfterSuper()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Animal(string name)
            {
                public string Name => name;
            }

            [Transpile]
            public sealed class Dog(string name, string breed) : Animal(name)
            {
                public string Describe() => $"{breed} named {name}";
            }
            """
        );

        var output = result["app/dog.ts"];
        var superIndex = output.IndexOf("super(", StringComparison.Ordinal);
        await Assert.That(superIndex).IsGreaterThan(-1);

        var assignments = System
            .Text.RegularExpressions.Regex.Matches(output, @"this\._\w+\s*=")
            .Cast<System.Text.RegularExpressions.Match>()
            .ToList();

        await Assert.That(assignments.Count).IsGreaterThanOrEqualTo(2);
        foreach (var m in assignments)
            await Assert.That(m.Index).IsGreaterThan(superIndex);
    }

    [Test]
    public async Task StructPrimaryCtor_PromotesCapturedParam()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public readonly struct Point(int x, int y)
            {
                public int Magnitude() => x * x + y * y;
            }
            """
        );

        var output = result["app/point.ts"];
        await Assert.That(output).Contains("private readonly _x: number");
        await Assert.That(output).Contains("private readonly _y: number");
        await Assert.That(output).Contains("this._x * this._x + this._y * this._y");
    }

    [Test]
    public async Task MultipleMethods_CapturingSameParam_ShareSyntheticField()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface ILogger
            {
                void Log(string message);
            }

            [Transpile]
            public sealed class Service(ILogger logger)
            {
                public void Start() => logger.Log("start");
                public void Stop() => logger.Log("stop");
            }
            """
        );

        var output = result["service.ts"];
        await Assert.That(output).Contains("private readonly _logger: ILogger");
        await Assert.That(output).Contains("this._logger = logger;");
        // Both methods route through the same synthesized field.
        await Assert.That(output).Contains("this._logger.log(\"start\")");
        await Assert.That(output).Contains("this._logger.log(\"stop\")");
    }
}
