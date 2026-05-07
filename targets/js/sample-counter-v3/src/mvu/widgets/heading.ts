/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Widget } from "#/mvu";

export class Heading extends Widget {
  private readonly _level: number;

  private readonly _content: string;

  constructor(content: string, level: number) {
    super();
    this._content = content;
    this._level = level;
  }

  render(): HTMLElement {
    const headingType =
      this._level === 1
        ? { tagName: "h1" }
        : this._level === 2
          ? { tagName: "h2" }
          : this._level === 3
            ? { tagName: "h3" }
            : this._level === 4
              ? { tagName: "h4" }
              : this._level === 5
                ? { tagName: "h5" }
                : { tagName: "h6" };
    const sizeEm =
      this._level === 1
        ? 2
        : this._level === 2
          ? 1.5
          : this._level === 3
            ? 1.25
            : this._level === 4
              ? 1
              : this._level === 5
                ? 0.875
                : 0.75;
    const element = document.createElement(headingType.tagName);
    element.textContent = this._content;
    element.setAttribute("style", `font-size:${sizeEm}em`);

    return element;
  }
}
