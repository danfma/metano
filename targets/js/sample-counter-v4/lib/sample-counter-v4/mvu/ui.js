import { createElement } from "inferno-create-element";
export function Column(args) {
    const { gap = 0, children } = args;
    return createElement("div", { className: `column gap-${gap}` }, children);
}
export function Row(args) {
    const { gap = 0, children } = args;
    return createElement("div", { className: `row gap-${gap}` }, children);
}
export function Text(args) {
    const { content } = args;
    return createElement("span", { className: "text" }, content);
}
export function Heading(args) {
    const { content, level = 1 } = args;
    return createElement(`h${level}`, { className: "heading" }, content);
}
export function Button(args) {
    const { label, onClick } = args;
    return createElement("button", { className: "btn", onClick: onClick }, label);
}
