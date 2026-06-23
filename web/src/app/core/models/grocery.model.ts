export interface GroceryCategory {
  id: number;
  name: string;
  color: string;
  isProtected: boolean;
  rules: GroceryCategoryRule[];
}

export interface GroceryCategoryRule {
  id: number;
  categoryId: number;
  pattern: string | null;
  value: number | null;
}

export interface GroceryReceiptSummary {
  id: number;
  storeName: string;
  receiptDate: string;
  total: number;
  itemCount: number;
  sourceFile: string | null;
}

export interface GroceryItem {
  id: number;
  receiptId: number;
  storeName: string;
  receiptDate: string;
  description: string;
  amount: number;
  quantity: number;
  categoryId: number | null;
  categoryName: string | null;
  categoryColor: string | null;
  categorySetManually: boolean;
  isExcluded: boolean;
  receiptCategory?: string;
}

export interface PagedGroceryItemsResult {
  items: GroceryItem[];
  totalCount: number;
  totalAmount: number;
}

export interface GroceryMonthlySummary {
  month: string;
  store: string;
  total: number;
}

export interface GroceryReceiptUploadResult {
  receiptId: number;
  storeName: string;
  receiptDate: string;
  total: number;
  itemCount: number;
  wasDuplicate: boolean;
  newReceiptCategories: string[];
}

export interface GroceryReceiptCategoryMapping {
  id: number;
  receiptCategoryName: string;
  groceryCategoryId: number;
  categoryName: string;
}
