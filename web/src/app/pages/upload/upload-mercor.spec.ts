import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { UploadComponent } from './upload';
import {
  ParsedSlipResponse,
  UnifiedMercorResult,
  UnifiedUploadItemResult,
} from '../../core/models/statement.model';

// Synthetic: a Mercor month of $145.17 for 3.63 h, and two payouts imported for it.
const statement: UnifiedMercorResult = {
  pdfPath: 'stored.pdf',
  fileName: 'mercor-august.pdf',
  period: '2026-08-01',
  totalPayUsd: 145.17,
  hoursWorked: 3.63,
  payRateUsd: 40,
  suggestedEur: 124.5,
  payouts: [
    { date: '2026-08-10', bank: 'REVOLUT', amount: 74.5 },
    { date: '2026-08-25', bank: 'REVOLUT', amount: 50 },
  ],
};

const batchItem: UnifiedUploadItemResult = {
  fileName: 'mercor-august.pdf',
  documentType: 'MercorNeedsEur',
  success: true,
  wasDuplicate: false,
  error: null,
  statementResult: null,
  groceryResult: null,
  salaryResult: null,
  mercorResult: statement,
};

const converted: ParsedSlipResponse = {
  parserName: 'Mercor',
  employer: 'Mercor',
  employerNif: null,
  period: '2026-08-01',
  grossAmount: 120,
  netAmount: 120,
  lineItems: [
    {
      description: 'Base Pay',
      amount: 120,
      itemType: 'income',
      quantity: null,
      unitValue: null,
      percentage: null,
      incidenciaBase: null,
    },
  ],
  baseAmount: 120,
  hoursWorked: 3.63,
  hourlyRate: 33.07,
  totalEspecie: null,
};

describe('UploadComponent with a Mercor statement', () => {
  let fixture: ComponentFixture<UploadComponent>;
  let component: UploadComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [UploadComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(UploadComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne('/api/salary/profiles').flush([]);

    const file = new File(['%PDF'], 'mercor-august.pdf', { type: 'application/pdf' });
    component.onFileSelected({ target: { files: [file], value: '' } } as unknown as Event);
    http.expectOne('/api/upload/batch').flush([batchItem]);
    fixture.detectChanges();
  });

  function dialog(): HTMLElement {
    return fixture.nativeElement.querySelector('.mercor-modal');
  }

  function primaryButton(): HTMLButtonElement {
    return dialog().querySelector('.modal-actions .btn-primary') as HTMLButtonElement;
  }

  it('asks for the EUR received first, pre-filled from the month’s payouts', () => {
    expect(dialog()).not.toBeNull();
    expect(component.showSlipModal()).toBe(false);
    expect(component.mercorEur()).toBe(124.5);
    expect(dialog().textContent).toContain('$145.17');
    expect(dialog().querySelectorAll('.payout').length).toBe(2);
  });

  it('cannot go on to the slip without the EUR', () => {
    component.setMercorEur('');
    fixture.detectChanges();

    expect(primaryButton().disabled).toBe(true);
    component.confirmMercorEur();
    http.expectNone('/api/salary/parse-mercor');
    expect(component.showSlipModal()).toBe(false);
    expect(component.salaryQueue()[0].status).toBe('needs-eur');
  });

  it('leaves the statement waiting on the timeline when put off', () => {
    component.dismissMercorModal();
    fixture.detectChanges();

    expect(dialog()).toBeNull();
    const entry = component.groups()[0].entries[0];
    expect(entry.action).toBe('Enter EUR received');
    component.reviewEntry(entry);
    expect(component.showMercorModal()).toBe(true);
  });

  it('converts at the EUR entered, then saves a Mercor slip under a new hours-based profile', () => {
    component.setMercorEur(120);
    component.confirmMercorEur();

    const convert = http.expectOne('/api/salary/parse-mercor');
    expect(convert.request.body).toEqual({ pdfPath: 'stored.pdf', eurReceived: 120 });
    convert.flush(converted);

    expect(component.showMercorModal()).toBe(false);
    expect(component.showSlipModal()).toBe(true);
    expect(component.slipPendingProfileName()).toBe('Mercor');
    expect(component.slipGross()).toBe(120);

    component.submitSlip();
    const profile = http.expectOne('/api/salary/profiles');
    expect(profile.request.method).toBe('POST');
    expect(profile.request.body.hourlyRateFormula).toBe('hours');
    profile.flush({
      id: 7,
      name: 'Mercor',
      description: null,
      slipCount: 0,
      hourlyRateFormula: 'hours',
    });
    http.expectOne('/api/salary/profiles').flush([]);
    http.expectOne('/api/salary/item-categories?profileId=7').flush([]);
    http.expectOne('/api/salary/item-categories').flush({
      id: 3,
      name: 'Base Pay',
      color: '#22c55e',
      itemType: 'income',
      isProtected: false,
      profileId: 7,
    });

    const save = http.expectOne('/api/salary/slips');
    expect(save.request.body).toMatchObject({
      salaryProfileId: 7,
      period: '2026-08-01',
      grossAmount: 120,
      netAmount: 120,
      hoursWorked: 3.63,
      pdfPath: 'stored.pdf',
      lineItems: [{ salaryItemCategoryId: 3, amount: 120, sortOrder: 0 }],
    });
    save.flush({});

    expect(component.salaryQueue()[0].status).toBe('saved');
  });
});
