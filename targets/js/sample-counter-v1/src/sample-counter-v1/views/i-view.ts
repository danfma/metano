/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export interface IView {
  render(container: HTMLElement): void;
}
