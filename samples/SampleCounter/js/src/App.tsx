import { CounterPresenter } from "#/sample-counter";
import { createSignal } from "solid-js";
import styles from "./App.module.css";

function createAppState() {
  const [count, setCount] = createSignal(0);

  const counter = new CounterPresenter({
    displayCounter(counter) {
      setCount(counter.count)
    }
  })

  const increment = () => counter.increment();
  const decrement = () => counter.decrement();

  return { count, increment, decrement };
}

function App() {
    const { count, increment, decrement } = createAppState();

    return (
      <div>
        <div class={styles.counter}>
          <button class={styles.action} type="button" onclick={decrement}>-</button>
          <pre class={styles.text}>{count()}</pre>
          <button class={styles.action} type="button" onclick={increment}>+</button>
        </div>
      </div>
    )
}

export default App
