import { Transaction } from '../models/statement.model';

export function availableMonths(transactions: (Transaction & { month: string })[]): string[] {
  return [...new Set(transactions.map((tx) => tx.month))].sort().reverse();
}
