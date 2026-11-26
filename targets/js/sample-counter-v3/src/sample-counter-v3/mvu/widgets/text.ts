/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Widget } from "../widget.js";

export class Text extends Widget {
  private declare readonly _content: string;

  constructor(content: string) {
    super();
    this._content = content;
  }

  override render(): HTMLElement {
    const span = document.createElement("span");
    span.textContent = this._content;

    return span;
  }
}
