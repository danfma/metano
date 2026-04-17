using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Tests.IR;

public class IrExpressionExtractionTests
{
    [Test]
    public async Task IntLiteral_ExtractsAsInt32()
    {
        var ir = ExtractFromMethod("int Get() => 42;");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var literal = ret.Value as IrLiteral;
        await Assert.That(literal).IsNotNull();
        await Assert.That(literal!.Kind).IsEqualTo(IrLiteralKind.Int32);
        await Assert.That(literal.Value).IsEqualTo(42);
    }

    [Test]
    public async Task StringLiteral_ExtractsAsString()
    {
        var ir = ExtractFromMethod("""string Get() => "hello";""");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var literal = ret.Value as IrLiteral;
        await Assert.That(literal!.Kind).IsEqualTo(IrLiteralKind.String);
        await Assert.That(literal.Value).IsEqualTo("hello");
    }

    [Test]
    public async Task TrueLiteral_ExtractsAsBoolean()
    {
        var ir = ExtractFromMethod("bool Get() => true;");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var literal = ret.Value as IrLiteral;
        await Assert.That(literal!.Kind).IsEqualTo(IrLiteralKind.Boolean);
        await Assert.That((bool)literal.Value!).IsTrue();
    }

    [Test]
    public async Task NullLiteral_ExtractsAsNull()
    {
        var ir = ExtractFromMethod("string? Get() => null;");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var literal = ret.Value as IrLiteral;
        await Assert.That(literal!.Kind).IsEqualTo(IrLiteralKind.Null);
    }

    [Test]
    public async Task BinaryAddition_ExtractsOp()
    {
        var ir = ExtractFromMethod("int Add() => 1 + 2;");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var bin = ret.Value as IrBinaryExpression;
        await Assert.That(bin).IsNotNull();
        await Assert.That(bin!.Operator).IsEqualTo(IrBinaryOp.Add);
    }

    [Test]
    public async Task Equality_ExtractsEqualOp()
    {
        var ir = ExtractFromMethod("bool Eq() => 1 == 2;");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var bin = (ret.Value as IrBinaryExpression)!;
        await Assert.That(bin.Operator).IsEqualTo(IrBinaryOp.Equal);
    }

    [Test]
    public async Task Identifier_ExtractsByName()
    {
        var ir = ExtractFromMethod("int Get() { int x = 1; return x; }");
        var ret = GetAt<IrReturnStatement>(ir, 1);
        var id = ret.Value as IrIdentifier;
        await Assert.That(id).IsNotNull();
        await Assert.That(id!.Name).IsEqualTo("x");
    }

    [Test]
    public async Task MemberAccess_ExtractsTargetAndName()
    {
        var ir = ExtractFromMethod("int Get() => this.field;", "public int field;");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var access = ret.Value as IrMemberAccess;
        await Assert.That(access).IsNotNull();
        await Assert.That(access!.MemberName).IsEqualTo("field");
        await Assert.That(access.Target).IsTypeOf<IrThisExpression>();
    }

    [Test]
    public async Task MemberAccess_PopulatesOriginWithDeclaringType()
    {
        var ir = ExtractFromMethod("int Get() => this.field;", "public int field;");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var access = (ret.Value as IrMemberAccess)!;
        await Assert.That(access.Origin).IsNotNull();
        await Assert.That(access.Origin!.MemberName).IsEqualTo("field");
        await Assert.That(access.Origin.IsStatic).IsFalse();
        await Assert.That(access.Origin.DeclaringTypeFullName).IsEqualTo("T");
    }

    [Test]
    public async Task Invocation_PopulatesOriginWithDeclaringTypeAndStaticness()
    {
        var ir = ExtractFromMethod(
            "int Call() => Add(1, 2);",
            "public static int Add(int a, int b) => a + b;"
        );
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var call = (ret.Value as IrCallExpression)!;
        await Assert.That(call.Origin).IsNotNull();
        await Assert.That(call.Origin!.MemberName).IsEqualTo("Add");
        await Assert.That(call.Origin.IsStatic).IsTrue();
        await Assert.That(call.Origin.DeclaringTypeFullName).IsEqualTo("T");
    }

    [Test]
    public async Task Invocation_OriginUsesOpenGenericFullNameForClosedGeneric()
    {
        var ir = ExtractFromMethod(
            "void M(System.Collections.Generic.List<int> xs) { xs.Add(1); }"
        );
        var stmt = GetAt<IrExpressionStatement>(ir, 0);
        var call = (stmt.Expression as IrCallExpression)!;
        await Assert.That(call.Origin).IsNotNull();
        await Assert
            .That(call.Origin!.DeclaringTypeFullName)
            .IsEqualTo("System.Collections.Generic.List<T>");
        await Assert.That(call.Origin.MemberName).IsEqualTo("Add");
        await Assert.That(call.Origin.IsStatic).IsFalse();
    }

