namespace Metano.Tests;

/// <summary>
/// Phase B (#31) coverage: lambdas tagged for expression-tree capture
/// emit a <c>QueryableMeta</c> object literal alongside the closure
/// inside the <c>linq(...)</c> pipe call. The trigger today is an
/// <c>IQueryable&lt;T&gt;</c> receiver — calls resolve to
/// <c>System.Linq.Queryable</c> whose lambda parameters are typed as
/// <c>Expression&lt;Func&lt;…&gt;&gt;</c>. Bodies that fall outside the
/// MVP subset stay opaque (no meta emitted) so the closure path keeps
/// working.
/// </summary>
public class QueryableExpressionTreeTests
{
    [Test]
    public async Task IQueryableReceiver_EmitsExpressionTreeMeta()
    {
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> Adults(IQueryable<User> users) =>
                    users.Where(u => u.Age >= 18);
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        await Assert.That(output).Contains("linq(");
        await Assert.That(output).Contains("where(");
        await Assert.That(output).Contains("tree:");
        await Assert.That(output).Contains("\"binary\"");
        await Assert.That(output).Contains("\"member\"");
    }

    [Test]
    public async Task PlainEnumerable_NoQueryableTrigger_NoMeta()
    {
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> Adults(this IEnumerable<User> users) =>
                    users.Where(u => u.Age >= 18);
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        await Assert.That(output).Contains("linq(");
        await Assert.That(output).Contains("where(");
        await Assert.That(output).DoesNotContain("tree:");
    }

    [Test]
    public async Task CapturedLocal_EmitsCaptureNodeAndCapturesBundle()
    {
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> AdultsAtLeast(IQueryable<User> users, int minAge)
                {
                    int threshold = minAge;
                    return users.Where(u => u.Age >= threshold);
                }
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        await Assert.That(output).Contains("\"capture\"");
        await Assert.That(output).Contains("captures:");
        await Assert.That(output).Contains("threshold:");
    }

