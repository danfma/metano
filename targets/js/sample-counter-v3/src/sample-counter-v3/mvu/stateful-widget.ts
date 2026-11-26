/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";
import type { BuildContext } from "./build-context.js";
import { Widget } from "./widget.js";

export abstract class StatefulWidget<TState> extends Widget {
  private declare _context: BuildContext<TState> | null;

  constructor() {
    super();
    this._context = null;
  }

  abstract initial(): TState;

  protected abstract build(context: BuildContext<TState>): Widget;

  bind(context: BuildContext<TState>): void {
    this._context = context;
  }

  override render(): HTMLElement {
    if (this._context == null) {
      throw new InvalidOperationException("Widget not bound to a context.");
    }

    return this.build(this._context).render();
  }
}
