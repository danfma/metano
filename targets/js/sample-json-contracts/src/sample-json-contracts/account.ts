/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ClassType } from "metano-runtime";

let Account$rt: ClassType<Account> | undefined;

export class Account {
  declare name: string;

  /** @internal */
  declare version: number;

  /** @internal */
  declare id: string;

  declare hits: number;

  constructor() {
    this.name = "";
    this.version = 0;
    this.id = "";
    this.hits = 0;
  }

  static get $runtimeType(): ClassType<Account> {
    return (Account$rt ??= {
      kind: "class",
      name: "Account",
      fullName: "SampleJsonContracts.Account",
      namespace: "SampleJsonContracts",
      ctor: Account,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Account(),
      test: (v: unknown): v is Account => v instanceof Account,
    });
  }
}
