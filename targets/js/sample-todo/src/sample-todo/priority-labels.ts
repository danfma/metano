/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { typeInitializer } from "metano-runtime";
import { Priority } from "./priority";

export class PriorityLabels {
  static readonly #$init = /* @__PURE__ */ typeInitializer("SampleTodo.PriorityLabels", () => {
    PriorityLabels.labels = new Map<Priority, string>();
    PriorityLabels.labels.set(Priority.Low, "Low");
    PriorityLabels.labels.set(Priority.Medium, "Medium");
    PriorityLabels.labels.set(Priority.High, "High");
  }) as () => void;

  static #labels$: Map<Priority, string> | null = null;

  constructor() {
    PriorityLabels.#$init();
  }

  static get labels(): Map<Priority, string> {
    PriorityLabels.#$init();

    return PriorityLabels.#labels$ as Map<Priority, string>;
  }

  private static set labels(value: Map<Priority, string>) {
    PriorityLabels.#$init();
    PriorityLabels.#labels$ = value;
  }
}
