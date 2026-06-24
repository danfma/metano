/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { CounterApp } from "#/sample-counter-v4/components/counter-app";
import { getOrCreateElementById } from "#/sample-counter-v4/inferno/dom-extensions";
import { createElement } from "inferno-create-element";
import { render } from "inferno";

render(createElement(CounterApp, {}), getOrCreateElementById(document, "root"));
