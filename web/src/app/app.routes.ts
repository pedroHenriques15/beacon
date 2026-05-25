import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  {
    path: 'dashboard',
    loadComponent: () => import('./pages/dashboard/dashboard').then((m) => m.DashboardComponent),
  },
  {
    path: 'transactions',
    loadComponent: () =>
      import('./pages/transactions/transactions').then((m) => m.TransactionsComponent),
  },
  {
    path: 'analytics',
    loadComponent: () => import('./pages/analytics/analytics').then((m) => m.AnalyticsComponent),
  },
  {
    path: 'rules',
    loadComponent: () => import('./pages/rules/rules').then((m) => m.RulesComponent),
  },
  {
    path: 'upload',
    loadComponent: () => import('./pages/upload/upload').then((m) => m.UploadComponent),
  },
  {
    path: 'salary',
    loadComponent: () => import('./pages/salary/salary').then((m) => m.SalaryComponent),
  },
  {
    path: 'settings',
    loadComponent: () => import('./pages/settings/settings').then((m) => m.SettingsComponent),
  },
];
