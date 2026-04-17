import type { Counter } from "./counter";

export interface ICounterView {
  displayCounter(counter: Counter): void;
}
