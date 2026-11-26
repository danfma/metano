export class Account {
  declare readonly amount: number;
  declare private readonly _bonus: number;
  declare private readonly _total: number;
  declare readonly twice: number;
  declare private readonly _owner: string;

  constructor(amount: number, owner: string, bonus: number) {
    this.amount = 0;
    this._bonus = bonus;
    this._total = 0;
    this.twice = 0;
    this._owner = owner;
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
