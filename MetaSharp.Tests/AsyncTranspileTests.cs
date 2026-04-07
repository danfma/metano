namespace MetaSharp.Tests;

public class AsyncTranspileTests
{
    [Test]
    public async Task AsyncStaticMethod_GeneratesAsyncFunction()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Fetcher(string Url)
            {
                public static async Task<string> Load(string url)
                {
                    return url;
                }
            }
            """
        );

        var output = result["fetcher.ts"];
        await Assert.That(output).Contains("static async load(url: string): Promise<string>");
        await Assert.That(output).Contains("return url;");
    }

    [Test]
    public async Task TaskReturnType_MapsToPromise()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Service(string Name)
            {
                public static async Task<int> GetCount()
                {
                    return 42;
                }
            }
            """
        );

        var output = result["service.ts"];
        await Assert.That(output).Contains("Promise<number>");
    }

    [Test]
    public async Task VoidTask_MapsToPromiseVoid()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Worker(string Id)
            {
                public static async Task Run()
                {
                    return;
                }
            }
            """
        );

        var output = result["worker.ts"];
        await Assert.That(output).Contains("Promise<void>");
    }

    [Test]
    public async Task AwaitExpression_TranspilesCorrectly()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Loader(string Url)
            {
                public static async Task<string> FetchTwice(
                    Task<string> source)
                {
                    var first = await source;
                    return first;
                }
            }
            """
        );

        var output = result["loader.ts"];
        await Assert.That(output).Contains("await source");
    }

    [Test]
    public async Task ValueTaskReturnType_MapsToPromise()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Cache(string Key)
            {
                public static async System.Threading.Tasks.ValueTask<int> Lookup()
                {
                    return 0;
                }
            }
            """
        );

        var output = result["cache.ts"];
        await Assert.That(output).Contains("Promise<number>");
    }

    [Test]
    public async Task VoidValueTask_MapsToPromiseVoid()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Sink(string Name)
            {
                public static async System.Threading.Tasks.ValueTask Flush()
                {
                    return;
                }
            }
            """
        );

        var output = result["sink.ts"];
        await Assert.That(output).Contains("Promise<void>");
    }
}
