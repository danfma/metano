/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export class InvalidCurrencyCodeException extends Error {
  constructor(code: string) {
    super(`${`Unknown currency code '${code}'`} (Parameter 'code')`);
  }
}
