import { eur, signedEur } from '../../core/utils/money';

/** The items whose name contains the search term, ignoring case; all of them for a blank term. */
export function filterByName<T extends { name: string }>(items: readonly T[], term: string): T[] {
  const needle = term.trim().toLowerCase();
  if (!needle) return items.slice();
  return items.filter((item) => item.name.toLowerCase().includes(needle));
}

/** Rules grouped by their category id, each group in the order given. */
export function groupByCategory<T extends { categoryId: number }>(
  rules: readonly T[],
): Map<number, T[]> {
  const groups = new Map<number, T[]>();
  for (const rule of rules) {
    const group = groups.get(rule.categoryId);
    if (group) group.push(rule);
    else groups.set(rule.categoryId, [rule]);
  }
  return groups;
}

/** '1 rule', '3 rules': the count with the word that fits it. */
export function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}

/** A category's rule count: 'No rules', '1 rule', '3 rules'. */
export function ruleCountLabel(count: number): string {
  return count === 0 ? 'No rules' : plural(count, 'rule', 'rules');
}

/** A rule's amount: '€1,500.00', '−€12.50' for a negative one, '' when the rule has none. */
export function ruleAmount(value: number | null | undefined): string {
  if (value === null || value === undefined) return '';
  return value < 0 ? signedEur(value) : eur(value);
}

/** What a rule matches, in words: 'contains Banana and amount €2.50', 'equals BANANA KG'. */
export function ruleSummary(
  pattern: string | null | undefined,
  value: number | null | undefined,
  matchWholeDescription: boolean,
): string {
  const parts: string[] = [];
  if (pattern) parts.push(`${matchWholeDescription ? 'equals' : 'contains'} ${pattern}`);
  if (value !== null && value !== undefined) parts.push(`amount ${ruleAmount(value)}`);
  return parts.join(' and ');
}
