/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { UNear } from "./u-near.js";

let UNearA$rt: ClassType<UNearA> | undefined;

export class UNearA extends UNear {
  constructor(
    a: number,
    readonly b: number,
  ) {
    super(a);
  }

  static override get $runtimeType(): ClassType<UNearA> {
    return (UNearA$rt ??= {
      kind: "class",
      name: "UNearA",
      fullName: "SampleJsonContracts.UNearA",
      namespace: "SampleJsonContracts",
      ctor: UNearA,
      base: () => UNear.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is UNearA => v instanceof UNearA,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.b === other.b;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.b);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<UNearA>): UNearA {
    return copyRecordWith<UNearA>(this, overrides);
  }
}
