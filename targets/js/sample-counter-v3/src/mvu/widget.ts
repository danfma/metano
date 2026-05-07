/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export abstract class Widget {
  constructor() { }

  abstract render(): HTMLElement;
}
