import { CounterGroup } from "./counter-group.js";
import { render } from "solid-js/web";

const container = document.getElementById("app")!;

render(() => <CounterGroup />, container);
