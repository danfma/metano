/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { BuildContext } from "./build-context";
import { Widget } from "./widget";

export abstract class StatefulWidget<TState> extends Widget {
  private _context: BuildContext<TState> | null = null;

  constructor() {
    super();
  }

  abstract initial(): TState;

  protected abstract build(context: BuildContext<TState>): Widget;

  bind(context: BuildContext<TState>): void {
    this._context = context;
  }

  render(): HTMLElement {
    if (this._context == null) {
      throw new Error("Widget not bound to a context.");
    }

    return this.build(this._context).render();
  }
}
