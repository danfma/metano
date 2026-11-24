/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { IGauge$runtimeType, type IGauge } from "./i-gauge.js";

let Gauge$rt: ClassType<Gauge> | undefined;

export class Gauge implements IGauge {
  constructor(
    readonly level: number,
    readonly unit: string,
  ) {}

  static get $runtimeType(): ClassType<Gauge> {
    return (Gauge$rt ??= {
      kind: "class",
      name: "Gauge",
      fullName: "SampleJsonContracts.Gauge",
      namespace: "SampleJsonContracts",
      ctor: Gauge,
      interfaces: [() => IGauge$runtimeType],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Gauge => v instanceof Gauge,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.level === other.level &&
      this.unit === other.unit
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.level);
    hc.add(this.unit);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Gauge>): Gauge {
    return copyRecordWith<Gauge>(this, overrides);
  }
}
