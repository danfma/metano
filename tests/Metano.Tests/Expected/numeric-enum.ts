export const Priority = {
  Low: 0,
  Medium: 1,
  High: 2,
} as const;

export type Priority = typeof Priority[keyof typeof Priority];
