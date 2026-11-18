/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { HMid } from "./h-mid";

let HLeaf$rt: ClassType<HLeaf> | undefined;

export class HLeaf extends HMid {
  constructor(
    a: number,
    b: number,
    readonly c: number,
  ) {
    super(a, b);
  }

  static override get $runtimeType(): ClassType<HLeaf> {
    return (HLeaf$rt ??= {
      kind: "class",
      name: "HLeaf",
      fullName: "SampleJsonContracts.HLeaf",
      namespace: "SampleJsonContracts",
      ctor: HLeaf,
      base: () => HMid.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is HLeaf => v instanceof HLeaf,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.c === other.c;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.c);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<HLeaf>): HLeaf {
    return copyRecordWith<HLeaf>(this, overrides);
  }
}
