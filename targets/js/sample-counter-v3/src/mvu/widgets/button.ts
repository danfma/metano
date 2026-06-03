/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Widget } from "#/mvu";

export class Button extends Widget {
  private readonly _label: string;

  private readonly _onPressed: () => void;

  constructor(label: string, onPressed: () => void) {
    super();
    this._label = label;
    this._onPressed = onPressed;
  }

  render(): HTMLElement {
    const btn = document.createElement("button");
    btn.textContent = this._label;
    btn.onclick = () => this._onPressed();

    return btn;
  }
}
