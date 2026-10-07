import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { InvestmentsComponent } from './investments';
import { InvestmentAsset, InvestmentLot } from '../../core/models/statement.model';

let nextId = 1;

function lot(assetId: number, date: string, quantity: number, pricePerUnit: number): InvestmentLot {
  return { id: nextId++, assetId, date, quantity, pricePerUnit, fees: null, notes: null };
}

function asset(
  id: number,
  assetType: 'ETF' | 'Gold',
  lots: InvestmentLot[],
  price: number,
): InvestmentAsset {
  return {
    id,
    assetType,
    ticker: assetType === 'ETF' ? 'ETF.DE' : null,
    isin: null,
    name: assetType === 'ETF' ? 'An ETF' : 'Gold',
    notes: null,
    pricesSyncedAt: null,
    priceSyncError: null,
    priceCount: 1,
    lots,
    priceSnapshots: [
      { id: nextId++, assetId: id, date: '2026-10-02', pricePerUnit: price, source: 'Synced' },
    ],
  };
}

describe('InvestmentsComponent', () => {
  let fixture: ComponentFixture<InvestmentsComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [InvestmentsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(InvestmentsComponent);
    http.expectOne('/api/investments/prices/status').flush({ enabled: false });
  });

  function text(selector: string): string {
    return texts(selector)[0] ?? '';
  }

  function texts(selector: string): string[] {
    const els: HTMLElement[] = [...fixture.nativeElement.querySelectorAll(selector)];
    return els.map((el) => el.textContent!.replace(/\s+/g, ' ').trim());
  }

  it('opens the value chart on the whole history', () => {
    const history = http.expectOne((r) => r.url === '/api/investments/prices/history');

    expect(fixture.componentInstance.historyRange()).toBe('All');
    expect(history.request.params.has('from')).toBe(false);
  });

  describe('with an ETF partly sold and gold never sold', () => {
    beforeEach(() => {
      http.expectOne((r) => r.url === '/api/investments/prices/history').flush([]);
      // ETF: 10 bought at 100, 4 sold at 120 (+80 realised), 6 held at 110 (+60 unrealised).
      // Gold: 10 g bought at 100 in January, now 121 (+210 unrealised).
      http
        .expectOne('/api/investments/assets')
        .flush([
          asset(1, 'ETF', [lot(1, '2026-03-02', 10, 100), lot(1, '2026-06-15', -4, 120)], 110),
          asset(2, 'Gold', [lot(2, '2026-01-24', 10, 100)], 121),
        ]);
      fixture.detectChanges();
    });

    it('leads with the total return since the first buy, then splits it', () => {
      expect(text('.value-card__gain')).toBe('+€350.00 (+17.5%) return since Jan 2026');
      expect(texts('.tile__label')).toEqual(['Money put in', 'Unrealised', 'Realised']);
      expect(texts('.tile__value')).toEqual(['€2,000.00', '+€270.00', '+€80.00']);
      expect(texts('.tile__pct')).toEqual(['+16.9%']);
    });

    it('starts the changes with all time, then the longest period first', () => {
      expect(texts('.changes dt')).toEqual(['All time', '1 month', '1 week', '1 day']);
      expect(text('.changes dd')).toBe('+17.50%');
    });

    it('shows each holding’s total return, realised included', () => {
      expect(texts('.holding__gain .cell__num')).toEqual(['+€140.00', '+€210.00']);
      expect(texts('.holding__gain .cell__pct')).toEqual(['+14.0%', '+21.0%']);
    });

    it('follows the tab', () => {
      fixture.componentInstance.activeTab.set('ETF');
      fixture.detectChanges();
      expect(text('.value-card__gain')).toBe('+€140.00 (+14.0%) return since Mar 2026');

      fixture.componentInstance.activeTab.set('Gold');
      fixture.detectChanges();
      expect(text('.value-card__gain')).toBe('+€210.00 (+21.0%) return since Jan 2026');
    });
  });
});
