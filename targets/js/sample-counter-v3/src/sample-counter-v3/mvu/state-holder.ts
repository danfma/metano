/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export class StateHolder<TState> {
  private _state: TState;

  onChange: (() => void) | null = null;

  constructor(initial: TState) {
    this._state = initial;
  }

  get state(): TState {
    return this._state;
  }

  update(reducer: (arg: TState) => TState): void {
    this._state = reducer(this._state);
    this.onChange?.();
  }
}
