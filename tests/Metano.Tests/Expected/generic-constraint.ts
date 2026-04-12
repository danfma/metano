import { HashCode } from "metano-runtime";
import type { IEntity } from "./i-entity";

export class Repo<T extends IEntity> {
  constructor(readonly item: T) { }

  equals(other: any): boolean {
    return other instanceof Repo && this.item === other.item;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.item);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Repo<T>>): Repo<T> {
    return new Repo(overrides?.item ?? this.item);
  }
}
