import { LoudGreeter } from "./loud-greeter";

export class Caller {
  constructor() { }

  static throughBase(): number {
    const g = new LoudGreeter();

    return g.greetNameTimes("x", 3);
  }

  static direct(): number {
    const g = new LoudGreeter();

    return g.greetNameTimes$("x", 9);
  }
}
