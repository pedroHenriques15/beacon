import { HttpParams } from '@angular/common/http';

export function buildParams(
  obj: Record<string, string | number | boolean | null | undefined>,
): HttpParams {
  let p = new HttpParams();
  for (const [key, value] of Object.entries(obj)) {
    if (value !== null && value !== undefined) {
      p = p.set(key, String(value));
    }
  }
  return p;
}
