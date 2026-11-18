/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ClassType } from "metano-runtime";

let Account$rt: ClassType<Account> | undefined;

export class Account {
  name: string = "";

  /** @internal */
  version: number = 0;

  /** @internal */
  id: string = "";

  hits: number = 0;

  constructor() {}

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
