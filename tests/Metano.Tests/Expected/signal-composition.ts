import { createSignal } from "solid-js";

const [count, setCount] = createSignal(0);

console.log(count());

setCount(count() + 1);

setCount((c: number) => c + 1);
