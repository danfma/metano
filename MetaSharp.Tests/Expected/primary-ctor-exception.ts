export class DuplicateEntryException extends Error {
  constructor(name: string) {
    super(`Duplicate entry: ${name}`);
  }
}
