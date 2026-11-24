import { render } from "solid-js/web";
import { AppView } from "./views/app-view.jsx";

const container = document.getElementById("root");

if (!container) {
  throw new Error("#root element not found");
}

render(() => <AppView />, container);
