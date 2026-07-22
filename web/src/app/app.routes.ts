import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  {
    path: 'dashboard',
    title: 'Dashboard · Beacon',
    loadComponent: () => import('./pages/dashboard/dashboard').then((m) => m.DashboardComponent),
  },
  {
    path: 'transactions',
    title: 'Transactions · Beacon',
    loadComponent: () =>
      import('./pages/transactions/transactions').then((m) => m.TransactionsComponent),
  },
  {
    path: 'analytics',
    title: 'Analytics · Beacon',
    loadComponent: () => import('./pages/analytics/analytics').then((m) => m.AnalyticsComponent),
  },
  {
    path: 'rules',
    title: 'Categories · Beacon',
    loadComponent: () => import('./pages/rules/rules').then((m) => m.RulesComponent),
  },
  {
    path: 'upload',
    title: 'Upload · Beacon',
    loadComponent: () => import('./pages/upload/upload').then((m) => m.UploadComponent),
  },
  {
    path: 'salary',
    title: 'Salary · Beacon',
    loadComponent: () => import('./pages/salary/salary').then((m) => m.SalaryComponent),
  },
  {
    path: 'settings',
    title: 'Settings · Beacon',
    loadComponent: () => import('./pages/settings/settings').then((m) => m.SettingsComponent),
  },
  {
    path: 'calendar',
    title: 'Calendar · Beacon',
    loadComponent: () => import('./pages/calendar/calendar').then((m) => m.CalendarPage),
  },
  { path: '**', redirectTo: 'dashboard' },
];
