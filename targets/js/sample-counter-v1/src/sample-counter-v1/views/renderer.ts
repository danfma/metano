/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { IView } from "./i-view.js";

export class Renderer {
  private declare readonly _container: HTMLElement;

  constructor(containerId: string) {
    this._container = undefined!;
    this._container = document.getElementById(containerId) ?? this.createElement(containerId);
  }

  private createElement(containerId: string): HTMLElement {
    const element = document.createElement("div");
    element.id = containerId;
    document.body.append(element);

    return element;
  }

  render(view: IView): void {
    view.render(this._container);
  }
}
