/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { IWidget } from "./i-widget";
import { StateHolder } from "./state-holder";
import type { ViewFn } from "./view-fn";

export class App {
  constructor() {}

  static mount<TState>(containerId: string, initialState: TState, view: ViewFn<TState>): void {
    const container = App.resolveContainer(containerId);
    const holder = new StateHolder(initialState);
    const setState = holder.set.bind(holder);
    const render = () => App.apply(view(holder.state, setState), container);
    holder.onChange = render;
    render();
  }

  private static apply(root: IWidget, container: HTMLElement): void {
    container.innerHTML = "";
    container.append(root.build());
  }

  private static resolveContainer(containerId: string): HTMLElement {
    const existing = document.getElementById(containerId);

    if (existing != null) {
      return existing;
    }

    const element = document.createElement("div");
    element.id = containerId;
    document.body.append(element);

    return element;
  }
}
