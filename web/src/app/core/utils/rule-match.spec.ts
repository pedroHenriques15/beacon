import { describe, it, expect } from 'vitest';
import { matchesRule } from './rule-match';

const tx = { description: 'COMPRA LIDL LISBOA', amount: 23.5 };
const WHOLE = true;
const PARTIAL = false;

describe('matchesRule', () => {
  it('matches a whole-description pattern only against the whole description', () => {
    expect(matchesRule(tx, 'COMPRA LIDL LISBOA', null, WHOLE)).toBe(true);
    expect(matchesRule(tx, 'LIDL', null, WHOLE)).toBe(false);
    expect(matchesRule(tx, 'COMPRA LIDL', null, WHOLE)).toBe(false);
  });

  it('matches a partial pattern anywhere in the description', () => {
    expect(matchesRule(tx, 'LIDL', null, PARTIAL)).toBe(true);
    expect(matchesRule(tx, 'COMPRA LIDL LISBOA', null, PARTIAL)).toBe(true);
  });

  it('does not match a missing pattern', () => {
    expect(matchesRule(tx, 'CONTINENTE', null, WHOLE)).toBe(false);
    expect(matchesRule(tx, 'CONTINENTE', null, PARTIAL)).toBe(false);
  });

  it('is case-sensitive, mirroring the backend Ordinal comparison', () => {
    expect(matchesRule(tx, 'compra lidl lisboa', null, WHOLE)).toBe(false);
    expect(matchesRule(tx, 'lidl', null, PARTIAL)).toBe(false);
  });

  it('ignores spaces around the description for a whole-description match', () => {
    const padded = { ...tx, description: ' COMPRA LIDL LISBOA  ' };
    expect(matchesRule(padded, 'COMPRA LIDL LISBOA', null, WHOLE)).toBe(true);
  });

  it('matches on exact value only', () => {
    expect(matchesRule(tx, null, 23.5, WHOLE)).toBe(true);
    expect(matchesRule(tx, null, 23.51, WHOLE)).toBe(false);
  });

  it('requires BOTH pattern and value when both are set', () => {
    expect(matchesRule(tx, 'COMPRA LIDL LISBOA', 23.5, WHOLE)).toBe(true);
    expect(matchesRule(tx, 'COMPRA LIDL LISBOA', 99, WHOLE)).toBe(false);
    expect(matchesRule(tx, 'LIDL', 23.5, PARTIAL)).toBe(true);
    expect(matchesRule(tx, 'LIDL', 99, PARTIAL)).toBe(false);
    expect(matchesRule(tx, 'CONTINENTE', 23.5, PARTIAL)).toBe(false);
  });

  it('matches nothing when neither is set', () => {
    expect(matchesRule(tx, null, null, WHOLE)).toBe(false);
    expect(matchesRule(tx, undefined, undefined, PARTIAL)).toBe(false);
    expect(matchesRule(tx, '', null, PARTIAL)).toBe(false);
  });
});
