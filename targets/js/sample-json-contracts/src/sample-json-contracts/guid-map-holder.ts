/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, type UUID, copyRecordWith, type ClassType } from "metano-runtime";
import type { Dictionary } from "metano-runtime/system/collections";

let GuidMapHolder$rt: ClassType<GuidMapHolder> | undefined;

export class GuidMapHolder {
  constructor(readonly byId: Dictionary<UUID, string>) {}

  static get $runtimeType(): ClassType<GuidMapHolder> {
    return (GuidMapHolder$rt ??= {
      kind: "class",
      name: "GuidMapHolder",
      fullName: "SampleJsonContracts.GuidMapHolder",
      namespace: "SampleJsonContracts",
      ctor: GuidMapHolder,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is GuidMapHolder => v instanceof GuidMapHolder,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.byId === other.byId
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.byId);

    return hc.toHashCode();
  }

  with(overrides?: Partial<GuidMapHolder>): GuidMapHolder {
    return copyRecordWith<GuidMapHolder>(this, overrides);
  }

  toString(): string {
    return `GuidMapHolder { ById = ${this.byId} }`;
  }
}
