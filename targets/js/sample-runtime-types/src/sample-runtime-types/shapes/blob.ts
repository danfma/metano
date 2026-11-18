/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export class Blob {
  constructor() {}

  get size(): number {
    return 1;
  }
}