    [Test]
    public async Task Invocation_CapturesGenericTypeArguments()
    {
        var ir = ExtractFromMethod(
            "int Call() => Box<int>(42);",
            "public static T Box<T>(T v) => v;"
        );
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var call = (ret.Value as IrCallExpression)!;
        await Assert.That(call.TypeArguments).IsNotNull();
        await Assert.That(call.TypeArguments!).Count().IsEqualTo(1);
        await Assert.That(call.TypeArguments[0]).IsTypeOf<IrPrimitiveTypeRef>();
    }

    [Test]
    public async Task Invocation_ExtractsTargetAndArgs()
    {
        // Calling an instance method through the implicit-this shorthand: the
        // extractor promotes `Add` to `this.Add` so backends don't have to
        // reconstruct the elision.
        var ir = ExtractFromMethod("int Call() => Add(1, 2);", "int Add(int a, int b) => a + b;");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var call = (ret.Value as IrCallExpression)!;
        var target = (call.Target as IrMemberAccess)!;
        await Assert.That(target).IsNotNull();
        await Assert.That(target.Target).IsTypeOf<IrThisExpression>();
        await Assert.That(target.MemberName).IsEqualTo("Add");
        await Assert.That(call.Arguments).Count().IsEqualTo(2);
    }

    [Test]
    public async Task StaticInvocation_SynthesizesClassNameQualifier()
    {
        // Static methods called from within the same class without a
        // qualifier (`Add(…)`) must be promoted to `ClassName.Add(…)` in the
        // IR so backends don't have to rediscover the containing type — TS
        // and Dart both reject unqualified calls to sibling statics.
        var ir = ExtractFromMethod(
            "int Call() => Add(1, 2);",
            "static int Add(int a, int b) => a + b;"
        );
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var call = (ret.Value as IrCallExpression)!;
        var target = (call.Target as IrMemberAccess)!;
        await Assert.That(target).IsNotNull();
        await Assert.That(target.Target).IsTypeOf<IrTypeReference>();
        await Assert.That(target.MemberName).IsEqualTo("Add");
    }

    [Test]
    public async Task LocalIdentifier_StaysAsBareIdentifier()
    {
        var ir = ExtractFromMethod("int Get() { int x = 42; return x; }");
        var ret = GetAt<IrReturnStatement>(ir, 1);
        await Assert.That(ret.Value).IsTypeOf<IrIdentifier>();
    }

    [Test]
    public async Task IsNullPattern_ExtractsConstantPattern()
    {
        var ir = ExtractFromMethod("bool IsNull(object? x) => x is null;");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var isPattern = (ret.Value as IrIsPatternExpression)!;
        await Assert.That(isPattern.Pattern).IsTypeOf<IrConstantPattern>();
    }

    [Test]
    public async Task IsTypePattern_ExtractsTypePattern()
    {
        var ir = ExtractFromMethod("bool IsString(object o) => o is string;");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var isPattern = (ret.Value as IrIsPatternExpression)!;
        var typePat = isPattern.Pattern as IrTypePattern;
        await Assert.That(typePat).IsNotNull();
        await Assert.That(typePat!.DesignatorName).IsNull();
    }

    [Test]
    public async Task IsTypePatternWithDesignator_CapturesVariableName()
    {
        var ir = ExtractFromMethod("bool IsString(object o) => o is string s;");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var isPattern = (ret.Value as IrIsPatternExpression)!;
        var typePat = (isPattern.Pattern as IrTypePattern)!;
        await Assert.That(typePat.DesignatorName).IsEqualTo("s");
    }

    [Test]
    public async Task SwitchExpression_ExtractsArmsWithPatternsAndResults()
    {
        // A minimal switch expression with a constant arm, a discard arm, and
        // the arm's result is what the extractor must flatten into an
        // IrSwitchExpression. The scrutinee is the governing expression; the
        // patterns drop into the same IR hierarchy the `is` extractor uses.
        var ir = ExtractFromMethod(
            "string Describe(int n) => n switch { 0 => \"zero\", _ => \"many\" };"
        );
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var sw = (ret.Value as IrSwitchExpression)!;
        await Assert.That(sw.Arms.Count).IsEqualTo(2);
        await Assert.That(sw.Arms[0].Pattern).IsTypeOf<IrConstantPattern>();
        await Assert.That(sw.Arms[1].Pattern).IsTypeOf<IrDiscardPattern>();
    }

