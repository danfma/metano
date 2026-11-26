/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { IWidget } from "../i-widget.js";

export class Text implements IWidget {
  private declare readonly _content: string;

  constructor(content: string) {
    this._content = content;
  }

  build(): HTMLElement {
    const span = document.createElement("span");
    span.textContent = this._content;

    return span;
  }
}
