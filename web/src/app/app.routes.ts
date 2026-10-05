import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  {
    path: 'dashboard',
    title: 'Home · Beacon',
    data: { label: 'Home' },
    loadComponent: () => import('./pages/dashboard/dashboard').then((m) => m.DashboardComponent),
  },
  {
    path: 'transactions',
    title: 'Activity · Beacon',
    data: { label: 'Activity' },
    loadComponent: () =>
      import('./pages/transactions/transactions').then((m) => m.TransactionsComponent),
  },
  {
    path: 'analytics',
    title: 'Insights · Beacon',
    data: { label: 'Insights' },
    loadComponent: () => import('./pages/analytics/analytics').then((m) => m.AnalyticsComponent),
  },
  {
    path: 'rules',
    title: 'Categories · Beacon',
    data: { label: 'Categories' },
    loadComponent: () => import('./pages/rules/rules').then((m) => m.RulesComponent),
  },
  {
    path: 'upload',
    title: 'Upload · Beacon',
    data: { label: 'Upload' },
    loadComponent: () => import('./pages/upload/upload').then((m) => m.UploadComponent),
  },
  {
    path: 'salary',
    title: 'Salary · Beacon',
    data: { label: 'Salary' },
    loadComponent: () => import('./pages/salary/salary').then((m) => m.SalaryComponent),
  },
  {
    path: 'settings',
    title: 'Settings · Beacon',
    data: { label: 'Settings' },
    loadComponent: () => import('./pages/settings/settings').then((m) => m.SettingsComponent),
  },
  {
    path: 'calendar',
    title: 'Calendar · Beacon',
    data: { label: 'Calendar' },
    loadComponent: () => import('./pages/calendar/calendar').then((m) => m.CalendarPage),
  },
  {
    path: 'investments',
    title: 'Invest · Beacon',
    data: { label: 'Invest' },
    loadComponent: () =>
      import('./pages/investments/investments').then((m) => m.InvestmentsComponent),
  },
  { path: '**', redirectTo: 'dashboard' },
];
