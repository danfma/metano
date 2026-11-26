/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { IWidget } from "../i-widget.js";

export class Button implements IWidget {
  private declare readonly _label: string;
  private declare readonly _onPressed: () => void;

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
