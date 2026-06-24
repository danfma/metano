/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { Counter } from "#/sample-counter-v1/models/counter";
import { Renderer } from "#/sample-counter-v1/views/renderer";
import type { ICounterView } from "./i-counter-view";

export class CounterPresenter {
  private _state: Counter;

  private readonly _view: ICounterView;

  constructor(view: ICounterView, initialState: Counter) {
    this._view = view;
    this._state = initialState;
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
