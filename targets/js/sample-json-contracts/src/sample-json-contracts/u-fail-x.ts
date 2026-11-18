/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { UFail } from "./u-fail";

let UFailX$rt: ClassType<UFailX> | undefined;

export class UFailX extends UFail {
  constructor(readonly a: number) {
    super();
  }

  static override get $runtimeType(): ClassType<UFailX> {
    return (UFailX$rt ??= {
      kind: "class",
      name: "UFailX",
      fullName: "SampleJsonContracts.UFailX",
      namespace: "SampleJsonContracts",
      ctor: UFailX,
      base: () => UFail.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is UFailX => v instanceof UFailX,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.a === other.a;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.a);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<UFailX>): UFailX {
    return copyRecordWith<UFailX>(this, overrides);
  }
}
