/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let SPoint$rt: ClassType<SPoint> | undefined;

export class SPoint {
  constructor(
    readonly x: number,
    readonly y: number,
  ) {}

  static get $runtimeType(): ClassType<SPoint> {
    return (SPoint$rt ??= {
      kind: "class",
      name: "SPoint",
      fullName: "SampleJsonContracts.SPoint",
      namespace: "SampleJsonContracts",
      ctor: SPoint,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: true,
      default: () => SPoint.$default(),
      create: () => SPoint.$default(),
      test: (v: unknown): v is SPoint => v instanceof SPoint,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.x === other.x &&
      this.y === other.y
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);
    hc.add(this.y);

    return hc.toHashCode();
  }

  with(overrides?: Partial<SPoint>): SPoint {
    return copyRecordWith<SPoint>(this, overrides);
  }

  toString(): string {
    return `SPoint { X = ${this.x}, Y = ${this.y} }`;
  }

  static $default(): SPoint {
    return Object.assign(Object.create(SPoint.prototype), { x: 0, y: 0 });
  }
}
