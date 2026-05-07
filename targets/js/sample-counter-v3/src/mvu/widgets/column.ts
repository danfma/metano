/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Widget } from "#/mvu";

export class Column extends Widget {
  private readonly _gap: number;

  private readonly _children: Widget[];

  constructor(gap: number, children: Widget[]) {
    super();
    this._gap = gap;
    this._children = children;
  }

  render(): HTMLElement {
    const div = document.createElement("div");
    div.setAttribute('style', `display:flex;flex-direction:column;gap:${this._gap}px`);
    for (const child of this._children) {
      div.append(child.render());
    }

    return div;
  }
}
