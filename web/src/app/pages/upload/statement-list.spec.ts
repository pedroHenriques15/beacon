import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { StatementListComponent } from './statement-list';
import { FinanceService } from '../../core/services/finance.service';

function makeStatement(id: number, bank: string, periodTo: string, pdfPath: string | null = 'x') {
  return {
    id,
    bank,
    periodFrom: `${periodTo.slice(0, 7)}-01`,
    periodTo,
    closingBalance: 100 * id,
    pdfPath,
    importedAt: periodTo,
    transactions: [],
  };
}

describe('StatementListComponent', () => {
  let fixture: ComponentFixture<StatementListComponent>;
  let component: StatementListComponent;
  const statements = signal<ReturnType<typeof makeStatement>[]>([]);
  const deleteStatement = vi.fn(() => of(undefined));
  const reload = vi.fn();
  const getStatementFile = vi.fn(() => of(new Blob()));

  beforeEach(() => {
    statements.set([]);
    deleteStatement.mockClear();
    reload.mockClear();
    getStatementFile.mockClear();
    TestBed.configureTestingModule({
      imports: [StatementListComponent],
      providers: [
        {
          provide: FinanceService,
          useValue: { statements, deleteStatement, reload, getStatementFile },
        },
      ],
    });
    fixture = TestBed.createComponent(StatementListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('lists every bank’s statements newest first, by bank within a date', () => {
    statements.set([
      makeStatement(1, 'Revolut', '2026-08-31'),
      makeStatement(2, 'BPI', '2026-09-30'),
      makeStatement(3, 'Activo', '2026-09-30'),
    ]);
    expect(component.statements().map((s) => s.id)).toEqual([3, 2, 1]);
  });

  it('shows six, then all of them when asked', () => {
    statements.set(
      Array.from({ length: 8 }, (_, i) => makeStatement(i + 1, 'BPI', `2026-0${i + 1}-28`)),
    );
    expect(component.shown().length).toBe(6);
    component.showAll.set(true);
    expect(component.shown().length).toBe(8);
  });

  it('asks before deleting, then deletes and reloads', () => {
    component.deleteStatement(4);
    expect(component.confirmDeleteId()).toBe(4);
    expect(deleteStatement).not.toHaveBeenCalled();

    component.onConfirmDelete();
    expect(deleteStatement).toHaveBeenCalledWith(4);
    expect(reload).toHaveBeenCalled();
    expect(component.confirmDeleteId()).toBeNull();
    expect(component.deleting()).toBeNull();
  });

  it('opens nothing for a statement without a file, and disables its button', () => {
    statements.set([makeStatement(1, 'BPI', '2026-09-30', null)]);
    fixture.detectChanges();
    component.openStatementFile(1, null);
    expect(getStatementFile).not.toHaveBeenCalled();
    const button = fixture.nativeElement.querySelector('.statement__open') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
  });

  it('shows nothing without statements', () => {
    expect(fixture.nativeElement.querySelector('.statements')).toBeNull();
  });
});
