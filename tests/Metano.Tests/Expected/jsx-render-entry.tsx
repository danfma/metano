import { CounterGroup } from "./counter-group";
import { render } from "solid-js/web";

const container = document.getElementById("app");

render(() => <CounterGroup />, container);
