import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { buildParams } from '../utils/http-params';
import { Observable } from 'rxjs';
import {
  ParsedSlipResponse,
  SalaryItemCategory,
  SalaryProfile,
  SalarySlip,
} from '../models/statement.model';

export interface SlipLineItemBody {
  salaryItemCategoryId: number;
  amount: number;
  sortOrder: number;
  quantity?: number;
  unitValue?: number;
  percentage?: number;
  incidenciaBase?: number;
}

export interface SlipBody {
  period: string;
  grossAmount: number;
  netAmount: number;
  notes?: string;
  pdfPath?: string | null;
  sourceFile?: string | null;
  baseAmount?: number;
  hoursWorked?: number;
  hourlyRate?: number;
  totalEspecie?: number;
  lineItems: SlipLineItemBody[];
}

@Injectable({ providedIn: 'root' })
export class SalaryService {
  private http = inject(HttpClient);

  getProfiles(): Observable<SalaryProfile[]> {
    return this.http.get<SalaryProfile[]>('/api/salary/profiles');
  }
  createProfile(name: string, description?: string): Observable<SalaryProfile> {
    return this.http.post<SalaryProfile>('/api/salary/profiles', { name, description });
  }
  updateProfile(id: number, name: string, description?: string): Observable<SalaryProfile> {
    return this.http.put<SalaryProfile>(`/api/salary/profiles/${id}`, { name, description });
  }
  deleteProfile(id: number): Observable<void> {
    return this.http.delete<void>(`/api/salary/profiles/${id}`);
  }

  getItemCategories(profileId: number): Observable<SalaryItemCategory[]> {
    return this.http.get<SalaryItemCategory[]>(
      `/api/salary/item-categories?profileId=${profileId}`,
    );
  }
  createItemCategory(
    profileId: number,
    name: string,
    color: string,
    itemType: string,
  ): Observable<SalaryItemCategory> {
    return this.http.post<SalaryItemCategory>('/api/salary/item-categories', {
      salaryProfileId: profileId,
      name,
      color,
      itemType,
    });
  }
  updateItemCategory(
    id: number,
    name: string,
    color: string,
    itemType: string,
  ): Observable<SalaryItemCategory> {
    return this.http.put<SalaryItemCategory>(`/api/salary/item-categories/${id}`, {
      name,
      color,
      itemType,
    });
  }
  deleteItemCategory(id: number): Observable<void> {
    return this.http.delete<void>(`/api/salary/item-categories/${id}`);
  }

  getSlips(profileId?: number, year?: number): Observable<SalarySlip[]> {
    const p = buildParams({ profileId, year });
    return this.http.get<SalarySlip[]>('/api/salary/slips', { params: p });
  }

  uploadSlipPdf(file: File): Observable<{ pdfPath: string; fileName: string }> {
    const fd = new FormData();
    fd.append('file', file, file.name);
    return this.http.post<{ pdfPath: string; fileName: string }>('/api/salary/upload-pdf', fd);
  }

  parsePdf(pdfPath: string): Observable<ParsedSlipResponse> {
    return this.http.post<ParsedSlipResponse>('/api/salary/parse-pdf', { pdfPath });
  }

  createSlip(body: SlipBody & { salaryProfileId: number }): Observable<SalarySlip> {
    return this.http.post<SalarySlip>('/api/salary/slips', body);
  }

  updateSlip(id: number, body: SlipBody): Observable<SalarySlip> {
    return this.http.put<SalarySlip>(`/api/salary/slips/${id}`, body);
  }

  deleteSlip(id: number): Observable<void> {
    return this.http.delete<void>(`/api/salary/slips/${id}`);
  }
}
