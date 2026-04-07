export const Color = {
  Red: "RED",
  Green: "GREEN",
  Blue: "BLUE",
} as const;

export type Color = typeof Color[keyof typeof Color];
