namespace MetaSharp.Tests;

public class QueueStackTests
{
    [Test]
    public async Task QueueType_MapsToArray()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Buffer
            {
                private readonly Queue<string> _items = new();
                public Buffer() { }
            }
            """
        );

        var output = result["buffer.ts"];
        await Assert.That(output).Contains("_items: string[]");
    }

    [Test]
    public async Task QueueMethods_MapCorrectly()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class MessageQueue
            {
                private readonly Queue<string> _queue = new();

                public MessageQueue() { }

                public void Add(string msg) { _queue.Enqueue(msg); }
                public string Take() { return _queue.Dequeue(); }
                public string Peek() { return _queue.Peek(); }
                public bool Has(string msg) { return _queue.Contains(msg); }
            }
            """
        );

        var output = result["message-queue.ts"];
        await Assert.That(output).Contains(".push(msg)");
        await Assert.That(output).Contains(".shift()");
        await Assert.That(output).Contains("[0]");
        await Assert.That(output).Contains(".includes(msg)");
    }

    [Test]
    public async Task StackType_MapsToArray()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class History
            {
                private readonly Stack<string> _items = new();
                public History() { }
            }
            """
        );

        var output = result["history.ts"];
        await Assert.That(output).Contains("_items: string[]");
    }

    [Test]
    public async Task StackMethods_MapCorrectly()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class UndoStack
            {
                private readonly Stack<string> _stack = new();

                public UndoStack() { }

                public void Push(string action) { _stack.Push(action); }
                public string Pop() { return _stack.Pop(); }
                public string Peek() { return _stack.Peek(); }
            }
            """
        );

        var output = result["undo-stack.ts"];
        await Assert.That(output).Contains(".push(action)");
        await Assert.That(output).Contains(".pop()");
        // Stack.Peek → arr[arr.length - 1]
        await Assert.That(output).Contains(".length - 1]");
    }
}
