using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Tests;

/// <summary>
/// The barrel-scope claim pass of the module-function fast paths (spec 013
/// FR-032) over the types it walks and the names it reserves: every static
/// class the transformer flattens claims its dispatched groups, whatever
/// extension member made it a module, and pass 1 reserves the names other
/// bindings already hold — an <c>[Import]</c> facade's binding in any static
/// class, and the module variables a containerless class declares.
/// </summary>
public class ModuleFunctionClaimGoldenTests
{
    /// <summary>
    /// A static class whose only extension is <c>[Ignore]</c>d, <c>[Emit]</c>
    /// or non-public still emits as a module, so its dispatched group gets a
    /// barrel claim: two fast paths and a dispatcher, never MS0001 and two
    /// <c>export function plain</c>.
    /// </summary>
    [Test]
    public async Task HostsWithoutAnEmittedExtension_ClaimTheirGroups()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            public static class IgnHost
            {
                [Ignore]
                public static int Twice(this int x) => x * 2;

                public static int Plain(int x) => x;

                public static int Plain(string x) => 0;
            }

            public static class EmHost
            {
                [Emit("($0 * 3)")]
                public static int Thrice(this int x) => x * 3;

                public static int Lone(int x) => x;

                public static int Lone(string x) => 0;
            }

            public static class PrivHost
            {
                internal static int Quad(this int x) => x * 4;

                public static int Hidden(int x) => x;

                public static int Hidden(string x) => 0;
            }

