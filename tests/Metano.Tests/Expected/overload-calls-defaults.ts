import { LoudGreeter } from "./loud-greeter.js";

export class Caller {
  static throughBase(): number {
    const g = new LoudGreeter();

    return g.greetNameTimes("x", 3);
  }

  static direct(): number {
    const g = new LoudGreeter();

    return g.greetNameTimes$("x", 9);
  }
}
