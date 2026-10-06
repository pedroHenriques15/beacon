import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { UploadComponent } from './upload';
import { InvestmentsService } from '../../core/services/investments.service';
import { UnifiedUploadItemResult } from '../../core/models/statement.model';

// Synthetic: a Trade Republic month whose buys became lots, and an XTB month of trades.
function statementItem(lotsAdded: number): UnifiedUploadItemResult {
  return {
    fileName: 'Extrato de transações.csv',
    documentType: 'BankStatement',
    success: true,
    wasDuplicate: false,
    error: null,
    statementResult: {
      imported: true,
      bank: 'TRADE REPUBLIC',
      periodFrom: '2026-08-01',
      transactionCount: 12,
      unknownCount: 0,
      message: null,
      lotsAdded,
    },
    groceryResult: null,
    salaryResult: null,
  };
}

function xtbItem(added: number): UnifiedUploadItemResult {
  return {
    fileName: 'EUR_1_2026-05-31_2026-06-30.xlsx',
    documentType: 'BrokerExport',
    success: added > 0,
    wasDuplicate: added === 0,
    error: null,
    statementResult: null,
    groceryResult: null,
    salaryResult: null,
    tradesResult: {
      broker: 'XTB',
      periodFrom: '2026-06-01',
      periodTo: '2026-06-30',
      tradeCount: 2,
      added,
      warnings: [],
    },
  };
}

describe('UploadComponent and the Invest page', () => {
  let fixture: ComponentFixture<UploadComponent>;
  let http: HttpTestingController;
  const investments = { load: vi.fn() };

  beforeEach(() => {
    investments.load.mockClear();
    TestBed.configureTestingModule({
      imports: [UploadComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: InvestmentsService, useValue: investments },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(UploadComponent);
    fixture.detectChanges();
    http.expectOne('/api/salary/profiles').flush([]);
  });

  function upload(name: string, results: UnifiedUploadItemResult[]): void {
    const file = new File(['x'], name);
    fixture.componentInstance.onFileSelected({
      target: { files: [file], value: '' },
    } as unknown as Event);
    http.expectOne('/api/upload/batch').flush(results);
    fixture.detectChanges();
  }

  it('refreshes the investments when a statement’s buys added lots', () => {
    upload('Extrato de transações.csv', [statementItem(2)]);
    expect(investments.load).toHaveBeenCalledTimes(1);
  });

  it('refreshes the investments when a broker’s export added lots', () => {
    upload('EUR_1_2026-05-31_2026-06-30.xlsx', [xtbItem(2)]);
    expect(investments.load).toHaveBeenCalledTimes(1);
  });

  it('leaves them alone when no lot was added', () => {
    upload('batch.zip', [statementItem(0), xtbItem(0)]);
    expect(investments.load).not.toHaveBeenCalled();
  });
});