            public class Use
            {
                public int Go() => 3.Thrice() + IgnHost.Plain(1) + EmHost.Lone(1) + PrivHost.Hidden(1);
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await Assert
            .That(result["ign-host.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("module-claims-ignored-extension-host.ts"));
        await Assert
            .That(result["em-host.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("module-claims-emit-extension-host.ts"));
        await Assert
            .That(result["priv-host.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("module-claims-private-extension-host.ts"));

        // The call sites name the claimed fast paths; the container qualifier
        // they keep is the pre-existing #554.
        await Assert
            .That(result["use.ts"])
            .Contains(
                "return (3 * 3) + IgnHost.plainInt32(1) + EmHost.loneInt32(1) "
                    + "+ PrivHost.hiddenInt32(1);"
            );
    }

    /// <summary>
    /// A static class holding only a C# 14 <c>extension(...)</c> block emits
    /// as a module, so its plain dispatched group claims its fast paths too.
    /// </summary>
    [Test]
    public async Task ExtensionBlockHost_ClaimsItsGroup()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            public static class BlockOnly
            {
                extension(string s)
                {
                    public int Twice() => s.Length * 2;
                }

                public static int Plain(int x) => x;

                public static int Plain(string x) => 0;
            }

            public class Use
            {
                public int Go() => "x".Twice() + BlockOnly.Plain(1);
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await Assert
            .That(result["block-only.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("module-claims-extension-block-host.ts"));

        // The container qualifier on the plain call is the pre-existing #554.
        await Assert
            .That(result["use.ts"])
            .Contains("return twice(\"x\") + BlockOnly.plainInt32(1);");
    }

    /// <summary>
    /// An <c>[Import]</c> facade binds its name in the caller wherever it is
    /// declared — as the only extension of a static class, or as a plain
    /// static of a class that keeps its container — so pass 1 reserves it
    /// in every static type: <c>Foo(int x)</c> moves to <c>fooX$</c> with
    /// MS0067 and the <c>ext-lib</c> import survives.
    /// </summary>
    [Test]
    [Arguments("public static int FooX(this int x) => x;", "1.FooX()")]
    [Arguments("public static int FooX(int x) => x;", "ImpHost.FooX(1)")]
    public async Task ImportFacade_AnyStaticHost_ReservesItsBinding(string facade, string call)
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            public static class ImpHost
            {
                [Import("fooX", "ext-lib")]
                {{facade}}
            }

            [NoContainer]
            public static class Res
            {
                public static int Foo(int x) => x;

                public static int Foo(string s) => 0;
            }

            public class Use
            {
                public int Go() => {{call}} + Res.Foo(1) + Res.Foo("a");
            }
            """
        );

        var shadows = ShadowMessages(diagnostics);

        await Assert
            .That(shadows)
            .IsEquivalentTo([
                "Fast path 'Res.fooX' of 'Foo(int x)' is already taken in the barrel; it is "
                    + "emitted as 'fooX$' (spec 013 FR-032).",
            ]);
        await Assert
            .That(result["res.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("module-claims-import-host-res.ts"));
        await Assert
            .That(result["use.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("module-claims-import-host-use.ts"));
    }

    /// <summary>
    /// A containerless class declares its module state as top-level
    /// variables under their emitted names — a <c>[Name]</c> override
    /// verbatim, a constant under its C# name, a private field unexported
    /// but still bound in the file — so pass 1 reserves exactly those:
    /// <c>fooX</c> and <c>bazX</c> push their fast paths to <c>$</c>, while
    /// the <c>[Ignore]</c>d <c>BarX</c> and the constant <c>QuxX</c> leave
    /// <c>barX</c> and <c>quxX</c> free.
    /// </summary>
    [Test]
    public async Task ModuleState_ReservesTheDeclaredVariableNames()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Vars
            {
                [Name("fooX")]
                public static int Renamed = 1;

                [Ignore]
                public static int BarX = 2;

                private static int bazX = 3;

                public const int QuxX = 4;

                public static int Foo(int x) => x + Renamed;

                public static int Foo(string s) => 0;

                public static int Bar(int x) => x;

                public static int Bar(string s) => 0;

                public static int Baz(int x) => x + bazX;

                public static int Baz(string s) => 0;

                public static int Qux(int x) => x + QuxX;

                public static int Qux(string s) => 0;
            }
            """
        );

        var shadows = ShadowMessages(diagnostics);

        await Assert
            .That(shadows)
            .IsEquivalentTo([
                "Fast path 'Vars.fooX' of 'Foo(int x)' is already taken in the barrel; it is "
                    + "emitted as 'fooX$' (spec 013 FR-032).",
                "Fast path 'Vars.bazX' of 'Baz(int x)' is already taken in the barrel; it is "
                    + "emitted as 'bazX$' (spec 013 FR-032).",
            ]);
        await Assert
            .That(result["vars.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("module-claims-module-state.ts"));
    }

    /// <summary>
    /// The facade rewriter and the claim pass read one definition of the
    /// name a facade binds (<see cref="SymbolHelper.ImportBindingOf"/>), so
    /// even a reserved-word name — not escaped yet, #555 — is reserved
    /// exactly as the import binds it.
    /// </summary>
    [Test]
    public async Task ImportFacade_ReservedWordName_ReservesTheBindingTheImportUses()
    {
        const string source = """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Res
            {
                [Import("remove", "ext-lib")]
                public static int Delete(int x) => x;
            }

            public class Use
            {
                public int Go() => Res.Delete(1);
            }
            """;

        var facade = TranspileHelper
            .CompileLibrary(source)
            .GetTypeByMetadataName("Res")!
            .GetMembers("Delete")
            .OfType<IMethodSymbol>()
            .Single();

        var binding = facade.ImportBindingOf(TargetLanguage.TypeScript);
        var result = TranspileHelper.Transpile(source);

        await Assert.That(binding).IsEqualTo("delete");
        await Assert
            .That(result["use.ts"])
            .Contains($"import {{ remove as {binding} }} from \"ext-lib\";");
    }

    private static IReadOnlyList<string> ShadowMessages(
        IReadOnlyList<MetanoDiagnostic> diagnostics
    ) =>
        diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.OverloadNameShadowed)
            .Select(diagnostic => diagnostic.Message)
            .ToList();
}
