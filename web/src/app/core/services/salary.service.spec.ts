import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SalaryService } from './salary.service';
import {
  ParsedSlipResponse,
  SalaryItemCategory,
  SalaryProfile,
  SalarySlip,
} from '../models/statement.model';

const PROFILE_A: SalaryProfile = {
  id: 1,
  name: 'Main Job',
  description: null,
  slipCount: 2,
  hourlyRateFormula: 'days',
};
const PROFILE_B: SalaryProfile = {
  id: 2,
  name: 'Side Job',
  description: 'Consulting',
  slipCount: 0,
  hourlyRateFormula: 'days',
};

const CAT_INCOME: SalaryItemCategory = {
  id: 10,
  name: 'Vencimento',
  color: '#34d399',
  itemType: 'income',
  isProtected: true,
  profileId: 1,
};
const CAT_TAX: SalaryItemCategory = {
  id: 11,
  name: 'IRS',
  color: '#f59e0b',
  itemType: 'tax',
  isProtected: true,
  profileId: 1,
};

const SLIP: SalarySlip = {
  id: 100,
  salaryProfileId: 1,
  profileName: 'Main Job',
  period: '2026-03-01',
  grossAmount: 1774.4,
  netAmount: 1619.4,
  notes: null,
  pdfPath: 'salary/abc.pdf',
  sourceFile: 'march.pdf',
  importedAt: '2026-04-01T00:00:00Z',
  lineItems: [],
  baseAmount: null,
  hoursWorked: null,
  hourlyRate: null,
  totalEspecie: null,
};

const PARSED: ParsedSlipResponse = {
  parserName: 'CentralGest',
  employer: 'KONK CONSULTING',
  employerNif: '506977544',
  period: '2026-03-01',
  grossAmount: 1774.4,
  netAmount: 1619.4,
  baseAmount: null,
  hoursWorked: null,
  hourlyRate: null,
  totalEspecie: null,
  lineItems: [
    {
      description: 'Vencimento',
      amount: 1000,
      itemType: 'income',
      quantity: null,
      unitValue: null,
      percentage: null,
      incidenciaBase: null,
    },
    {
      description: 'IRS',
      amount: 45,
      itemType: 'tax',
      quantity: null,
      unitValue: null,
      percentage: null,
      incidenciaBase: null,
    },
  ],
};

