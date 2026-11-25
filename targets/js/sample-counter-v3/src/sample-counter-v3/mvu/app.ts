/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { BuildContext } from "./build-context.js";
import { StateHolder } from "./state-holder.js";
import type { StatefulWidget } from "./stateful-widget.js";

export class App {
  private constructor() {}

  static run<TState>(containerId: string, widget: StatefulWidget<TState>): void {
    const holder = new StateHolder(widget.initial());
    const container = App.resolveContainer(containerId);
    const render = () => {
      widget.bind(new BuildContext(holder.state, holder.update.bind(holder)));
      container.innerHTML = "";
      container.append(widget.render());
    };
    holder.onChange = render;
    render();
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
