export class Account {
  readonly amount: number = 0;

  private readonly _bonus: number;

  private readonly _total: number = 0;

  readonly twice: number = 0;

  private readonly _owner: string;

  constructor(amount: number, owner: string, bonus: number) {
    this._owner = owner;
    this._bonus = bonus;
    this.amount = Math.imul(amount, 2);
    this._total = (bonus + 1) | 0;
    this.twice = Math.imul(amount, 2);
  }

  get owner(): string {
    return this._owner.toUpperCase();
  }

  sum(): number {
    return (this._bonus + this._total) | 0;
  }
}
