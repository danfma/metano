/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { IView } from "#/sample-counter-v1/views/i-view";

export interface ICounterView extends IView {
  onButtonClick: (() => void) | null;
  showCounter(counter: number): void;
}
