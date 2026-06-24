/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { IWidget } from "#/sample-counter-v2/mvu/i-widget";

export class Column implements IWidget {
  private readonly _children: IWidget[];

  constructor(children: IWidget[]) {
    this._children = children;
  }

  build(): HTMLElement {
    const div = document.createElement("div");
    for (const child of this._children) {
      div.append(child.build());
    }

    return div;
  }
}
