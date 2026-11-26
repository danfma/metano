/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { Counter } from "../models/counter.js";
import { Renderer } from "../views/renderer.js";
import type { ICounterView } from "./i-counter-view.js";

export class CounterPresenter {
  private declare _state: Counter;
  private declare readonly _view: ICounterView;

  constructor(view: ICounterView, initialState: Counter) {
    this._state = initialState;
    this._view = view;
  }

  startApplication(containerName: string): void {
    const renderer = new Renderer(containerName);
    renderer.render(this._view);
    this._view.onButtonClick = this.onButtonClicked.bind(this);
    this._view.showCounter(this._state.count);
  }

  private onButtonClicked(): void {
    this._state = this._state.increment();
    this._view.showCounter(this._state.count);
  }
}
