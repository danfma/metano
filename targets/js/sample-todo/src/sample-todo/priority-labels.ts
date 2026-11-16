/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Priority } from "./priority";

export class PriorityLabels {
  static labels: Map<Priority, string>;

  static {
    PriorityLabels.labels = new Map<Priority, string>();
    PriorityLabels.labels.set(Priority.Low, "Low");
    PriorityLabels.labels.set(Priority.Medium, "Medium");
    PriorityLabels.labels.set(Priority.High, "High");
  }

  constructor() {}
}
