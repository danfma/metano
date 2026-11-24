/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let EpisodeSummary$rt: ClassType<EpisodeSummary> | undefined;

export class EpisodeSummary {
  constructor(
    readonly id: string,
    readonly title: string,
  ) {}

  static get $runtimeType(): ClassType<EpisodeSummary> {
    return (EpisodeSummary$rt ??= {
      kind: "class",
      name: "EpisodeSummary",
      fullName: "SampleJsonContracts.EpisodeSummary",
      namespace: "SampleJsonContracts",
      ctor: EpisodeSummary,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is EpisodeSummary => v instanceof EpisodeSummary,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.id === other.id &&
      this.title === other.title
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.id);
    hc.add(this.title);

    return hc.toHashCode();
  }

  with(overrides?: Partial<EpisodeSummary>): EpisodeSummary {
    return copyRecordWith<EpisodeSummary>(this, overrides);
  }

  toString(): string {
    return `EpisodeSummary { Id = ${this.id}, Title = ${this.title} }`;
  }
}