    [Test]
    public async Task CompositeBoolean_EmitsNestedBinaryTree()
    {
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> ActiveAdults(IQueryable<User> users) =>
                    users.Where(u => u.Age >= 18 && u.Active);
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
                public bool Active { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        await Assert.That(output).Contains("tree:");
        await Assert.That(output).Contains("\"&&\"");
    }

    [Test]
    public async Task AsQueryableLift_TriggersMetaOnEnumerableSource()
    {
        // Calling AsQueryable() on a plain IEnumerable lifts the chain
        // into Queryable.Where (Expression<Func<…>> parameter), which
        // fires the queryable trigger even though the original source
        // was a plain Enumerable.
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> Adults(IEnumerable<User> users) =>
                    users.AsQueryable().Where(u => u.Age >= 18);
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        await Assert.That(output).Contains("tree:");
    }

    [Test]
    public async Task ExpressionDelegateParameter_OnQueryable_TriggersMeta()
    {
        // System.Linq.Queryable.Where takes Expression<Func<T,bool>> —
        // covers the parameter-type trigger even if the receiver type
        // detection happened to miss IQueryable<T>.
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IQueryable<User> Adults(IQueryable<User> users) =>
                    users.Where(u => u.Age >= 18);
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        await Assert.That(output).Contains("tree:");
        await Assert.That(output).Contains("\">=\"");
    }

    [Test]
    public async Task UnsupportedSyntax_KeepsClosureWithoutMeta()
    {
        // Object creation lives outside the Phase B MVP subset
        // (param/capture/literal/member/call/binary/unary/conditional).
        // The lambda still compiles into a C# expression tree, but the
        // walker bails and the call site keeps the closure-only form.
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> Doubled(IQueryable<User> users) =>
                    users.Select(u => new User { Age = u.Age + 1 });
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        await Assert.That(output).Contains("select(");
        await Assert.That(output).DoesNotContain("tree:");
    }

    [Test]
    public async Task ImplicitOptIn_IQueryableReceiver_UnsupportedBody_StaysSilent()
    {
        // IQueryable<T> receiver alone is implicit — the user did
        // not necessarily ask the provider to handle every body
        // shape. Status-quo silent bail preserved (no MS0024).
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> Doubled(IQueryable<User> users) =>
                    users.Select(u => new User { Age = u.Age + 1 });
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0024")).IsFalse();
        await Assert.That(files["user-ext.ts"]).DoesNotContain("tree:");
    }

    [Test]
    public async Task Walker_ExplicitOptIn_UnsupportedBody_RaisesMS0024()
    {
        // Drive the walker directly with isExplicitOptIn=true so we
        // exercise the diagnostic path independent of which call-site
        // signal the trigger detection picks up. End-to-end coverage
        // for explicit opt-in via [Queryable] / Expression<Func<…>>
        // on arbitrary invocations lives in the
        // CustomMethod_* tests (#218); this unit test pins the
        // walker contract directly.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Bag { public int Value { get; set; } }

            public static class Probe
            {
                public static System.Linq.Expressions.Expression<System.Func<Bag, Bag>> Lambda() =>
                    b => new Bag { Value = b.Value + 1 };
            }
            """;

        var compilation = Metano.Tests.IR.IrTestHelper.Compile(source);
        var diagnostics = new List<Metano.Compiler.Diagnostics.MetanoDiagnostic>();
        var syntaxTree = compilation.SyntaxTrees.First();
        var lambda = syntaxTree
            .GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LambdaExpressionSyntax>()
            .First();
        var model = compilation.GetSemanticModel(syntaxTree);
        using (Metano.Compiler.Extraction.QueryableExtractionDiagnostics.Open(diagnostics.Add))
        {
            var walker = new Metano.Compiler.Extraction.IrExpressionTreeExtractor(
                model,
                originResolver: null,
                target: null,
                valueExtractor: new Metano.Compiler.Extraction.IrExpressionExtractor(model),
                isExplicitOptIn: true
            );
            var meta = walker.TryExtract(lambda);
            await Assert.That(meta).IsNull();
        }

        await Assert.That(diagnostics.Any(d => d.Code == "MS0024")).IsTrue();
    }

    [Test]
    public async Task ParamType_LocalNamedType_EmitsStructuredNameWithoutOrigin()
    {
        // Local types contribute `{ name: "User" }` with no `from` —
        // providers can dispatch on the bare identifier when the type
        // lives in the same package.
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> Adults(IQueryable<User> users) =>
                    users.Where(u => u.Age >= 18);
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        await Assert.That(output).Contains("type: { name: \"User\" }");
        await Assert.That(output).DoesNotContain("from:");
    }

    [Test]
    public async Task LiteralType_Primitive_EmitsStructuredPrimitiveName()
    {
        // Primitives lower to their TS surface name (`number`) carried
        // under `name`, with no origin — same shape as local named
        // types.
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> Adults(IQueryable<User> users) =>
                    users.Where(u => u.Age >= 18);
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        await Assert.That(output).Contains("type: { name: \"number\" }");
    }

    [Test]
    public async Task ParamType_CrossPackageType_EmitsStructuredNameAndOrigin()
    {
        // A queryable lambda whose parameter type lives in a referenced
        // assembly carries `{ name, from }` so providers can
        // disambiguate same-named types across packages.
        var library = """
            using Metano.Annotations;
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib-pkg")]

            namespace LibNs;

            public class Product
            {
                public int Stock { get; set; }
            }
            """;

        var consumer = """
            using System.Collections.Generic;
            using System.Linq;
            using LibNs;

            namespace ConsumerNs;

            [Transpile]
            public static class ProductExt
            {
                public static IEnumerable<Product> InStock(IQueryable<Product> products) =>
                    products.Where(p => p.Stock >= 1);
            }
            """;

        var result = TranspileHelper.TranspileWithLibrary(library, consumer);
        var output = result.Values.Single(f => f.Contains("inStock"));
        await Assert.That(output).Contains("type: { name: \"Product\", from: \"lib-pkg\" }");
    }

    [Test]
    public async Task CustomMethod_WithQueryableAttribute_UnsupportedBody_RaisesMS0024()
    {
        // #218: A non-LINQ method with [Queryable] on a Func<> parameter
        // is an explicit opt-in. The walker fires from the general
        // invocation path (not the LINQ-chain path) and surfaces MS0024
        // when the lambda body falls outside the MVP subset.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;
            using Metano.Annotations;

            [Transpile]
            public class Bag { public int Value { get; set; } }

            [Transpile]
            public static class Probe
            {
                public static Bag Run([Queryable] Func<Bag, Bag> f, Bag b) => f(b);

                public static Bag Call(Bag input) =>
                    Run(b => new Bag { Value = b.Value + 1 }, input);
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0024")).IsTrue();
    }

    [Test]
    public async Task CustomMethod_WithExpressionFuncParam_UnsupportedBody_RaisesMS0024()
    {
        // #218: A non-LINQ method whose parameter is Expression<Func<…>>
        // is an explicit opt-in via the parameter type alone — no
        // [Queryable] attribute needed.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;
            using System.Linq.Expressions;

            [Transpile]
            public class Bag { public int Value { get; set; } }

            [Transpile]
            public static class Probe
            {
                public static Bag Run(Expression<Func<Bag, Bag>> f, Bag b) => f.Compile()(b);

                public static Bag Call(Bag input) =>
                    Run(b => new Bag { Value = b.Value + 1 }, input);
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0024")).IsTrue();
    }

    [Test]
    public async Task CustomMethod_WithQueryableAttribute_SupportedBody_NoDiagnostic()
    {
        // #218: Explicit opt-in via [Queryable] with a supported body —
        // walker succeeds, no MS0024.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;
            using Metano.Annotations;

            [Transpile]
            public class Bag { public int Value { get; set; } }

            [Transpile]
            public static class Probe
            {
                public static bool Run([Queryable] Func<Bag, bool> f, Bag b) => f(b);

                public static bool Call(Bag input) =>
                    Run(b => b.Value >= 18, input);
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0024")).IsFalse();
    }

    [Test]
    public async Task CustomMethod_NoQueryableSignal_UnsupportedBody_StaysSilent()
    {
        // #218: A plain Func<> parameter with no [Queryable] and no
        // Expression<Func<…>> signal is NOT an opt-in — even an
        // unsupported body must bail silently, no MS0024.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [Transpile]
            public class Bag { public int Value { get; set; } }

            [Transpile]
            public static class Probe
            {
                public static Bag Run(Func<Bag, Bag> f, Bag b) => f(b);

                public static Bag Call(Bag input) =>
                    Run(b => new Bag { Value = b.Value + 1 }, input);
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0024")).IsFalse();
    }

    [Test]
    public async Task Walker_ImplicitOptIn_UnsupportedBody_StaysSilent()
    {
        // Same shape as the explicit test but isExplicitOptIn=false —
        // walker bails silently, no MS0024.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Bag { public int Value { get; set; } }

            public static class Probe
            {
                public static System.Linq.Expressions.Expression<System.Func<Bag, Bag>> Lambda() =>
                    b => new Bag { Value = b.Value + 1 };
            }
            """;

        var compilation = Metano.Tests.IR.IrTestHelper.Compile(source);
        var diagnostics = new List<Metano.Compiler.Diagnostics.MetanoDiagnostic>();
        var syntaxTree = compilation.SyntaxTrees.First();
        var lambda = syntaxTree
            .GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LambdaExpressionSyntax>()
            .First();
        var model = compilation.GetSemanticModel(syntaxTree);
        using (Metano.Compiler.Extraction.QueryableExtractionDiagnostics.Open(diagnostics.Add))
        {
            var walker = new Metano.Compiler.Extraction.IrExpressionTreeExtractor(
                model,
                originResolver: null,
                target: null,
                valueExtractor: new Metano.Compiler.Extraction.IrExpressionExtractor(model),
                isExplicitOptIn: false
            );
            walker.TryExtract(lambda);
        }

        await Assert.That(diagnostics.Any(d => d.Code == "MS0024")).IsFalse();
    }
}
