/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import type { Temporal } from "@js-temporal/polyfill";
import { HashCode, copyRecordWith, valueEquals, type ClassType } from "metano-runtime";
import { UserId } from "../../shared-kernel/user-id.js";

let Comment$rt: ClassType<Comment> | undefined;

export class Comment {
  constructor(
    readonly authorId: UserId,
    readonly message: string,
    readonly createdAt: Temporal.ZonedDateTime,
    readonly isSystem: boolean = false,
  ) {}

  static get $runtimeType(): ClassType<Comment> {
    return (Comment$rt ??= {
      kind: "class",
      name: "Comment",
      fullName: "SampleIssueTracker.Issues.Domain.Comment",
      namespace: "SampleIssueTracker.Issues.Domain",
      ctor: Comment,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Comment => v instanceof Comment,
    });
  }

  static system(message: string, createdAt: Temporal.ZonedDateTime): Comment {
    return new Comment(UserId.system(), message, createdAt, true);
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.authorId === other.authorId &&
      this.message === other.message &&
      valueEquals(this.createdAt, other.createdAt) &&
      this.isSystem === other.isSystem
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.authorId);
    hc.add(this.message);
    hc.add(this.createdAt);
    hc.add(this.isSystem);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Comment>): Comment {
    return copyRecordWith<Comment>(this, overrides);
  }
}
