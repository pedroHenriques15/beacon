const EUR = new Intl.NumberFormat('en-GB', {
  style: 'currency',
  currency: 'EUR',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const EUR_WHOLE = new Intl.NumberFormat('en-GB', {
  style: 'currency',
  currency: 'EUR',
  maximumFractionDigits: 0,
});

/** '€1,862.45', always without a sign. */
export function eur(value: number): string {
  return EUR.format(Math.abs(value));
}

/** '€1,862', always without a sign. */
export function eurWhole(value: number): string {
  return EUR_WHOLE.format(Math.abs(value));
}

/** '+€2,980.00' or '−€1,862.45' (a true minus sign); zero has no sign. */
export function signedEur(value: number, whole = false): string {
  const text = whole ? eurWhole(value) : eur(value);
  if (Math.abs(value) < 0.005) return text;
  return (value < 0 ? '−' : '+') + text;
}

/** Axis labels: '€0', '€500', '€1.5k', '€12k'. */
export function eurAxis(value: number): string {
  if (Math.abs(value) < 1000) return '€' + Math.round(value);
  const k = value / 1000;
  return '€' + (Number.isInteger(k) || Math.abs(k) >= 10 ? Math.round(k) : k.toFixed(1)) + 'k';
}
