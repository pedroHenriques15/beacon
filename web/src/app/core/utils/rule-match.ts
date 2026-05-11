export function matchesRule(
  tx: { description: string; amount: number },
  pattern: string | null | undefined,
  value: number | null | undefined,
): boolean {
  const patMatch = pattern ? tx.description.includes(pattern) : true;
  const valMatch = value !== null && value !== undefined ? tx.amount === value : true;
  return patMatch && valMatch;
}
