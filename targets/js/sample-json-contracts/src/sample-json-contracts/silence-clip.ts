/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { Clip } from "./clip";

let SilenceClip$rt: ClassType<SilenceClip> | undefined;

export class SilenceClip extends Clip {
  constructor(seconds: number) {
    super(seconds);
  }

  static override get $runtimeType(): ClassType<SilenceClip> {
    return (SilenceClip$rt ??= {
      kind: "class",
      name: "SilenceClip",
      fullName: "SampleJsonContracts.SilenceClip",
      namespace: "SampleJsonContracts",
      ctor: SilenceClip,
      base: () => Clip.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is SilenceClip => v instanceof SilenceClip,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());

    return hc.toHashCode();
  }

  override with(overrides?: Partial<SilenceClip>): SilenceClip {
    return copyRecordWith<SilenceClip>(this, overrides);
  }
}
