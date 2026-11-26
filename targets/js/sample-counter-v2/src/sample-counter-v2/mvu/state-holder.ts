/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export class StateHolder<TState> {
  private declare _state: TState;
  declare onChange: (() => void) | null;

  constructor(initial: TState) {
    this._state = initial;
    this.onChange = null;
  }

  get state(): TState {
    return this._state;
  }

  set(next: TState): void {
    this._state = next;
    this.onChange?.();
  }
}
