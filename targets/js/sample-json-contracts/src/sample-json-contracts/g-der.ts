/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { GBase } from "./g-base.js";

let GDer$rt: ClassType<GDer> | undefined;

export class GDer extends GBase<number> {
  constructor(readonly extra: number) {
    super(1);
  }

  static override get $runtimeType(): ClassType<GDer> {
    return (GDer$rt ??= {
      kind: "class",
      name: "GDer",
      fullName: "SampleJsonContracts.GDer",
      namespace: "SampleJsonContracts",
      ctor: GDer,
      base: () => GBase.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is GDer => v instanceof GDer,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.extra === other.extra;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.extra);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<GDer>): GDer {
    return copyRecordWith<GDer>(this, overrides);
  }
}
