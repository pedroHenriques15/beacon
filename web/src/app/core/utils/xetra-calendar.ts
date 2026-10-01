/**
 * Xetra's trading days, which every priced listing follows: weekdays except New Year's Day,
 * Good Friday, Easter Monday, Labour Day and 24, 25, 26 and 31 December. A copy of the API's
 * Services/Pricing/XetraCalendar.cs; change both together. Dates are ISO strings (yyyy-mm-dd).
 */

const FIXED_HOLIDAYS = ['01-01', '05-01', '12-24', '12-25', '12-26', '12-31'];

function toDate(iso: string): Date {
  return new Date(iso + 'T00:00:00Z');
}

function toIso(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function addDays(iso: string, days: number): string {
  const d = toDate(iso);
  d.setUTCDate(d.getUTCDate() + days);
  return toIso(d);
}

/** Easter Sunday in the Gregorian calendar (the anonymous Gregorian algorithm). */
export function easterSunday(year: number): string {
  const a = year % 19;
  const b = Math.floor(year / 100);
  const c = year % 100;
  const d = Math.floor(b / 4);
  const e = b % 4;
  const f = Math.floor((b + 8) / 25);
  const g = Math.floor((b - f + 1) / 3);
  const h = (19 * a + b - d - g + 15) % 30;
  const i = Math.floor(c / 4);
  const k = c % 4;
  const l = (32 + 2 * e + 2 * i - h - k) % 7;
  const m = Math.floor((a + 11 * h + 22 * l) / 451);
  const month = Math.floor((h + l - 7 * m + 114) / 31);
  const day = ((h + l - 7 * m + 114) % 31) + 1;
  return toIso(new Date(Date.UTC(year, month - 1, day)));
}

export function isTradingDay(iso: string): boolean {
  const weekday = toDate(iso).getUTCDay();
  if (weekday === 0 || weekday === 6) return false;
  if (FIXED_HOLIDAYS.includes(iso.slice(5))) return false;
  const easter = easterSunday(Number(iso.slice(0, 4)));
  return iso !== addDays(easter, -2) && iso !== addDays(easter, 1);
}

/** The last trading day before the given date. */
export function previousTradingDay(iso: string): string {
  let day = addDays(iso, -1);
  while (!isTradingDay(day)) day = addDays(day, -1);
  return day;
}
