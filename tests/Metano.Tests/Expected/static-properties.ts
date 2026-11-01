export class Settings {
  private static _level: number = 1;

  static readonly count: number = 3;

  static title: string = "x";

  static plain: number = 0;

  readonly instance: number = 7;

  constructor() { }

  static get level(): number {
    return Settings._level;
  }

  static set level(value: number) {
    Settings._level = value < 0 ? 0 : value;
  }

  static get twice(): number {
    return Settings.count * 2;
  }
}
