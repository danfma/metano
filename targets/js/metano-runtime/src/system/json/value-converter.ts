/** The serialize/deserialize pair the runtime resolves for one descriptor. */
export type ValueConverter = {
  serialize: (value: unknown) => unknown;
  deserialize: (value: unknown) => unknown;
};
