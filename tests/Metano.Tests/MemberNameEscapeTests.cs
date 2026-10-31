namespace Metano.Tests;

/// <summary>
/// Tests for the distinction between variable / parameter identifier escape rules
/// and class member declaration / property access escape rules. Reserved words can
/// appear in property position in JS (<c>obj.delete</c>, <c>class Foo { delete() {} }</c>)
/// but NOT as variable identifiers or namespace function names.
///
/// The compiler routes class methods + member-access call sites through a
/// non-escaping helper so the user can write a method named <c>Delete</c> in C# and
/// get <c>delete()</c> in TS without manually adding <c>[Name("delete")]</c>.
/// [InlineWrapper] types stay on the escaping path because they lower to namespace
/// function declarations, where reserved words are still illegal.
/// </summary>
public class MemberNameEscapeTests
{
    [Test]
    public async Task ClassMethodWithReservedName_NotEscaped()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Bag
            {
                public void Delete() { }
                public void New() { }
            }
            """
        );

        var output = result["bag.ts"];
        // Method declarations don't get the underscore.
        await Assert.That(output).Contains("delete():");
        await Assert.That(output).Contains("new():");
        await Assert.That(output).DoesNotContain("delete_");
        await Assert.That(output).DoesNotContain("new_");
    }

    [Test]
    public async Task ClassMethodCallSite_AlsoNotEscaped()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Bag
            {
                public void Delete() { }
            }

            [Transpile]
            public class User
            {
                public void Cleanup(Bag b) => b.Delete();
            }
            """
        );

        var output = result["user.ts"];
        await Assert.That(output).Contains("b.delete()");
        await Assert.That(output).DoesNotContain("b.delete_()");
    }

    [Test]
    public async Task InlineWrapper_StaticMethodNamed_New_StillEscaped()
    {
        // [InlineWrapper] types lower to namespace functions, where reserved words
        // are illegal even though `obj.new` is fine. Both the declaration and the
        // call site escape so they stay in agreement.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, InlineWrapper]
            public readonly record struct UserId(string Value)
            {
                public static UserId New() => new("x");
            }

            [Transpile]
            public class Service
            {
                public UserId Make() => UserId.New();
            }
            """
        );

        var idOutput = result["user-id.ts"];
        var svcOutput = result["service.ts"];
        // Declaration uses the escaped form because namespace `function new() {}`
        // is a parse error.
        await Assert.That(idOutput).Contains("function new_");
        // Call site matches.
        await Assert.That(svcOutput).Contains("UserId.new_()");
    }

    [Test]
    public async Task NameOverride_BypassesAllEscaping()
    {
        // [Name("delete")] writes the value verbatim — no escape, no camelCase.
        // This stays the user's escape hatch for properties / fields where the
        // declaration site can't safely use a reserved word.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Bag
            {
                [Name("delete")]
                public void Remove() { }
            }

            [Transpile]
            public class User
            {
                public void Cleanup(Bag b) => b.Remove();
            }
            """
        );

        await Assert.That(result["bag.ts"]).Contains("delete():");
        await Assert.That(result["user.ts"]).Contains("b.delete()");
    }

    // Data members escape reserved words at their declaration (a promoted
    // constructor parameter cannot be named `default`), so every read and
    // write of the member names the escaped property (#253). One fixture
    // covers each access shape on purpose: plain and optional-chain reads, a
    // second reserved word, a [PlainObject] read, and a self read/write.
    [Test]
    public async Task DataMemberWithReservedName_AccessUsesDeclaredEscapedName()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public record Settings(int Default, string Delete);

            [Transpile, PlainObject]
            public record Options(int Default);

            [Transpile]
            public class Reader
            {
                public int New { get; set; }

                public int Read(Settings s) => s.Default;
                public int? ReadOptional(Settings? s) => s?.Default;
                public string ReadDelete(Settings s) => s.Delete;
                public int ReadOptions(Options o) => o.Default;
                public void Write(int value) => New = value + this.New;
            }
            """
        );

        var output = result["reader.ts"];
        await Assert.That(output).Contains("new_: number = 0;");
        await Assert.That(output).Contains("return s.default_;");
        await Assert.That(output).Contains("return s?.default_ ?? null;");
        await Assert.That(output).Contains("return s.delete_;");
        await Assert.That(output).Contains("return o.default_;");
        await Assert.That(output).Contains("this.new_ = value + this.new_;");
        await Assert.That(output).DoesNotContain(".default;");
    }

    // Interface methods follow the class-method rule — no escaping — so an
    // implementing class and an interface-typed call site agree (#253).
    [Test]
    public async Task InterfaceMethodWithReservedName_NotEscaped()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IStore
            {
                int Default { get; }
                void Delete();
            }

            [Transpile]
            public class Store : IStore
            {
                public int Default => 1;
                public void Delete() { }
            }

            [Transpile]
            public class User
            {
                public int Cleanup(IStore s)
                {
                    s.Delete();
                    return s.Default;
                }
            }
            """
        );

        await Assert.That(result["i-store.ts"]).Contains("delete(): void;");
        await Assert.That(result["i-store.ts"]).Contains("readonly default_: number;");
        await Assert.That(result["store.ts"]).Contains("delete(): void");
        await Assert.That(result["user.ts"]).Contains("s.delete();");
        await Assert.That(result["user.ts"]).Contains("return s.default_;");
    }

    // A member declared outside the toolchain ([Import], BCL) keeps the name
    // its real declaration has — JS accepts `obj.default` (#253).
    [Test]
    public async Task ForeignDataMemberWithReservedName_NotEscaped()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [Import("Module", from: "some-lib")]
            public class Module
            {
                public object Default { get; } = null!;
            }

            [Transpile]
            public class Loader
            {
                public object Load(Module m) => m.Default;
            }
            """
        );

        await Assert.That(result["loader.ts"]).Contains("return m.default;");
    }

    // An [External] stub declares nothing the toolchain renames, so its
    // reserved-word property keeps its real name (#253).
    [Test]
    public async Task ExternalDataMemberWithReservedName_NotEscaped()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [External]
            public class Module
            {
                public object Default { get; } = null!;
            }

            [Transpile]
            public class Loader
            {
                public object Load(Module m) => m.Default;
            }
            """
        );

        await Assert.That(result["loader.ts"]).Contains("return m.default;");
    }

    // Static reads and compound writes name the escaped member too (#253).
    [Test]
    public async Task StaticAndCompoundAccessOfReservedName_UseEscapedName()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                public static int Default { get; set; }
                public int Case;

                public int Bump()
                {
                    Case += 2;
                    Case++;
                    return Counter.Default + Case;
                }
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).Contains("this.case_ += 2;");
        await Assert.That(output).Contains("this.case_++;");
        await Assert.That(output).Contains("return Counter.default_ + this.case_;");
    }

    // A pattern over a foreign type reads the member by its real name, like an
    // ordinary access (#253).
    [Test]
    public async Task PropertyPatternOnForeignReservedName_NotEscaped()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [Import("Module", from: "some-lib")]
            public class Module
            {
                public int Default { get; } = 0;
            }

            [Transpile]
            public record Settings(int Default);

            [Transpile]
            public class Loader
            {
                public bool IsDefault(Module m) => m is { Default: 1 };
                public bool IsOne(Settings s) => s is { Default: 1 };
            }
            """
        );

        var output = result["loader.ts"];
        await Assert.That(output).Contains("m.default === 1");
        await Assert.That(output).Contains("s.default_ === 1");
    }

    // A data member of a referenced transpiled package is declared escaped by
    // that package, so the consumer reads the escaped name (#253).
    [Test]
    public async Task CrossPackageDataMemberWithReservedName_UsesEscapedName()
    {
        var result = TranspileHelper.TranspileWithLibrary(
            """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("settings-lib")]

            public record Settings(int Default);
            """,
            """
            [Transpile]
            public class Reader
            {
                public int Read(Settings s) => s.Default;
            }
            """
        );

        await Assert.That(result["reader.ts"]).Contains("return s.default_;");
    }
}
