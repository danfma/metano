/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { Temporal } from "@js-temporal/polyfill";
import { HashCode, copyRecordWith, valueEquals } from "metano-runtime";
import { UserId } from "#/sample-issue-tracker/shared-kernel/user-id";

export class Comment {
  constructor(
    readonly authorId: UserId,
    readonly message: string,
    readonly createdAt: Temporal.ZonedDateTime,
    readonly isSystem: boolean = false,
  ) {}

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
