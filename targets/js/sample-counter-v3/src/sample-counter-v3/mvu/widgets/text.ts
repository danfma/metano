/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Widget } from "#/sample-counter-v3/mvu/widget";

export class Text extends Widget {
  private readonly _content: string;

  constructor(content: string) {
    super();
    this._content = content;
  }

  render(): HTMLElement {
    const span = document.createElement("span");
    span.textContent = this._content;

    return span;
  }
}
