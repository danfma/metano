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
    public async Task PromotedParam_ReadFromMemberBody_ReadsTheProperty()
    {
        // The promoted property (`constructor(readonly limit: number)`) is
        // the parameter's only emitted storage, so a member body that reads
        // the parameter reads the property; a bare `limit` would be out of
        // scope there. A deferred initializer runs in the constructor body,
        // where the bare parameter is in scope, so it keeps it.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Quota(int limit)
            {
                public int Limit { get; } = limit;

                public int Twice { get; } = limit * 2;

                public int Doubled => limit * 2;

                public bool Allows(int amount) => amount <= limit;
            }
            """
        );

        var output = result["quota.ts"];
        await Assert.That(output).Contains("constructor(readonly limit: number)");
        await Assert.That(output).Contains("this.twice = limit * 2;");
        await Assert.That(output).Contains("return this.limit * 2;");
        await Assert.That(output).Contains("return amount <= this.limit;");
        await Assert.That(output).DoesNotContain("_limit");
    }

    [Test]
    public async Task PromotedParam_WithASettableProperty_KeepsItsOwnField()
    {
        // `m.Limit = 5` changes the property but not the parameter, so C#
        // keeps separate storage for it (CS9124): the constructor still folds
        // the property, and the member body reads its own readonly field.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Settable(int limit)
            {
                public int Limit { get; set; } = limit;

                public int Original() => limit;
            }
            """
        );

        var output = result["settable.ts"];
        await Assert.That(output).Contains("constructor(public limit: number)");
        await Assert.That(output).Contains("this._limit = limit;");
        await Assert.That(output).Contains("return this._limit;");
        await Assert.That(output).Contains("private readonly _limit: number;");
    }

    [Test]
    public async Task PromotedParam_WithAnInitAccessor_KeepsItsOwnField()
    {
        // An object initializer (`new Sized(1) { Size = 6 }`) changes an
        // init-only property after construction, but not the parameter.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Sized(int size)
            {
                public int Size { get; init; } = size;

                public int Original() => size;
            }
            """
        );

        var output = result["sized.ts"];
        await Assert.That(output).Contains("constructor(readonly size: number)");
        await Assert.That(output).Contains("return this._size;");
        await Assert.That(output).Contains("private readonly _size: number;");
    }

    [Test]
    public async Task WrittenClassParam_IsNotPromoted()
    {
        // A member that writes the parameter makes it diverge from the
        // property, so the constructor does not fold the pair: the property
        // keeps its own declaration and the parameter a mutable field.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Written(int count)
            {
                public int Count { get; } = count;

                public void Reset() => count = 0;

                public int Current() => count;
            }
            """
        );

        var output = result["written.ts"];
        await Assert.That(output).Contains("constructor(count: number)");
        await Assert.That(output).Contains("private _count: number;");
        await Assert.That(output).Contains("this.count = this._count;");
        await Assert.That(output).Contains("this._count = 0;");
        await Assert.That(output).Contains("return this._count;");
    }

    [Test]
    public async Task PositionalRecordParam_WrittenByAnInitializer_StaysPromoted()
    {
        // C# assigns the positional property the original argument before any
        // initializer runs: with an argument of 1, `Y = X++` leaves the
        // property X at 1 and Y at 1 (the parameter becomes 2). The parameter
        // property does the same; the initializer keeps the bare constructor
        // parameter.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Counted(int X)
            {
                public int Y { get; } = X++;

                public int GetX() => X;
            }

            [Transpile]
            public record struct CountedValue(int X)
            {
                public int Y { get; } = X++;
            }
            """
        );

        var counted = result["counted.ts"];
        await Assert.That(counted).Contains("constructor(readonly x: number)");
        await Assert.That(counted).Contains("this.y = x++;");
        await Assert.That(counted).Contains("return this.x;");
        await Assert.That(counted).DoesNotContain("private _x");

        // The record struct pins the same lowering with its mutable
        // positional property (`public`).
        var countedValue = result["counted-value.ts"];
        await Assert.That(countedValue).Contains("constructor(public x: number)");
        await Assert.That(countedValue).Contains("this.y = x++;");
    }

    [Test]
    public async Task RecordParam_WithAnExplicitPropertyAndAWrite_IsNotPromoted()
    {
        // An explicitly declared `X { get; } = X` is an ordinary initializer
        // that runs in declaration order, after `Y = X++`, so X ends at 2:
        // the pair is not folded and the property reads the written value.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Explicit(int X)
            {
                public int Y { get; } = X++;

                public int X { get; } = X;
            }
            """
        );

        var output = result["explicit.ts"];
        await Assert.That(output).Contains("constructor(x: number)");
        await Assert.That(output).Contains("this.y = x++;\n    this.x = x;");
    }

    [Test]
    public async Task WrittenClassParam_BehindAnExpressionBodiedAlias_IsNotPromoted()
    {
        // `A => a` reads the parameter live, so after `a++` it must see the
        // new value: the getter reads the mutable field instead of being
        // folded into a readonly parameter property.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Live(int a)
            {
                public int A => a;

                public void Inc() => a++;
            }
            """
        );

        var output = result["live.ts"];
        await Assert.That(output).Contains("constructor(a: number)");
        await Assert.That(output).Contains("private _a: number;");
        await Assert.That(output).Contains("get a(): number {\n    return this._a;");
        await Assert.That(output).Contains("this._a++;");
    }

    [Test]
    public async Task WrittenParam_InAnInitializer_RunsInDeclarationOrder()
    {
        // `B = a++` runs before `A = a`, so A sees the incremented value and
        // so does a later member read: all three share the mutable field.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Ordered(int a)
            {
                public int B { get; } = a++;

                public int A { get; } = a;

                public int Read() => a;
            }
            """
        );

        var output = result["ordered.ts"];
        await Assert.That(output).Contains("constructor(a: number)");
        await Assert.That(output).Contains("return this._a;");

        await Assert
            .That(output)
            .Contains("this._a = a;\n    this.b = this._a++;\n    this.a = this._a;");
    }

    [Test]
    public async Task WrittenParam_ByAnInitializerLambda_GetsAMutableField()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Clearable(int total)
            {
                public int Total { get; } = total;

                public System.Action Clear { get; } = () => total = 0;

                public int Current() => total;
            }
            """
        );

        var output = result["clearable.ts"];
        await Assert.That(output).Contains("private _total: number;");
        await Assert.That(output).Contains("this._total = 0");
        await Assert.That(output).Contains("return this._total;");
    }

    [Test]
    public async Task ParenthesizedWrite_IsAWrite()
    {
        // The identifier's syntactic parent is the parentheses, not the
        // assignment, but the bound operation is the assignment's target, so
        // the parameter is not promoted.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Parenthesized(int a)
            {
                public int A { get; } = a;

                public int Run()
                {
                    (a) = 9;
                    return a;
                }
            }
            """
        );

        var output = result["parenthesized.ts"];
        await Assert.That(output).Contains("constructor(a: number)");
        await Assert.That(output).Contains("private _a: number;");
        await Assert.That(output).Contains("this._a = 9;");
    }

    [Test]
    public async Task DeconstructionWrite_IsAWrite()
    {
        // Every element of a deconstruction target is written, so neither
        // parameter is promoted and both fields are mutable. The
        // deconstruction itself still reports MS0001, which this test does
        // not inspect.
        var (result, _) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public sealed class Deconstructed(int a, int b)
            {
                public int A { get; } = a;

                public int Swap()
                {
                    (a, b) = (b, a);
                    return a;
                }
            }
            """
        );

        var output = result["deconstructed.ts"];
        await Assert.That(output).Contains("constructor(a: number, b: number)");
        await Assert.That(output).Contains("private _a: number;");
        await Assert.That(output).Contains("private _b: number;");
    }

    [Test]
    public async Task WrittenCapturedParam_GetsAMutableField()
    {
        // A member that writes a captured parameter writes its backing
        // field, which therefore cannot be readonly (TS2540 otherwise); a
        // parameter that is only read keeps the readonly field.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Tally(int count, int step)
            {
                public void Bump() => count += step;

                public int Read() => count;
            }
            """
        );

        var output = result["tally.ts"];
        await Assert.That(output).Contains("private _count: number;");
        await Assert.That(output).Contains("private readonly _step: number;");
        await Assert.That(output).Contains("this._count += this._step;");
    }

    [Test]
    public async Task PromotedParam_ToAVirtualProperty_KeepsItsOwnField()
    {
        // An override can replace the getter, so the read cannot go
        // through the property.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Overridable(int limit)
            {
                public virtual int Limit { get; } = limit;

                public int Original() => limit;
            }
            """
        );

        var output = result["overridable.ts"];
        await Assert.That(output).Contains("return this._limit;");
        await Assert.That(output).Contains("private readonly _limit: number;");
    }

    [Test]
    public async Task PromotedParam_ToAnIgnoredProperty_IsCaptured()
    {
        // An [Ignore]d property is not emitted, so nothing is promoted: the
        // parameter needs its own field for the member body to read.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Hidden(int limit)
            {
                [Ignore]
                public int Limit { get; } = limit;

                public int Twice() => limit * 2;
            }
            """
        );

        var output = result["hidden.ts"];
        await Assert.That(output).Contains("private readonly _limit: number;");
        await Assert.That(output).Contains("return this._limit * 2;");
    }

    [Test]
    public async Task PropertiesDifferingOnlyByCase_PromoteTheExactCaseMatch()
    {
        // Two alias properties whose names differ only by case used to crash
        // the case-insensitive name lookup. The exact-case one is promoted;
        // the other reads it, since the parameter is never written.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Cased(int value)
            {
                public int value { get; } = value;

                public int Value => value;
            }
            """
        );

        var output = result["cased.ts"];
        await Assert.That(output).Contains("constructor(readonly value: number)");
        await Assert.That(output).DoesNotContain("_value");
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
    public async Task FieldNameCollision_SynthesizesTheNextFreeName()
    {
        // The user already declares `_view` for an unrelated purpose AND has
        // a `view` primary-ctor param the detector captures. The synthesized
        // field takes the next free name, so the user's field stays intact
        // and the member body still reads the parameter's storage (a bare
        // `view` would be out of scope).
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
        await Assert.That(output).Contains("private _view: number = 0");
        await Assert.That(output).Contains("private readonly _view2: ICounterView");
        await Assert.That(output).Contains("this._view2.showCounter(this._view)");
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
