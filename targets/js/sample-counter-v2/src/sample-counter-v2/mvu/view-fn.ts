/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { IWidget } from "./i-widget.js";

export type ViewFn<TState> = (state: TState, setState: (obj: TState) => void) => IWidget;
