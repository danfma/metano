/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import type { Title } from "./title.js";

let EpisodeRec$rt: ClassType<EpisodeRec> | undefined;

export class EpisodeRec {
  constructor(
    readonly id: string,
    readonly note: Title,
  ) {}

  static get $runtimeType(): ClassType<EpisodeRec> {
    return (EpisodeRec$rt ??= {
      kind: "class",
      name: "EpisodeRec",
      fullName: "SampleJsonContracts.EpisodeRec",
      namespace: "SampleJsonContracts",
      ctor: EpisodeRec,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is EpisodeRec => v instanceof EpisodeRec,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.id === other.id &&
      this.note === other.note
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.id);
    hc.add(this.note);

    return hc.toHashCode();
  }

  with(overrides?: Partial<EpisodeRec>): EpisodeRec {
    return copyRecordWith<EpisodeRec>(this, overrides);
  }
}
