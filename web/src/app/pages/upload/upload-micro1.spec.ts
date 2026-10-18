import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { UploadComponent } from './upload';
import {
  SalaryItemCategory,
  SalaryProfile,
  SalarySlip,
  UnifiedUploadItemResult,
} from '../../core/models/statement.model';

// Synthetic: the second half of July from a micro1 invoice paired with its Deel withdrawal.
const batchItem: UnifiedUploadItemResult = {
  fileName: 'invoice-jul16-31.pdf',
  documentType: 'SalarySlip',
  success: true,
  wasDuplicate: false,
  error: null,
  statementResult: null,
  groceryResult: null,
  salaryResult: {
    pdfPath: 'invoice.pdf',
    fileName: 'invoice-jul16-31.pdf',
    files: [{ pdfPath: 'withdrawal.pdf', sourceFile: 'withdrawal_b.pdf' }],
    parsed: {
      parserName: 'Micro1',
      employer: 'Micro1 Inc.',
      employerNif: null,
      period: '2026-07-01',
      grossAmount: 350,
      netAmount: 348,
      lineItems: [
        {
          description: 'Base Pay',
          amount: 350,
          itemType: 'income',
          quantity: null,
          unitValue: null,
          percentage: null,
          incidenciaBase: null,
        },
      ],
      baseAmount: 350,
      hoursWorked: 8,
      hourlyRate: 43.75,
      totalEspecie: null,
    },
  },
};

const profile: SalaryProfile = {
  id: 5,
  name: 'Micro1 Inc.',
  description: null,
  slipCount: 1,
  hourlyRateFormula: 'hours',
};

const basePay: SalaryItemCategory = {
  id: 9,
  name: 'Base Pay',
  color: '#22c55e',
  itemType: 'income',
  isProtected: false,
  profileId: 5,
};

const firstHalf: SalarySlip = {
  id: 40,
  salaryProfileId: 5,
  profileName: 'Micro1 Inc.',
  period: '2026-07-01',
  grossAmount: 1400,
  netAmount: 1390,
  notes: null,
  pdfPath: 'first.pdf',
  sourceFile: 'invoice-jul1-15.pdf',
  importedAt: '2026-07-20T10:00:00Z',
  lineItems: [],
  baseAmount: 1400,
  hoursWorked: 32,
  hourlyRate: 43.75,
  totalEspecie: null,
};

describe('UploadComponent with a micro1 paycheck', () => {
  let fixture: ComponentFixture<UploadComponent>;
  let component: UploadComponent;
  let http: HttpTestingController;

  function upload(existingSlips: SalarySlip[]): void {
    TestBed.configureTestingModule({
      imports: [UploadComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(UploadComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne('/api/salary/profiles').flush([profile]);

    const files = ['invoice-jul16-31.pdf', 'withdrawal_b.pdf'].map(
      (name) => new File(['%PDF'], name, { type: 'application/pdf' }),
    );
    component.onFileSelected({ target: { files, value: '' } } as unknown as Event);
    http.expectOne('/api/upload/batch').flush([batchItem]);
    http.expectOne('/api/salary/item-categories?profileId=5').flush([basePay]);
    http.expectOne('/api/salary/slips?profileId=5').flush(existingSlips);
    fixture.detectChanges();
  }

  function save(): void {
    component.submitSlip();
    http.expectOne('/api/salary/item-categories?profileId=5').flush([basePay]);
  }

  beforeEach(() => TestBed.resetTestingModule());

  it('saves a new month with the Deel withdrawal kept beside the invoice', () => {
    upload([]);
    expect(component.showSlipModal()).toBe(true);

    save();
    const create = http.expectOne((r) => r.url === '/api/salary/slips' && r.method === 'POST');
    expect(create.request.body).toMatchObject({
      salaryProfileId: 5,
      pdfPath: 'invoice.pdf',
      sourceFile: 'invoice-jul16-31.pdf',
      files: [{ pdfPath: 'withdrawal.pdf', sourceFile: 'withdrawal_b.pdf' }],
    });
    create.flush(firstHalf);

    expect(component.salaryQueue()[0].status).toBe('saved');
  });

  it('merges a second half with its invoice and its withdrawal', () => {
    upload([firstHalf]);
    expect(component.slipMergeConfirmed()).toBe(true);

    save();
    const merge = http.expectOne('/api/salary/slips/40/merge');
    expect(merge.request.body).toMatchObject({
      pdfPath: 'invoice.pdf',
      sourceFile: 'invoice-jul16-31.pdf',
      files: [{ pdfPath: 'withdrawal.pdf', sourceFile: 'withdrawal_b.pdf' }],
    });
    merge.flush(firstHalf);

    expect(component.salaryQueue()[0].status).toBe('saved');
  });
});
