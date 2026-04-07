namespace MetaSharp.Tests;

public class MethodOverloadFastPathTests
{
    [Test]
    public async Task FastPath_GeneratesPrivateSpecializedMethods()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Calculator
            {
                public int Add(int x, int y) { return x + y; }
                public int Add(int x, int y, int z) { return x + y + z; }
            }
            """
        );

        var output = result["calculator.ts"];
        // Fast-path methods are private with name + capitalized param names
        await Assert.That(output).Contains("private addXY(x: number, y: number): number");
        await Assert.That(output).Contains("private addXYZ(x: number, y: number, z: number): number");
        // Public dispatcher delegates to fast-paths
        await Assert.That(output).Contains("add(...args: unknown[])");
        await Assert.That(output).Contains("this.addXY(args[0] as number, args[1] as number)");
        await Assert.That(output).Contains("this.addXYZ(args[0] as number, args[1] as number, args[2] as number)");
    }

    [Test]
    public async Task FastPath_DispatcherDelegatesInsteadOfDuplicating()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Greeter
            {
                public string Greet(string name) { return "Hello, " + name; }
                public string Greet(string name, int times) {
                    var result = "";
                    for (int i = 0; i < times; i++) result += name;
                    return result;
                }
            }
            """
        );

        var output = result["greeter.ts"];
        // The body "Hello, " + name should appear ONLY once (in the fast path)
        var helloOccurrences = output.Split("\"Hello, \"").Length - 1;
        await Assert.That(helloOccurrences).IsEqualTo(1);
    }

    [Test]
    public async Task FastPath_StaticMethodsDelegateViaClassName()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class MathHelper
            {
                public static int Add(int x, int y) { return x + y; }
                public static int Add(int x, int y, int z) { return x + y + z; }
            }
            """
        );

        var output = result["math-helper.ts"];
        // Static dispatcher uses ClassName.fastPath instead of this.fastPath
        await Assert.That(output).Contains("private static addXY");
        await Assert.That(output).Contains("MathHelper.addXY(args[0] as number, args[1] as number)");
    }

    [Test]
    public async Task FastPath_VoidMethods_DispatcherCallsThenReturns()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Logger
            {
                public void Log(string msg) { System.Console.WriteLine(msg); }
                public void Log(string msg, int level) { System.Console.WriteLine($"{level}: {msg}"); }
            }
            """
        );

        var output = result["logger.ts"];
        await Assert.That(output).Contains("private logMsg(msg: string): void");
        await Assert.That(output).Contains("private logMsgLevel(msg: string, level: number): void");
        // Void dispatcher branches: call fast-path then return
        await Assert.That(output).Contains("this.logMsg(args[0] as string)");
        await Assert.That(output).Contains("this.logMsgLevel(args[0] as string, args[1] as number)");
    }
}
