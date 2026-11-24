/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, defaultEquals, type ClassType } from "metano-runtime";
import type { IGauge } from "./i-gauge.js";

let Dashboard$rt: ClassType<Dashboard> | undefined;

export class Dashboard {
  constructor(
    readonly title: string,
    readonly main: IGauge | null,
  ) {}

  static get $runtimeType(): ClassType<Dashboard> {
    return (Dashboard$rt ??= {
      kind: "class",
      name: "Dashboard",
      fullName: "SampleJsonContracts.Dashboard",
      namespace: "SampleJsonContracts",
      ctor: Dashboard,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Dashboard => v instanceof Dashboard,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.title === other.title &&
      defaultEquals(this.main, other.main)
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.title);
    hc.add(this.main);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Dashboard>): Dashboard {
    return copyRecordWith<Dashboard>(this, overrides);
  }
}
