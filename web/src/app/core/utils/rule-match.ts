/**
 * Whether a rule matches a row, as the server decides it (`RuleMatch.Matches`): the pattern
 * equals the row's description, trimmed, when the rule matches the whole description, and appears
 * anywhere in it otherwise (both case-sensitive); the amount equals the value when there is one.
 * An empty pattern is no text condition; a rule with neither matches nothing.
 */
export function matchesRule(
  tx: { description: string; amount: number },
  pattern: string | null | undefined,
  value: number | null | undefined,
  matchWholeDescription: boolean,
): boolean {
  const hasValue = value !== null && value !== undefined;
  if (!pattern && !hasValue) return false;
  const patMatch = !pattern
    ? true
    : matchWholeDescription
      ? tx.description.trim() === pattern
      : tx.description.includes(pattern);
  const valMatch = hasValue ? tx.amount === value : true;
  return patMatch && valMatch;
}
