/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { IWidget } from "#/mvu";

export class Button implements IWidget {
  private readonly _label: string;

  private readonly _onPressed: () => void;

  constructor(label: string, onPressed: () => void) {
    this._label = label;
    this._onPressed = onPressed;
  }

  build(): HTMLElement {
    const btn = document.createElement("button");
    btn.textContent = this._label;
    btn.onclick = () => this._onPressed();

    return btn;
  }
}
