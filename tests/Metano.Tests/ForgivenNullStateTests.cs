using Metano.Tests.TreeShaking;

namespace Metano.Tests;

/// <summary>
/// Where C# flow analysis proves a nullable variable non-null by a construct a
/// static checker does not follow — a null-forgiving dereference earlier in the
/// flow, a call whose nullable attributes say so — the read is asserted non-null
/// where it is used as non-null (#775). A read narrowed by an assignment or a
/// null test is left alone.
/// </summary>
public class ForgivenNullStateTests
{
    private const string Source = """
        #nullable enable

        using System.Diagnostics.CodeAnalysis;

        [Transpile]
        public sealed class Link
        {
            public Link? Prev;
            public Link Value = new Link(0);
            public int Id;

            public Link(int id) { Id = id; }
        }

        [Transpile]
        public sealed class Holder
        {
            public Link? Head;

            [MemberNotNull(nameof(Head))]
            public void Ensure() { Head ??= new Link(1); }

            public int Read()
            {
                Ensure();
                return Head.Id;
            }
        }

        [Transpile]
        public static class Stack
        {
            public static int Use(Link link) => link.Id;

            public static Link Pop(Link? stack)
            {
                Link link;
                link = stack!.Value!;
                var previous = stack.Prev;
                Use(stack);
                return previous ?? link;
            }

            public static int Thrown(Link? link)
            {
                ArgumentNullException.ThrowIfNull(link);
                return link.Id;
            }

            public static int Text(string? text) => string.IsNullOrEmpty(text) ? 0 : text.Length;

            public static int First(Link?[]? links)
            {
                _ = links!.Length;
                return links[0]!.Id;
            }

            public static int Passed()
            {
                var root = new Link(2);
                Use(root);
                return root.Id;
            }

            public static int Guarded(Link? link)
            {
                if (link == null)
                    return 0;

                return link.Id + Use(link);
            }
        }
        """;

    [Test]
    public async Task ForgivenRead_IsAssertedWhereUsedAsNonNull()
    {
        var output = TranspileHelper.Transpile(Source);
        var stack = output["stack.ts"];

        await Assert.That(stack).Contains("link = stack!.value;");
        await Assert.That(stack).Contains("const previous = stack!.prev;");
        await Assert.That(stack).Contains("Stack.use(stack!);");
        await Assert.That(stack).Contains("return link!.id;");
        await Assert.That(stack).Contains("text!.length");
        await Assert.That(stack).Contains("links![0]");
        await Assert.That(output["holder.ts"]).Contains("return this.head!.id;");
    }

    [Test]
    public async Task ReadNarrowedByANullTestOrPassedAround_StaysUnasserted()
    {
        var stack = TranspileHelper.Transpile(Source)["stack.ts"];

        await Assert.That(stack).Contains("return (link.id + Stack.use(link)) | 0;");
        await Assert.That(stack).Contains("return root.id;");
    }

    [Test]
    public async Task ForgivenReads_TypeCheckUnderStrict()
    {
        var output = TranspileHelper.Transpile(Source);

        var (exitCode, log) = await StrictTypeCheck.Run(new Dictionary<string, string>(), output);

        await Assert.That(log).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task Dart_KeepsOnlyTheAssertionsItsFlowAnalysisCannotSee()
    {
        // Dart promotes a local after `x!`, and its `!` throws at run time on a
        // field whose state a call changed (C# keeps the field's state across
        // the call): only a local proven by a call's nullable attributes keeps
        // the assertion.
        var (files, _) = DartTranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public sealed class Node
            {
                public Node? Next;
                public int Id;
            }

            [Transpile]
            public sealed class Queue
            {
                private Node? _head;

                public static int Peek(Node node) => node.Id;

                public int Drain()
                {
                    var first = Peek(_head!);
                    _head = null;
                    return first;
                }

                public int Again()
                {
                    var first = Peek(_head!);
                    return first + _head.Id;
                }

                public static int Local(Node? node)
                {
                    var id = node!.Id;
                    return id + node.Id;
                }

                public static int Thrown(Node? node)
                {
                    ArgumentNullException.ThrowIfNull(node);
                    return node.Id;
                }
            }
            """
        );

        var queue = files["queue.dart"];

        await Assert.That(queue).Contains("return first + this._head.id;");
        await Assert.That(queue).Contains("return id + node.id;");
        await Assert.That(queue).Contains("return node!.id;");
    }
}
