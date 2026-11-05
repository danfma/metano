/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export class Account {
  name: string = "";

  /** @internal */
  version: number = 0;

  /** @internal */
  id: string = "";

  hits: number = 0;

  constructor() {}
}
