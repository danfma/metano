/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { VNode as InfernoElement } from "inferno";
import { createElement } from "inferno-create-element";

export function Column(args: { gap?: number; children: InfernoElement[] }): InfernoElement {
  const { gap = 0, children } = args;

  return createElement("div", { className: `column gap-${gap}` }, children);
}

export function Row(args: { gap?: number; children: InfernoElement[] }): InfernoElement {
  const { gap = 0, children } = args;

  return createElement("div", { className: `row gap-${gap}` }, children);
}

export function Text(args: { content: string }): InfernoElement {
  const { content } = args;

  return createElement("span", { className: "text" }, content as any);
}

export function Heading(args: { content: string; level?: number }): InfernoElement {
  const { content, level = 1 } = args;

  return createElement(`h${level}`, { className: "heading" }, content as any);
}

export function Button(args: { label: string; onClick: () => void }): InfernoElement {
  const { label, onClick } = args;

  return createElement("button", { className: "btn", onClick: onClick }, label as any);
}
