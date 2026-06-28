namespace Metano.Tests;

/// <summary>
/// Pins import completeness for the companion-namespace lowering of an abstract
/// record with nested record variants (the <c>[StrictUnionGuard]</c> discriminated
/// union shape). Symbols referenced only inside the emitted <c>namespace</c> block —
/// an external type in a variant's constructor (the import walker recursing
/// <c>ns.Members</c>) and the <c>valueEquals</c> runtime helper a non-strict variant
/// field needs (<c>TypeTransformer.ScanTypeAndEmittedNested</c> recursing into the
/// nested variants it emits) — must be imported, otherwise the file fails to type-check
/// (feature 006).
/// </summary>
public class NestedRecordVariantImportTests
{
    // Mirrors the real Vigiata reproduction: a renamed DTO in its own file referenced
    // only inside a nested variant, plus a non-strict field forcing valueEquals.
    private const string UnionWithExternalTypeSource = """
        using Metano.Annotations;

        [assembly: TranspileAssembly]

        [Name("UserProfile")]
        public sealed record UserProfileDto(string Id);

        public abstract record GetUserProfileResponse
        {
            public sealed record Unauthorized : GetUserProfileResponse;
            public sealed record UserProfileLoaded(UserProfileDto UserProfile) : GetUserProfileResponse;
        }
        """;

    [Test]
    public async Task NestedVariant_ImportsExternalTypeReferencedOnlyInVariant()
    {
        var result = TranspileHelper.Transpile(UnionWithExternalTypeSource);

        var output = result["get-user-profile-response.ts"];
        // The variant `UserProfileLoaded(UserProfile)` references the renamed DTO in its
        // constructor — a symbol used ONLY inside the companion namespace. It must be imported.
        // Used purely as a type annotation, so a type-only import is the correct shape.
        await Assert.That(output).Contains("import type { UserProfile } from \"./user-profile\"");
    }

    [Test]
    public async Task NestedVariant_ImportsValueEqualsForNonStrictVariantField()
    {
        var result = TranspileHelper.Transpile(UnionWithExternalTypeSource);

        var output = result["get-user-profile-response.ts"];
        // The variant's synthesized `equals` calls valueEquals for the non-strict field —
        // the runtime helper must be imported alongside HashCode.
        await Assert
            .That(output)
            .Contains("import { HashCode, valueEquals } from \"metano-runtime\"");
        await Assert.That(output).Contains("valueEquals(this.userProfile, other.userProfile)");
    }

    [Test]
    public async Task NestedVariant_StrictOnlyField_DoesNotImportValueEquals()
    {
        // Negative control (FR-005): a variant whose only field is primitive must NOT pull
        // a spurious valueEquals import once the emitted nested variants are scanned.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            public abstract record Result
            {
                public sealed record Empty : Result;
                public sealed record Loaded(int Count) : Result;
            }
            """
        );

        var output = result["result.ts"];
        // Pair the negation with a positive that proves the strict variant WAS emitted with
        // a strict comparison, so the DoesNotContain can only fail for the real reason.
        await Assert.That(output).Contains("class Loaded");
        await Assert.That(output).Contains("this.count === other.count");
        await Assert.That(output).DoesNotContain("valueEquals");
    }

    [Test]
    public async Task NestedVariant_ImportMarkedVariant_DoesNotContributeRuntimeRequirement()
    {
        // Guards the [Import] hole: a nested variant marked [Import] is NOT emitted
        // (BuildTypeStatements skips it), so even though it carries a non-strict field it
        // must NOT pull a spurious valueEquals import into the parent file. The nested scan
        // applies the same [Import] skip the top-level driver does.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            public abstract record Outcome
            {
                public sealed record Local(int Count) : Outcome;

                [Import("External", from: "ext-lib")]
                public sealed record External(decimal Amount) : Outcome;
            }
            """
        );

        var output = result["outcome.ts"];
        await Assert.That(output).Contains("class Local");
        await Assert.That(output).DoesNotContain("valueEquals");
    }

    [Test]
    public async Task NestedVariant_ImportsCrossPackageTypeReferencedOnlyInVariant()
    {
        // Generality (US2): a variant field whose type comes from another package, used
        // nowhere else in the file, must still emit the cross-package import — the
        // ns.Members walk reuses the same origin-collection path as the top level.
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@scope/lib")]

            namespace MyLib.Domain;

            public record Money(decimal Amount, string Currency);
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public abstract record PaymentResult
            {
                public sealed record Pending : PaymentResult;
                public sealed record Settled(MyLib.Domain.Money Amount) : PaymentResult;
            }
            """;

        var result = TranspileHelper.TranspileWithLibrary(library, consumer);

        var output = result["app/payment-result.ts"];
        await Assert.That(output).Contains("Money } from \"@scope/lib/my-lib/domain\"");
    }

    [Test]
    public async Task NestedVariant_DeduplicatesRuntimeImportSharedWithTopLevel()
    {
        // SC-005: HashCode is needed by both the top-level abstract record and the
        // variants; valueEquals only by a variant. The file must carry exactly one
        // metano-runtime import line, not one per scan site.
        var result = TranspileHelper.Transpile(UnionWithExternalTypeSource);

        var output = result["get-user-profile-response.ts"];
        var runtimeImportCount = output.Split("from \"metano-runtime\"").Length - 1;
        await Assert.That(runtimeImportCount).IsEqualTo(1);
    }
}
