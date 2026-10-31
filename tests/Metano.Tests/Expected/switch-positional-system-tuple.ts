export function second(pair: [string, number]): number {
  return (($s) => {
    if ($s != null && $s[0] === "a") {
      const n = $s[1];

      return n;
    }

    return 0;
  })(pair);
}
