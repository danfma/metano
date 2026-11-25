/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Dictionary } from "metano-runtime/system/collections";
import { typeInitializer } from "metano-runtime";
import { Priority } from "./priority.js";

export class PriorityLabels {
  static readonly #$init = /* @__PURE__ */ typeInitializer("SampleTodo.PriorityLabels", () => {
    PriorityLabels.labels = new Dictionary<Priority, string>();
    PriorityLabels.labels.set(Priority.Low, "Low");
    PriorityLabels.labels.set(Priority.Medium, "Medium");
    PriorityLabels.labels.set(Priority.High, "High");
  }) as () => void;

  static #labels$: Dictionary<Priority, string> | null = null;

  private constructor() {}

  static get labels(): Dictionary<Priority, string> {
    PriorityLabels.#$init();

    return PriorityLabels.#labels$ as Dictionary<Priority, string>;
  }

  private static set labels(value: Dictionary<Priority, string>) {
    PriorityLabels.#$init();
    PriorityLabels.#labels$ = value;
  }

  static $initialize(): void {
    PriorityLabels.#$init();
  }
}
