import { HashCode } from "metano-runtime";
import { Temporal } from "@js-temporal/polyfill";
import { UserId } from "#/shared-kernel";

export class Comment {
  constructor(readonly authorId: UserId, readonly message: string, readonly createdAt: Temporal.ZonedDateTime, readonly isSystem: boolean = false) { }

  static system(message: string, createdAt: Temporal.ZonedDateTime): Comment {
    return new Comment(UserId.system(), message, createdAt, true);
  }

  equals(other: any): boolean {
    return other instanceof Comment && this.authorId === other.authorId && this.message === other.message && this.createdAt === other.createdAt && this.isSystem === other.isSystem;
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
    return new Comment(overrides?.authorId ?? this.authorId, overrides?.message ?? this.message, overrides?.createdAt ?? this.createdAt, overrides?.isSystem ?? this.isSystem);
  }
}
