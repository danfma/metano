namespace Metano.Tests;

public class QueueStackTests
{
    [Test]
    public async Task QueueType_MapsToTheRuntimeQueue()
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
        await Assert.That(output).Contains("_items: Queue<string> = new Queue<string>()");
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
        await Assert.That(output).Contains("this._queue.enqueue(msg);");
        await Assert.That(output).Contains("return this._queue.dequeue();");
        await Assert.That(output).Contains("return this._queue.peek();");
        await Assert.That(output).Contains("return this._queue.contains(msg);");
    }

    [Test]
    public async Task StackType_MapsToTheRuntimeStack()
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
        await Assert.That(output).Contains("_items: Stack<string> = new Stack<string>()");
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
        await Assert.That(output).Contains("this._stack.push(action);");
        await Assert.That(output).Contains("return this._stack.pop();");
        await Assert.That(output).Contains("return this._stack.peek();");
    }
}
