/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export class BuildContext<TState> {
  private readonly _updater: (obj: (arg: TState) => TState) => void;

  constructor(readonly state: TState, updater: (obj: (arg: TState) => TState) => void) {
    this._updater = updater;
  }

  setState(reducer: (arg: TState) => TState): void {
    this._updater(reducer);
  }
}
