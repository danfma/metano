/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let InitRec$rt: ClassType<InitRec> | undefined;

export class InitRec {
  readonly note: string | null = null;

  constructor(readonly a: number) {}

  static get $runtimeType(): ClassType<InitRec> {
    return (InitRec$rt ??= {
      kind: "class",
      name: "InitRec",
      fullName: "SampleJsonContracts.InitRec",
      namespace: "SampleJsonContracts",
      ctor: InitRec,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is InitRec => v instanceof InitRec,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.a === other.a &&
      this.note === other.note
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.a);
    hc.add(this.note);

    return hc.toHashCode();
  }

  with(overrides?: Partial<InitRec>): InitRec {
    return copyRecordWith<InitRec>(this, overrides);
  }
}
