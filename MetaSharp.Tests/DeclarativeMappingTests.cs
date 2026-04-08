namespace MetaSharp.Tests;

/// <summary>
/// Tests proving that the declarative <c>[MapMethod]</c>/<c>[MapProperty]</c> attributes
/// declared in the <c>MetaSharp.Runtime</c> namespace (under <c>MetaSharp/Runtime/</c>) are
/// picked up by the transpiler at compile time and routed through
/// <see cref="DeclarativeMappingRegistry"/> + <see cref="BclMapper"/> instead of (or in
/// addition to) the hardcoded BCL lowering rules.
///
/// The mappings under test:
/// <list type="bullet">
///   <item><c>List&lt;T&gt;.Count → length</c> — also covered by the legacy hardcoded path</item>
///   <item><c>List&lt;T&gt;.Add(x) → list.push(x)</c> — also covered by the legacy hardcoded path</item>
///   <item><c>List&lt;T&gt;.AddRange(other) → list.push(...other)</c> — uses the JsTemplate
///   form and has no hardcoded equivalent, so a passing test here proves the declarative
///   path is actually being executed end-to-end</item>
/// </list>
/// </summary>
public class DeclarativeMappingTests
{
    [Test]
    public async Task DeclarativeAddRange_LowersToSpreadPush()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class TodoList
            {
                public List<int> Items { get; } = [];
                public void Merge(List<int> other) => Items.AddRange(other);
            }
            """
        );

        var output = result["todo-list.ts"];
        // The JsTemplate is "$this.push(...$0)" — $this resolves to the receiver
        // (this.items) and $0 resolves to the argument identifier (other).
        await Assert.That(output).Contains("this.items.push(...other)");
    }

    [Test]
    public async Task DeclarativeListCount_LowersToLength()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class TodoList
            {
                public List<int> Items { get; } = [];
                public int Total => Items.Count;
            }
            """
        );

        var output = result["todo-list.ts"];
        await Assert.That(output).Contains("this.items.length");
    }

    [Test]
    public async Task DeclarativeListAdd_LowersToPush()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class TodoList
            {
                public List<int> Items { get; } = [];
                public void Append(int value) => Items.Add(value);
            }
            """
        );

        var output = result["todo-list.ts"];
        await Assert.That(output).Contains("this.items.push(value)");
    }

    [Test]
    public async Task DeclarativeGuidToStringN_StripsHyphens()
    {
        // Guid.ToString("N") matches the WhenArg0StringEquals = "N" filter in
        // MetaSharp/Runtime/Guid.cs and lowers via the strip-hyphens template.
        var result = TranspileHelper.Transpile(
            """
            using System;

            [Transpile]
            public class IdGen
            {
                public string Compact(Guid id) => id.ToString("N");
            }
            """
        );

        var output = result["id-gen.ts"];
        await Assert.That(output).Contains("id.replace(/-/g, \"\")");
    }

    [Test]
    public async Task DeclarativeGuidToStringDefault_LowersToIdentity()
    {
        // Guid.ToString() with no argument falls through to the unfiltered fallback
        // declaration in MetaSharp/Runtime/Guid.cs and lowers to the receiver itself
        // (Guid is already a string at runtime).
        var result = TranspileHelper.Transpile(
            """
            using System;

            [Transpile]
            public class IdGen
            {
                public string Render(Guid id) => id.ToString();
            }
            """
        );

        var output = result["id-gen.ts"];
        // The body should be `return id;` — the ToString() call collapses to its receiver.
        await Assert.That(output).Contains("return id;");
    }

    [Test]
    public async Task DeclarativeListRemove_LowersToCapturedReceiverIife()
    {
        // List<T>.Remove returns a bool. The declarative template wraps an arrow IIFE
        // that captures the receiver as `arr`, finds the index via indexOf, splices it
        // out if found, and returns the boolean. Capturing the receiver as the IIFE
        // argument prevents double-evaluation when the receiver is a method call.
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class TodoList
            {
                public List<int> Items { get; } = [];
                public bool Discard(int value) => Items.Remove(value);
            }
            """
        );

        var output = result["todo-list.ts"];
        await Assert.That(output).Contains("arr.indexOf(value)");
        await Assert.That(output).Contains("arr.splice(i, 1)");
        await Assert.That(output).Contains("return true");
        await Assert.That(output).Contains("return false");
        // Receiver is passed as the IIFE argument, not duplicated.
        await Assert.That(output).Contains("})(this.items)");
    }

    [Test]
    public async Task DeclarativeImmutableListAdd_LowersToSpread()
    {
        // ImmutableList<T>.Add returns a NEW list — the spread template creates a fresh
        // array containing the original elements followed by the new item.
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Immutable;

            [Transpile]
            public class History
            {
                public ImmutableList<int> Snapshots { get; private set; } = ImmutableList<int>.Empty;
                public void Record(int value) { Snapshots = Snapshots.Add(value); }
            }
            """
        );

        var output = result["history.ts"];
        await Assert.That(output).Contains("[...this.snapshots, value]");
    }

    [Test]
    public async Task DeclarativeImmutableListRemoveAt_LowersToCapturedReceiverIife()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Immutable;

            [Transpile]
            public class History
            {
                public ImmutableList<int> Snapshots { get; private set; } = ImmutableList<int>.Empty;
                public void DropAt(int index) { Snapshots = Snapshots.RemoveAt(index); }
            }
            """
        );

        var output = result["history.ts"];
        // The IIFE captures the receiver as `arr` and slices around the index. The arrow
        // body is a single expression so the IIFE form is `((arr) => [...])(receiver)`,
        // not `((arr) => { ... })(receiver)`.
        await Assert.That(output).Contains("arr.slice(0, index)");
        await Assert.That(output).Contains("arr.slice(index + 1)");
        await Assert.That(output).Contains("])(this.snapshots)");
    }

    [Test]
    public async Task DeclarativeEnumParse_EmbedsTypeArgumentName()
    {
        // Enum.Parse<T>(text) uses the $T0 placeholder in MetaSharp/Runtime/Enums.cs to
        // embed the user's enum type name into the lowered indexer expression. The
        // template is `$T0[$0 as keyof typeof $T0]`, so a call like
        // `Enum.Parse<Status>(text)` lowers to `Status[text as keyof typeof Status]`.
        var result = TranspileHelper.Transpile(
            """
            using System;

            [Transpile]
            public enum Status { Active, Inactive }

            [Transpile]
            public class StatusParser
            {
                public Status Parse(string text) => Enum.Parse<Status>(text);
            }
            """
        );

        var output = result["status-parser.ts"];
        await Assert.That(output).Contains("Status[text as keyof typeof Status]");
    }
}