describe('SalaryService', () => {
  let service: SalaryService;
  let ctrl: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [SalaryService, provideHttpClient(), provideHttpClientTesting()],
    });
    ctrl = TestBed.inject(HttpTestingController);
    service = TestBed.inject(SalaryService);
  });

  it('getProfiles() sends GET /api/salary/profiles', () => {
    service.getProfiles().subscribe();
    const req = ctrl.expectOne('/api/salary/profiles');
    expect(req.request.method).toBe('GET');
    req.flush([PROFILE_A]);
  });

  it('getProfiles() returns profile array', () => {
    let result: SalaryProfile[] = [];
    service.getProfiles().subscribe((v) => (result = v));
    ctrl.expectOne('/api/salary/profiles').flush([PROFILE_A, PROFILE_B]);
    expect(result.length).toBe(2);
    expect(result[0].name).toBe('Main Job');
  });

  it('createProfile() POSTs name and description', () => {
    service.createProfile('Consulting', 'Freelance').subscribe();
    const req = ctrl.expectOne('/api/salary/profiles');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Consulting', description: 'Freelance' });
    req.flush(PROFILE_B);
  });

  it('createProfile() omits description when undefined', () => {
    service.createProfile('Solo').subscribe();
    const req = ctrl.expectOne('/api/salary/profiles');
    expect(req.request.body).toEqual({ name: 'Solo', description: undefined });
    req.flush({ ...PROFILE_A, name: 'Solo' });
  });

  it('updateProfile() PUTs to /api/salary/profiles/:id', () => {
    service.updateProfile(1, 'Updated', 'desc').subscribe();
    const req = ctrl.expectOne('/api/salary/profiles/1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ name: 'Updated', description: 'desc' });
    req.flush(PROFILE_A);
  });

  it('deleteProfile() sends DELETE to /api/salary/profiles/:id', () => {
    service.deleteProfile(1).subscribe();
    const req = ctrl.expectOne('/api/salary/profiles/1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('getItemCategories() sends GET /api/salary/item-categories?profileId=1', () => {
    service.getItemCategories(1).subscribe();
    const req = ctrl.expectOne('/api/salary/item-categories?profileId=1');
    expect(req.request.method).toBe('GET');
    req.flush([CAT_INCOME]);
  });

  it('createItemCategory() POSTs profileId, name, color, itemType', () => {
    service.createItemCategory(1, 'PPR', '#6c63ff', 'income').subscribe();
    const req = ctrl.expectOne('/api/salary/item-categories');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      salaryProfileId: 1,
      name: 'PPR',
      color: '#6c63ff',
      itemType: 'income',
    });
    req.flush({ ...CAT_INCOME, id: 20, name: 'PPR' });
  });

  it('updateItemCategory() PUTs to /api/salary/item-categories/:id', () => {
    service.updateItemCategory(10, 'Salário Base', '#34d399', 'income').subscribe();
    const req = ctrl.expectOne('/api/salary/item-categories/10');
    expect(req.request.method).toBe('PUT');
    req.flush(CAT_INCOME);
  });

  it('deleteItemCategory() sends DELETE to /api/salary/item-categories/:id', () => {
    service.deleteItemCategory(11).subscribe();
    const req = ctrl.expectOne('/api/salary/item-categories/11');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('getSlips() sends GET /api/salary/slips with no params by default', () => {
    service.getSlips().subscribe();
    const req = ctrl.expectOne((r) => r.url === '/api/salary/slips');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys().length).toBe(0);
    req.flush([SLIP]);
  });

  it('getSlips() sends profileId param when provided', () => {
    service.getSlips(1).subscribe();
    const req = ctrl.expectOne((r) => r.url === '/api/salary/slips');
    expect(req.request.params.get('profileId')).toBe('1');
    req.flush([SLIP]);
  });

  it('getSlips() sends year param when provided', () => {
    service.getSlips(undefined, 2026).subscribe();
    const req = ctrl.expectOne((r) => r.url === '/api/salary/slips');
    expect(req.request.params.get('year')).toBe('2026');
    req.flush([SLIP]);
  });

  it('getSlips() sends both params when provided', () => {
    service.getSlips(2, 2025).subscribe();
    const req = ctrl.expectOne((r) => r.url === '/api/salary/slips');
    expect(req.request.params.get('profileId')).toBe('2');
    expect(req.request.params.get('year')).toBe('2025');
    req.flush([]);
  });

  it('createSlip() POSTs slip body to /api/salary/slips', () => {
    const body = {
      salaryProfileId: 1,
      period: '2026-03-01',
      grossAmount: 1774.4,
      netAmount: 1619.4,
      lineItems: [{ salaryItemCategoryId: 10, amount: 1000, sortOrder: 0 }],
    };
    service.createSlip(body).subscribe();
    const req = ctrl.expectOne('/api/salary/slips');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.salaryProfileId).toBe(1);
    expect(req.request.body.lineItems.length).toBe(1);
    req.flush(SLIP);
  });

  it('updateSlip() PUTs to /api/salary/slips/:id', () => {
    const body = {
      period: '2026-03-01',
      grossAmount: 1774.4,
      netAmount: 1619.4,
      lineItems: [],
    };
    service.updateSlip(100, body).subscribe();
    const req = ctrl.expectOne('/api/salary/slips/100');
    expect(req.request.method).toBe('PUT');
    req.flush(SLIP);
  });

  it('deleteSlip() sends DELETE to /api/salary/slips/:id', () => {
    service.deleteSlip(100).subscribe();
    const req = ctrl.expectOne('/api/salary/slips/100');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('uploadSlipPdf() POSTs multipart form data to /api/salary/upload-pdf', () => {
    const file = new File(['%PDF-1.4'], 'march.pdf', { type: 'application/pdf' });
    service.uploadSlipPdf(file).subscribe();

    const req = ctrl.expectOne('/api/salary/upload-pdf');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeInstanceOf(FormData);
    req.flush({ pdfPath: 'salary/abc.pdf', fileName: 'march.pdf' });
  });

  it('uploadSlipPdf() returns pdfPath and fileName', () => {
    let result: { pdfPath: string; fileName: string } | undefined;
    const file = new File([''], 'slip.pdf', { type: 'application/pdf' });
    service.uploadSlipPdf(file).subscribe((v) => (result = v));

    ctrl
      .expectOne('/api/salary/upload-pdf')
      .flush({ pdfPath: 'salary/xyz.pdf', fileName: 'slip.pdf' });

    expect(result?.pdfPath).toBe('salary/xyz.pdf');
    expect(result?.fileName).toBe('slip.pdf');
  });

  it('parsePdf() POSTs pdfPath to /api/salary/parse-pdf', () => {
    service.parsePdf('salary/abc.pdf').subscribe();

    const req = ctrl.expectOne('/api/salary/parse-pdf');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ pdfPath: 'salary/abc.pdf' });
    req.flush(PARSED);
  });

  it('parsePdf() returns ParsedSlipResponse with employer and period', () => {
    let result: ParsedSlipResponse | undefined;
    service.parsePdf('salary/abc.pdf').subscribe((v) => (result = v));

    ctrl.expectOne('/api/salary/parse-pdf').flush(PARSED);

    expect(result?.employer).toBe('KONK CONSULTING');
    expect(result?.period).toBe('2026-03-01');
    expect(result?.grossAmount).toBe(1774.4);
  });

  it('parsePdf() returns line items from response', () => {
    let result: ParsedSlipResponse | undefined;
    service.parsePdf('salary/abc.pdf').subscribe((v) => (result = v));

    ctrl.expectOne('/api/salary/parse-pdf').flush(PARSED);

    expect(result?.lineItems.length).toBe(2);
    expect(result?.lineItems[0].description).toBe('Vencimento');
    expect(result?.lineItems[1].itemType).toBe('tax');
  });

  it('parsePdf() passes through error responses', () => {
    let errorMsg = '';
    service.parsePdf('salary/bad.pdf').subscribe({
      error: (err) => (errorMsg = err.error),
    });

    ctrl
      .expectOne('/api/salary/parse-pdf')
      .flush('No salary slip parser recognised this PDF format.', {
        status: 400,
        statusText: 'Bad Request',
      });

    expect(errorMsg).toBe('No salary slip parser recognised this PDF format.');
  });
});
