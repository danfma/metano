/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Widget } from "../widget.js";

export class Button extends Widget {
  private declare readonly _label: string;
  private declare readonly _onPressed: () => void;

  constructor(label: string, onPressed: () => void) {
    super();
    this._label = label;
    this._onPressed = onPressed;
  }

  override render(): HTMLElement {
    const btn = document.createElement("button");
    btn.textContent = this._label;
    btn.onclick = () => this._onPressed();

    return btn;
  }
}
