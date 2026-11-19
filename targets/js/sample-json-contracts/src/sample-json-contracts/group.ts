/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import type { IReadOnlyList } from "metano-runtime/system/collections";
import { Clip } from "./clip";

let Group$rt: ClassType<Group> | undefined;

export class Group extends Clip {
  constructor(
    readonly name: string,
    readonly members: IReadOnlyList<Clip>,
  ) {
    super(0);
  }

  static override get $runtimeType(): ClassType<Group> {
    return (Group$rt ??= {
      kind: "class",
      name: "Group",
      fullName: "SampleJsonContracts.Group",
      namespace: "SampleJsonContracts",
      ctor: Group,
      base: () => Clip.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Group => v instanceof Group,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.name === other.name && this.members === other.members;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.name);
    hc.add(this.members);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<Group>): Group {
    return copyRecordWith<Group>(this, overrides);
  }
}
