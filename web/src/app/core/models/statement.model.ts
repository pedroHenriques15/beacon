import { GroceryReceiptUploadResult } from './grocery.model';

export interface Category {
  id: number;
  name: string;
  color: string;
  isProtected: boolean;
  rules?: CategoryRule[];
}

export interface CategoryRule {
  id: number;
  categoryId: number;
  pattern: string;
  value?: number | null;
  category?: Category;
}

export interface Transaction {
  id: number;
  statementId: number;
  datePosting: string;
  dateValue: string;
  description: string;
  amount: number;
  type: 'credit' | 'debit' | 'unknown';
  balance: number;
  categoryId: number | null;
  categoryRuleId: number | null;
  categorySetManually: boolean;
  isExcluded: boolean;
  category: Category | null;
}

export interface StatementSummary {
  id: number;
  bank: string;
  account: string;
  periodFrom: string;
  periodTo: string;
  currency: string;
  openingBalance: number;
  closingBalance: number;
  sourceFile: string;
  hasFile: boolean;
  transactionCount: number;
}

export interface Statement extends StatementSummary {
  pdfPath: string | null;
  importedAt: string;
  transactions: Transaction[];
}

export interface MonthlySummary {
  month: string;
  bank: string;
  income: number;
  expenses: number;
  net: number;
  closingBalance: number;
}

export interface TransferCandidate {
  newTxId: number;
  newDescription: string;
  newType: string;
  newBank: string;
  existingTxId: number;
  existingDescription: string;
  existingType: string;
  existingBank: string;
  date: string;
  amount: number;
}

export interface UploadResult {
  imported: boolean;
  bank: string;
  periodFrom: string;
  transactionCount: number;
  unknownCount: number;
  message: string | null;
  transferCandidates?: TransferCandidate[];
  warnings?: string[] | null;
}

export interface BatchUploadItemResult {
  fileName: string;
  success: boolean;
  result: UploadResult | null;
  error: string | null;
}

export interface PagedTransactionsResult<T> {
  items: T[];
  totalCount: number;
  totalCredit: number;
  totalDebit: number;
}

export type HourlyRateFormula = 'hours' | 'workdays' | 'days';

export interface SalaryProfile {
  id: number;
  name: string;
  description: string | null;
  slipCount: number;
  hourlyRateFormula: HourlyRateFormula;
}

export interface SalaryItemCategory {
  id: number;
  name: string;
  color: string;
  itemType: 'income' | 'deduction' | 'tax';
  isProtected: boolean;
  profileId: number;
}

export interface SalaryLineItem {
  id: number;
  salaryItemCategoryId: number;
  categoryName: string;
  categoryColor: string;
  categoryItemType: string;
  amount: number;
  sortOrder: number;
  quantity: number | null;
  unitValue: number | null;
  percentage: number | null;
  incidenciaBase: number | null;
}

export interface SalarySlip {
  id: number;
  salaryProfileId: number;
  profileName: string;
  period: string;
  grossAmount: number;
  netAmount: number;
  notes: string | null;
  pdfPath: string | null;
  sourceFile: string | null;
  importedAt: string;
  lineItems: SalaryLineItem[];
  baseAmount: number | null;
  hoursWorked: number | null;
  hourlyRate: number | null;
  totalEspecie: number | null;
}

export interface ParsedLineItemResponse {
  description: string;
  amount: number;
  itemType: 'income' | 'deduction' | 'tax';
  quantity: number | null;
  unitValue: number | null;
  percentage: number | null;
  incidenciaBase: number | null;
}

export interface UnifiedSalaryResult {
  pdfPath: string;
  fileName: string;
  parsed: ParsedSlipResponse;
}

export interface UnifiedUploadItemResult {
  fileName: string;
  documentType: 'BankStatement' | 'GroceryReceipt' | 'SalarySlip' | 'Unknown' | 'Micro1Unpaired';
  success: boolean;
  wasDuplicate: boolean;
  error: string | null;
  statementResult: UploadResult | null;
  groceryResult: GroceryReceiptUploadResult | null;
  salaryResult: UnifiedSalaryResult | null;
}

export interface InvestmentLot {
  id: number;
  assetId: number;
  date: string;
  quantity: number;
  pricePerUnit: number;
  fees: number | null;
  notes: string | null;
}

export interface InvestmentPriceSnapshot {
  id: number;
  assetId: number;
  date: string;
  pricePerUnit: number;
}

export interface InvestmentAsset {
  id: number;
  assetType: 'ETF' | 'Gold';
  ticker: string | null;
  name: string;
  notes: string | null;
  lots: InvestmentLot[];
  priceSnapshots: InvestmentPriceSnapshot[];
}

export interface BackfillPriceHistoryResponse {
  assetId: number;
  snapshotsAdded: number;
  snapshotsSkipped: number;
  earliestDate: string | null;
  latestDate: string | null;
  message: string;
}

export interface ParsedSlipResponse {
  parserName: string;
  employer: string;
  employerNif: string | null;
  period: string;
  grossAmount: number;
  netAmount: number;
  lineItems: ParsedLineItemResponse[];
  baseAmount: number | null;
  hoursWorked: number | null;
  hourlyRate: number | null;
  totalEspecie: number | null;
  warnings?: string[] | null;
}
