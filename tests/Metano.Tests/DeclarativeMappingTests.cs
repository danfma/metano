namespace Metano.Tests;

/// <summary>
/// Tests proving that the declarative <c>[MapMethod]</c>/<c>[MapProperty]</c> attributes
/// declared in the <c>Metano.Runtime</c> namespace (under <c>Metano/Runtime/</c>) are
/// picked up by the transpiler at compile time and routed through
/// <see cref="DeclarativeMappingRegistry"/> + <see cref="BclMapper"/> instead of (or in
/// addition to) the hardcoded BCL lowering rules.
///
/// The mappings under test:
/// <list type="bullet">
///   <item><c>List&lt;T&gt;.Count → count</c></item>
///   <item><c>List&lt;T&gt;.Add(x) → list.add(x)</c></item>
///   <item><c>List&lt;T&gt;.AddRange(other) → list.addRange(other)</c></item>
///   <item><c>List&lt;T&gt;.Remove(x) → list.remove(x)</c></item>
/// </list>
/// </summary>
public class DeclarativeMappingTests
{
    [Test]
    public async Task DeclarativeAddRange_LowersToAddRange()
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
        await Assert.That(output).Contains("this.items.addRange(other)");
    }

    [Test]
    public async Task DeclarativeListCount_LowersToCount()
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
        await Assert.That(output).Contains("this.items.count");
    }

    [Test]
    public async Task DeclarativeListAdd_LowersToAdd()
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
        await Assert.That(output).Contains("this.items.add(value)");
    }

    [Test]
    public async Task DeclarativeGuidToStringN_StripsHyphens()
    {
        // Guid.ToString("N") matches the WhenArg0StringEquals = "N" filter in
        // Metano/Runtime/Guid.cs and lowers via the strip-hyphens template.
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
        // declaration in Metano/Runtime/Guid.cs and lowers to the receiver itself
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
    public async Task DeclarativeListRemove_LowersToTheRuntimeListMember()
    {
        // List<T>.Remove returns a bool, which the runtime List's `remove` member
        // mirrors, so the call is a plain member rename.
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
        await Assert.That(output).Contains("return this.items.remove(value);");
        await Assert.That(output).DoesNotContain("listRemove");
    }

    [Test]
    public async Task DeclarativeImmutableListAdd_LowersToNamespaceHelper()
    {
        // ImmutableList<T>.Add returns a NEW list — lowers to the ImmutableCollection
        // namespace helper that returns a fresh array.
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
        await Assert.That(output).Contains("ImmutableCollection.add(this.snapshots, value)");
        await Assert.That(output).Contains("ImmutableCollection");
        await Assert.That(output).Contains("metano-runtime");
    }

    /// <summary>
    /// <c>Clear()</c> on an immutable list or array lowers to
    /// <c>ImmutableCollection.clear()</c>, whose template does not print the
    /// receiver: the mapping declares the helper it names, so it is imported
    /// whatever the receiver is.
    /// </summary>
    [Test]
    public async Task DeclarativeImmutableClear_ImportsTheNamespaceHelper()
    {
        var output = TranspileHelper.Transpile(
            """
            using System.Collections.Immutable;

            [Transpile]
            public static class History
            {
                public static int Cleared() => ImmutableList<int>.Empty.Clear().Count;

                public static ImmutableArray<int> Reset(ImmutableArray<int> items) => items.Clear();
            }
            """
        )["history.ts"];

        await Assert
            .That(output)
            .Contains("import { ImmutableCollection } from \"metano-runtime\";");

        await Assert.That(output).Contains("ImmutableCollection.clear()");
    }

    [Test]
    public async Task DeclarativeImmutableListRemoveAt_LowersToNamespaceHelper()
    {
        // ImmutableList<T>.RemoveAt lowers to ImmutableCollection.removeAt — the
        // namespace helper returns a fresh array, mirroring the immutable contract.
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
        await Assert.That(output).Contains("ImmutableCollection.removeAt(this.snapshots, index)");
        await Assert.That(output).Contains("metano-runtime");
        await Assert.That(output).Contains("ImmutableCollection");
    }

    [Test]
    public async Task DeclarativeEnumParse_ReadsTheTypeArgumentDescriptor()
    {
        // Enum.Parse<T>(text) uses the $R0 placeholder in Metano/Runtime/Enums.cs to
        // read the type argument's runtime type descriptor (spec 011 FR-024): the
        // template is `enumFromRaw($R0, $0)`, and the frontend asks for the enum's
        // descriptor as `typeof(Status)` would.
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
        await Assert.That(output).Contains("return enumFromRaw(Status$runtimeType, text);");
        await Assert.That(result["status.ts"]).Contains("export const Status$runtimeType");
    }
}
