/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ICounterView } from "../presenters/i-counter-view.js";

export class CounterView implements ICounterView {
  private declare _text: HTMLSpanElement | null;
  declare onButtonClick: (() => void) | null;

  constructor() {
    this._text = null;
    this.onButtonClick = null;
  }

  render(container: HTMLElement): void {
    const root = document.createElement("div");
    container.append(root);
    const text = document.createElement("span");
    text.innerHTML = "0";
    root.append(text);
    this._text = text;
    const button = document.createElement("button");
    button.innerHTML = "Click me";
    button.onclick = () => this.onButtonClick?.();
    root.append(button);
  }

  showCounter(counter: number): void {
    this._text != null && (this._text.innerHTML = counter.toString());
  }
}
