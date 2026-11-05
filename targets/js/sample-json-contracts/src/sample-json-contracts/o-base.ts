/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export abstract class OBase {
  readonly base: number = 0;

  abstract readonly name: string;

  constructor() {}
}
