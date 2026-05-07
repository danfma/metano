/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ICounterView } from "#/presenters";

export class CounterView implements ICounterView {
  private _text: HTMLSpanElement | null = null;

  onButtonClick: (() => void) | null = null;

  constructor() { }

  render(container: HTMLElement): void {
    const root = document.createElement("div");
    container.append(root);
    const text = document.createElement("span");
    text.innerHTML = "0";
    root.append(text);
    this._text = text;
    const button = document.createElement("button");
    button.innerHTML = "Click me";
    button.onclick = (_) => this.onButtonClick?.();
    root.append(button);
  }

  showCounter(counter: number): void {
    this._text != null && (this._text.innerHTML = counter.toString());
  }
}