    [Test]
    public async Task WithExpression_ExtractsSourceAndAssignments()
    {
        // `source with { X = e }` should flatten into an IrWithExpression
        // carrying the left-hand member name and the right-hand value.
        var ir = ExtractFromMethod(
            """
            Foo Update(Foo f)
            {
                return f with { X = 1 };
            }
            """,
            "public record Foo(int X, int Y);"
        );
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var w = (ret.Value as IrWithExpression)!;
        await Assert.That(w.Assignments.Count).IsEqualTo(1);
        await Assert.That(w.Assignments[0].MemberName).IsEqualTo("X");
        await Assert.That(w.Assignments[0].Value).IsTypeOf<IrLiteral>();
    }

    [Test]
    public async Task NamedArgument_IsPreservedOnIrArgument()
    {
        // `new Foo(x, Priority: p)` should produce an IrArgument whose Name is
        // "Priority" so backends (Dart directly, TS after reordering) can
        // reconstruct the source-side intent.
        var ir = ExtractFromMethod(
            """
            Foo Make(int x, int p)
            {
                return new Foo(x, Priority: p);
            }
            """,
            "public record Foo(int X, int Priority);"
        );
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var ne = (ret.Value as IrNewExpression)!;
        await Assert.That(ne.Arguments.Count).IsEqualTo(2);
        await Assert.That(ne.Arguments[0].Name).IsNull();
        await Assert.That(ne.Arguments[1].Name).IsEqualTo("Priority");
    }

    [Test]
    public async Task PropertyPattern_ExtractsSubpatternsAndOptionalType()
    {
        // `p is Point { X: 0 }` should flatten into a property pattern whose
        // Subpatterns name each member with its nested pattern, and whose
        // Type filter is set to Point.
        var ir = ExtractFromMethod("bool AtYAxis(object p) => p is System.Drawing.Point { X: 0 };");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var isPattern = (ret.Value as IrIsPatternExpression)!;
        var prop = (isPattern.Pattern as IrPropertyPattern)!;
        await Assert.That(prop.Type).IsNotNull();
        await Assert.That(prop.Subpatterns.Count).IsEqualTo(1);
        await Assert.That(prop.Subpatterns[0].MemberName).IsEqualTo("X");
        await Assert.That(prop.Subpatterns[0].Pattern).IsTypeOf<IrConstantPattern>();
    }

    [Test]
    public async Task SwitchExpressionWithWhenClause_CapturesGuardExpression()
    {
        // The `when` clause lives next to the pattern in C# — the extractor
        // must route it into IrSwitchArm.WhenClause, not into the pattern.
        var ir = ExtractFromMethod(
            "string Classify(int n) => n switch { var x when x > 0 => \"pos\", _ => \"other\" };"
        );
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var sw = (ret.Value as IrSwitchExpression)!;
        await Assert.That(sw.Arms[0].WhenClause).IsNotNull();
        await Assert.That(sw.Arms[1].WhenClause).IsNull();
    }

