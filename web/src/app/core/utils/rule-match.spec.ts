import { describe, it, expect } from 'vitest';
import { matchesRule } from './rule-match';

const tx = { description: 'COMPRA LIDL LISBOA', amount: 23.5 };

describe('matchesRule', () => {
  it('matches on a contained pattern', () => {
    expect(matchesRule(tx, 'LIDL', null)).toBe(true);
  });

  it('does not match a missing pattern', () => {
    expect(matchesRule(tx, 'CONTINENTE', null)).toBe(false);
  });

  it('is case-sensitive, mirroring the backend Ordinal comparison', () => {
    expect(matchesRule(tx, 'lidl', null)).toBe(false);
  });

  it('matches on exact value only', () => {
    expect(matchesRule(tx, null, 23.5)).toBe(true);
    expect(matchesRule(tx, null, 23.51)).toBe(false);
  });

  it('requires BOTH pattern and value when both are set', () => {
    expect(matchesRule(tx, 'LIDL', 23.5)).toBe(true);
    expect(matchesRule(tx, 'LIDL', 99)).toBe(false);
    expect(matchesRule(tx, 'CONTINENTE', 23.5)).toBe(false);
  });

  it('matches everything when neither is set', () => {
    expect(matchesRule(tx, null, null)).toBe(true);
    expect(matchesRule(tx, undefined, undefined)).toBe(true);
    expect(matchesRule(tx, '', null)).toBe(true);
  });
});
