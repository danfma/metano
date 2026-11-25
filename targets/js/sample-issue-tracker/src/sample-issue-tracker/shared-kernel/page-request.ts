/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let PageRequest$rt: ClassType<PageRequest> | undefined;

export class PageRequest {
  declare readonly number: number;
  declare readonly size: number;

  constructor(number: number = 1, size: number = 20) {
    this.number = number;
    this.size = size;
  }

  static get $runtimeType(): ClassType<PageRequest> {
    return (PageRequest$rt ??= {
      kind: "class",
      name: "PageRequest",
      fullName: "SampleIssueTracker.SharedKernel.PageRequest",
      namespace: "SampleIssueTracker.SharedKernel",
      ctor: PageRequest,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is PageRequest => v instanceof PageRequest,
    });
  }

  get safeNumber(): number {
    return Math.max(1, this.number);
  }

  get safeSize(): number {
    return Math.max(1, this.size);
  }

  get skip(): number {
    return Math.imul(this.safeNumber - 1, this.safeSize);
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.number === other.number &&
      this.size === other.size
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.number);
    hc.add(this.size);

    return hc.toHashCode();
  }

  with(overrides?: Partial<PageRequest>): PageRequest {
    return copyRecordWith<PageRequest>(this, overrides);
  }

  protected $printMembers(): string {
    return `Number = ${this.number}, Size = ${this.size}, SafeNumber = ${this.safeNumber}, SafeSize = ${this.safeSize}, Skip = ${this.skip}`;
  }

  toString(): string {
    return `PageRequest { ${this.$printMembers()} }`;
  }
}
