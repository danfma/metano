/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export class BuildContext<TState> {
  declare readonly state: TState;
  private declare readonly _updater: (obj: (arg: TState) => TState) => void;

  constructor(state: TState, updater: (obj: (arg: TState) => TState) => void) {
    this.state = state;
    this._updater = updater;
  }

  setState(reducer: (arg: TState) => TState): void {
    this._updater(reducer);
  }
}
