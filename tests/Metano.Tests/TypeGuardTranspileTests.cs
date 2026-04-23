namespace Metano.Tests;

public class TypeGuardTranspileTests
{
    [Test]
    public async Task Record_GeneratesGuardWithTypeofChecks()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public record Point(int X, int Y);
            """
        );

        var output = result["point.ts"];
        await Assert
            .That(output)
            .Contains("export function isPoint(value: unknown): value is Point");
        await Assert.That(output).Contains("value instanceof Point");
        await Assert.That(output).Contains("typeof v.x === \"number\"");
        await Assert.That(output).Contains("typeof v.y === \"number\"");
    }

    [Test]
    public async Task StringEnum_GeneratesLiteralUnionGuard()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, StringEnum, GenerateGuard]
            public enum Currency
            {
                [Name("BRL")] Brl,
                [Name("USD")] Usd,
                [Name("EUR")] Eur,
            }
            """
        );

        var output = result["currency.ts"];
        await Assert
            .That(output)
            .Contains("export function isCurrency(value: unknown): value is Currency");
        await Assert.That(output).Contains("value === \"BRL\"");
        await Assert.That(output).Contains("value === \"USD\"");
        await Assert.That(output).Contains("value === \"EUR\"");
    }

    [Test]
    public async Task NumericEnum_GeneratesValueCheckGuard()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public enum Status { Active, Inactive }
            """
        );

        var output = result["status.ts"];
        await Assert
            .That(output)
            .Contains("export function isStatus(value: unknown): value is Status");
        await Assert.That(output).Contains("typeof value === \"number\"");
        await Assert.That(output).Contains("value === 0");
        await Assert.That(output).Contains("value === 1");
    }

    [Test]
    public async Task Interface_GeneratesShapeGuardWithoutInstanceof()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public interface IEntity
            {
                string Id { get; }
                string Name { get; }
            }
            """
        );

        var output = result["i-entity.ts"];
        await Assert
            .That(output)
            .Contains("export function isIEntity(value: unknown): value is IEntity");
        await Assert.That(output).DoesNotContain("instanceof");
        await Assert.That(output).Contains("typeof v.id === \"string\"");
        await Assert.That(output).Contains("typeof v.name === \"string\"");
    }

    [Test]
    public async Task Record_WithTranspiledField_CallsNestedGuard()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile, StringEnum, GenerateGuard]
                public enum Currency { Brl, Usd }

                [Transpile, GenerateGuard]
                public record Money(int Cents, Currency Currency);
            }
            """
        );

        var moneyOutput = result["money.ts"];
        await Assert.That(moneyOutput).Contains("isCurrency(v.currency)");
        await Assert.That(moneyOutput).Contains("isCurrency");
    }

    [Test]
    public async Task NullableField_AcceptsNullOrInnerType()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile, GenerateGuard]
            public record Profile(string Name, string? Bio);
            """
        );

        var output = result["profile.ts"];
        await Assert.That(output).Contains("isProfile");
        await Assert.That(output).Contains("v.bio == null");
    }

    [Test]
    public async Task InheritedRecord_GuardChecksAllFields()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Base(int Id);

                [Transpile, GenerateGuard]
                public record Child(int Id, string Name) : Base(Id);
            }
            """
        );

        var childOutput = result["child.ts"];
        await Assert.That(childOutput).Contains("isChild");
        await Assert.That(childOutput).Contains("typeof v.id === \"number\"");
        await Assert.That(childOutput).Contains("typeof v.name === \"string\"");
    }

    [Test]
    public async Task Exception_NoGuardEvenWithAttribute()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public sealed class MyError(string message) : System.Exception(message);
            """
        );

        var output = result["my-error.ts"];
        await Assert.That(output).DoesNotContain("isMyError");
    }

    [Test]
    public async Task WithoutAttribute_NoGuardGenerated()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Item(string Name);
            """
        );

        var output = result["item.ts"];
        await Assert.That(output).DoesNotContain("isItem");
    }

    [Test]
    public async Task Guard_HasTypePredicateReturnType()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public record Item(string Name);
            """
        );

        var output = result["item.ts"];
        await Assert.That(output).Contains("): value is Item");
    }

    [Test]
    public async Task Class_WithPublicField_GuardChecksField()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public class Counter
            {
                public int Count;
            }
            """
        );

        var output = result["counter.ts"];
        await Assert
            .That(output)
            .Contains("export function isCounter(value: unknown): value is Counter");
        await Assert.That(output).Contains("typeof v.count === \"number\"");
    }

    [Test]
    public async Task InheritedProtectedField_GuardChecksInheritedField()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public class Base
                {
                    protected bool _active = true;
                }

                [Transpile, GenerateGuard]
                public class Child : Base
                {
                    public int Count;
                }
            }
            """
        );

        var output = result["child.ts"];
        await Assert.That(output).Contains("typeof v._active === \"boolean\"");
        await Assert.That(output).Contains("typeof v.count === \"number\"");
    }

    [Test]
    public async Task Guard_WithPropertyAndField_ChecksBoth()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public class Sample
            {
                public int Count;
                public string Name { get; set; } = "";
            }
            """
        );

        var output = result["sample.ts"];
        await Assert.That(output).Contains("typeof v.count === \"number\"");
        await Assert.That(output).Contains("typeof v.name === \"string\"");
    }

    [Test]
    public async Task Guard_ForRenamedTypeImportedCrossFile_ResolvesViaTsName()
    {
        // A type with [Name(TS, "Ticker")] + [GenerateGuard] emits
        // `isTicker`. A sibling record whose own guard references the
        // aliased type's guard must import `isTicker` via its alias —
        // this exercises the dual-keying invariant on
        // IrCompilation.TranspilableTypes + GuardableTypeKeys that
        // TryResolveGuardImport relies on.
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile, StringEnum, GenerateGuard]
                [Name(TargetLanguage.TypeScript, "Ticker")]
                public enum Symbol { Brl, Usd }

                [Transpile, GenerateGuard]
                public record Quote(int Cents, Symbol Kind);
            }
            """
        );

        var quoteKey = result.Keys.Single(k => k.EndsWith("quote.ts", StringComparison.Ordinal));
        var quote = result[quoteKey];
        await Assert.That(quote).Contains("isTicker");
        await Assert.That(quote).Contains("Ticker");
    }

    // ─── assertT throwing variant ────────────────────────────

    [Test]
    public async Task Record_EmitsAssertCompanionAlongsideIs()
    {
        // Every [GenerateGuard] type now emits the throwing assertT
        // companion — wraps isT, throws TypeError when the check fails.
        // Kept inline (no runtime helper import) so guards stay zero-dep
        // and tree-shakable per ADR-0009's accepted trade-offs.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public record Point(int X, int Y);
            """
        );

        var output = result["point.ts"];
        await Assert
            .That(output)
            .Contains("export function assertPoint(value: unknown, message?: string)");
        await Assert.That(output).Contains(": asserts value is Point");
        await Assert.That(output).Contains("if (!isPoint(value))");
        await Assert.That(output).Contains("throw new TypeError");
        await Assert.That(output).Contains("\"Value is not a Point\"");
    }

    [Test]
    public async Task StringEnum_EmitsAssertCompanion()
    {
        // The assertT path must work uniformly across every guardable
        // kind — enums, interfaces, records — because the companion
        // builder runs after the predicate builder has produced isT
        // regardless of the shape.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, StringEnum, GenerateGuard]
            public enum Currency { Brl, Usd }
            """
        );

        var output = result["currency.ts"];
        await Assert.That(output).Contains("export function assertCurrency");
        await Assert.That(output).Contains(": asserts value is Currency");
        await Assert.That(output).Contains("if (!isCurrency(value))");
    }

    [Test]
    public async Task Interface_EmitsAssertCompanion()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public interface IGreeting
            {
                string Message { get; }
            }
            """
        );

        var output = result["i-greeting.ts"];
        await Assert.That(output).Contains("export function assertIGreeting");
        await Assert.That(output).Contains(": asserts value is IGreeting");
        await Assert.That(output).Contains("if (!isIGreeting(value))");
    }

    [Test]
    public async Task Assert_RenamedTypeKeepsTsNameInErrorMessage()
    {
        // [Name(TypeScript, "Ticker")] renames the emitted type AND the
        // guard — the assertT error message must use the TS name so
        // consumers see the name they'll encounter in the generated
        // module, not the C# symbol name.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            [Name(TargetLanguage.TypeScript, "Ticker")]
            public record Clock(string Id);
            """
        );

        var output = result["ticker.ts"];
        await Assert.That(output).Contains("export function assertTicker");
        await Assert.That(output).Contains("\"Value is not a Ticker\"");
    }

    [Test]
    public async Task Assert_AcceptsCallerSuppliedMessage()
    {
        // The message parameter is optional; when supplied, it overrides
        // the default via the `??` coalesce so callers can inject
        // context-specific failure messages (e.g., "request body invalid:
        // expected TodoPatch").
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public record Widget(int Id);
            """
        );

        var output = result["widget.ts"];
        await Assert.That(output).Contains("message ?? \"Value is not a Widget\"");
    }

    [Test]
    public async Task Assert_NotEmittedForException()
    {
        // Exception types don't get isT (ADR-0009: exceptions are thrown,
        // not guarded). The assertT companion inherits that — no assertT
        // emits for exception types either.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, GenerateGuard]
            public class AppError : Exception
            {
                public AppError(string message) : base(message) {}
            }
            """
        );

        var output = result["app-error.ts"];
        await Assert.That(output).DoesNotContain("isAppError");
        await Assert.That(output).DoesNotContain("assertAppError");
    }

    // ─── [Discriminator] short-circuit guard ─────────────────

    [Test]
    public async Task Discriminator_ShortCircuitsOnNamedField()
    {
        // [Discriminator("Kind")] tags `Kind` as the narrowing field.
        // Convention: expected value is the type's TS name
        // (Circle ↔ "Circle"). Generated guard checks
        // `v.kind !== "Circle"` before walking the remaining shape so
        // a mismatched body exits without traversing every field.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, StringEnum]
            public enum ShapeKind { Circle, Square }

            [Transpile, GenerateGuard]
            [Discriminator("Kind")]
            public record Circle(ShapeKind Kind, double Radius);
            """
        );

        var output = result["circle.ts"];
        await Assert.That(output).Contains("if (v.kind !== \"Circle\")");
        await Assert.That(output).Contains("return false");
    }

    [Test]
    public async Task Discriminator_RemovesSelfFromFieldChecks()
    {
        // The discriminator narrows the field by literal comparison, so
        // the generated guard must skip the redundant isKind(v.kind)
        // call that GetAllFieldsForGuard would otherwise emit — avoids
        // a double-check plus the extra import it would pull in.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, StringEnum]
            public enum ShapeKind { Circle, Square }

            [Transpile, GenerateGuard]
            [Discriminator("Kind")]
            public record Circle(ShapeKind Kind, double Radius);
            """
        );

        var output = result["circle.ts"];
        await Assert.That(output).DoesNotContain("isShapeKind(v.kind)");
        // radius is still validated
        await Assert.That(output).Contains("typeof v.radius === \"number\"");
    }

    [Test]
    public async Task Discriminator_HonorsNameOverride()
    {
        // [Name(TypeScript, "Round")] renames the emitted type. The
        // discriminator expected value tracks the TS name so consumer
        // code keyed by the renamed variant continues to narrow — the
        // check emits `v.kind !== "Round"`.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, StringEnum]
            public enum ShapeKind { [Name("Round")] Round, Square }

            [Transpile, GenerateGuard]
            [Name(TargetLanguage.TypeScript, "Round")]
            [Discriminator("Kind")]
            public record Circle(ShapeKind Kind, double Radius);
            """
        );

        var output = result["round.ts"];
        await Assert.That(output).Contains("if (v.kind !== \"Round\")");
    }

    [Test]
    public async Task Discriminator_MissingField_EmitsMs0011()
    {
        // [Discriminator("Kind")] must refer to an existing property.
        // If the name is wrong (typo, removed field), the frontend
        // validator raises MS0011 pointing at the offending type.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, GenerateGuard]
            [Discriminator("DoesNotExist")]
            public record Circle(double Radius);
            """
        );

        var ms0011 = diagnostics.FirstOrDefault(d =>
            d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.InvalidDiscriminator
        );
        await Assert.That(ms0011).IsNotNull();
        await Assert.That(ms0011!.Message).Contains("DoesNotExist");
        await Assert.That(ms0011.Message).Contains("Circle");
    }

    [Test]
    public async Task Discriminator_NonStringEnumField_EmitsMs0011()
    {
        // [Discriminator] requires the referenced field to carry
        // [StringEnum] so the narrowing compiles to a literal
        // comparison. Numeric enums (or any non-string-valued type)
        // raise MS0011.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [Transpile]
            public enum ShapeKind { Circle, Square }

            [Transpile, GenerateGuard]
            [Discriminator("Kind")]
            public record Circle(ShapeKind Kind, double Radius);
            """
        );

        var ms0011 = diagnostics.FirstOrDefault(d =>
            d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.InvalidDiscriminator
        );
        await Assert.That(ms0011).IsNotNull();
        await Assert.That(ms0011!.Message).Contains("not a [StringEnum]");
    }

    [Test]
    public async Task Discriminator_NullableField_EmitsMs0011()
    {
        // The discriminant must be present on every instance so the
        // guard can narrow without a null check of its own. Nullable
        // discriminators raise MS0011.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            #nullable enable
            using Metano.Annotations.TypeScript;

            [Transpile, StringEnum]
            public enum ShapeKind { Circle, Square }

            [Transpile, GenerateGuard]
            [Discriminator("Kind")]
            public record Circle(ShapeKind? Kind, double Radius);
            """
        );

        var ms0011 = diagnostics.FirstOrDefault(d =>
            d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.InvalidDiscriminator
        );
        await Assert.That(ms0011).IsNotNull();
        await Assert.That(ms0011!.Message).Contains("nullable");
    }
}