    [Test]
    public async Task SimpleLambda_ExtractsAsLambdaExpression()
    {
        var ir = ExtractFromMethod("System.Func<int, int> Make() => x => x + 1;");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var lambda = (ret.Value as IrLambdaExpression)!;
        await Assert.That(lambda).IsNotNull();
        await Assert.That(lambda.Parameters).Count().IsEqualTo(1);
        await Assert.That(lambda.Parameters[0].Name).IsEqualTo("x");
        await Assert.That(lambda.Body).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ParenthesizedLambda_ExtractsMultiParam()
    {
        var ir = ExtractFromMethod("System.Func<int, int, int> Make() => (a, b) => a + b;");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var lambda = (ret.Value as IrLambdaExpression)!;
        await Assert.That(lambda.Parameters).Count().IsEqualTo(2);
        await Assert.That(lambda.Parameters[1].Name).IsEqualTo("b");
    }

    [Test]
    public async Task StringInterpolation_ExtractsTextAndExpressionParts()
    {
        var ir = ExtractFromMethod("""string Greet(string name) => $"hello {name}!";""");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var interp = (ret.Value as IrStringInterpolation)!;
        await Assert.That(interp.Parts).Count().IsEqualTo(3);
        await Assert.That(interp.Parts[0]).IsTypeOf<IrInterpolationText>();
        await Assert.That(interp.Parts[1]).IsTypeOf<IrInterpolationExpression>();
        await Assert.That(interp.Parts[2]).IsTypeOf<IrInterpolationText>();
    }

    [Test]
    public async Task ImplicitInstanceFieldReference_ExpandsToThisMemberAccess()
    {
        var ir = ExtractFromMethod("int Get() => field;", "public int field;");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var access = (ret.Value as IrMemberAccess)!;
        await Assert.That(access).IsNotNull();
        await Assert.That(access.Target).IsTypeOf<IrThisExpression>();
        await Assert.That(access.MemberName).IsEqualTo("field");
    }

    [Test]
    public async Task Assignment_ExtractsAsBinaryAssign()
    {
        var ir = ExtractFromMethod("void Set() { int x = 0; x = 5; }");
        // Statement 0: variable declaration. Statement 1: expression statement with assignment.
        var exprStmt = GetAt<IrExpressionStatement>(ir, 1);
        var bin = exprStmt.Expression as IrBinaryExpression;
        await Assert.That(bin).IsNotNull();
        await Assert.That(bin!.Operator).IsEqualTo(IrBinaryOp.Assign);
    }

    [Test]
    public async Task IfStatement_ExtractsConditionAndThen()
    {
        var ir = ExtractFromMethod("void M() { if (true) return; }");
        var ifs = GetAt<IrIfStatement>(ir, 0);
        await Assert.That(ifs.Condition).IsTypeOf<IrLiteral>();
        await Assert.That(ifs.Then).Count().IsEqualTo(1);
        await Assert.That(ifs.Else).IsNull();
    }

    [Test]
    public async Task VariableDeclaration_CarriesType()
    {
        var ir = ExtractFromMethod("void M() { int x = 42; }");
        var decl = GetAt<IrVariableDeclaration>(ir, 0);
        await Assert.That(decl.Name).IsEqualTo("x");
        await Assert.That(decl.Type).IsTypeOf<IrPrimitiveTypeRef>();
    }

    [Test]
    public async Task ObjectCreation_ExtractsTypeAndArgs()
    {
        var ir = ExtractFromMethod("T Get() => new T(1);", "public T(int x) { }");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var newExpr = ret.Value as IrNewExpression;
        await Assert.That(newExpr).IsNotNull();
        await Assert.That(newExpr!.Arguments).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ImplicitNew_ResolvesTargetType()
    {
        var ir = ExtractFromMethod("T Get() => new(1);", "public T(int x) { }");
        var ret = GetAt<IrReturnStatement>(ir, 0);
        var newExpr = ret.Value as IrNewExpression;
        await Assert.That(newExpr).IsNotNull();
        await Assert.That(newExpr!.Type).IsTypeOf<IrNamedTypeRef>();
    }

    [Test]
    public async Task ForEach_ExtractsVariableCollectionAndBody()
    {
        var ir = ExtractFromMethod(
            """
            void Loop(List<int> xs) {
                foreach (var x in xs) {
                    Console.WriteLine(x);
                }
            }
            """
        );
        var fe = GetAt<IrForEachStatement>(ir, 0);
        await Assert.That(fe.Variable).IsEqualTo("x");
        await Assert.That(fe.Body).Count().IsEqualTo(1);
    }

    [Test]
    public async Task While_ExtractsConditionAndBody()
    {
        var ir = ExtractFromMethod("void Loop() { while (true) break; }");
        var ws = GetAt<IrWhileStatement>(ir, 0);
        await Assert.That(ws.Condition).IsTypeOf<IrLiteral>();
        await Assert.That(ws.Body).Count().IsEqualTo(1);
    }

    [Test]
    public async Task TryCatch_ExtractsAllCatches()
    {
        var ir = ExtractFromMethod(
            """
            void Safe() {
                try {
                    DoWork();
                } catch (ArgumentException a) {
                    // handle
                } catch (Exception e) {
                    throw;
                }
            }
            """,
            extraMembers: "void DoWork() { }"
        );
        var ts = GetAt<IrTryStatement>(ir, 0);
        await Assert.That(ts.Catches).IsNotNull();
        await Assert.That(ts.Catches!).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Switch_ExtractsCasesAndDefault()
    {
        var ir = ExtractFromMethod(
            """
            string Classify(int x) {
                switch (x) {
                    case 0: return "zero";
                    case 1: return "one";
                    default: return "other";
                }
            }
            """
        );
        var sw = GetAt<IrSwitchStatement>(ir, 0);
        await Assert.That(sw.Cases).Count().IsEqualTo(3);
        await Assert.That(sw.Cases.Last().Labels).IsEmpty();
    }

    // -- helpers --

    private static IReadOnlyList<IrStatement> ExtractFromMethod(
        string method,
        string? extraMembers = null
    )
    {
        var csharp = $$"""
            public class T
            {
                {{extraMembers ?? ""}}
                {{method}}
            }
            """;
        var compilation = IrTestHelper.Compile(csharp);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();

        // The method under test is the last one — callers put the subject method last.
        var target = methods.Last();

        var isVoid = target.ReturnType is PredefinedTypeSyntax { Keyword.ValueText: "void" };
        var extractor = new IrStatementExtractor(model);
        return extractor.ExtractBody(target.Body, target.ExpressionBody, isVoid);
    }

    private static T GetAt<T>(IReadOnlyList<IrStatement> statements, int index)
        where T : IrStatement
    {
        var s = statements[index];
        if (s is not T typed)
            throw new InvalidOperationException(
                $"Expected statement #{index} to be {typeof(T).Name} but was {s.GetType().Name}."
            );
        return typed;
    }
}
