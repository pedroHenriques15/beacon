import { Component, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';

export interface NavItem {
  path: string;
  label: string;
  /** SVG path data on a 24 × 24 grid, drawn with a 1.8 px stroke. */
  icon: string;
}

const ICONS = {
  home: 'M4 11l8-6 8 6v8a1 1 0 0 1-1 1h-4v-6h-6v6H5a1 1 0 0 1-1-1z',
  activity:
    'M3 8c3 0 3-2 6-2s3 2 6 2 3-2 6-2M3 13c3 0 3-2 6-2s3 2 6 2 3-2 6-2M3 18c3 0 3-2 6-2s3 2 6 2 3-2 6-2',
  insights: 'M4 4v16h16M8 15c2-5 4-5 6-2s3 1 5-5',
  invest: 'M3 17l6-6 4 4 8-8M15 7h6v6',
  salary:
    'M6 6h12a3 3 0 0 1 3 3v6a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3V9a3 3 0 0 1 3-3zM14.5 12a2.5 2.5 0 1 1-5 0 2.5 2.5 0 1 1 5 0z',
  calendar:
    'M6.5 5h11a3 3 0 0 1 3 3v9a3 3 0 0 1-3 3h-11a3 3 0 0 1-3-3V8a3 3 0 0 1 3-3zM3.5 10h17M8 3v4M16 3v4',
  categories: 'M20.6 13.4l-7.2 7.2a2 2 0 0 1-2.8 0L3 13V3h10l7.6 7.6a2 2 0 0 1 0 2.8zM7.5 7.5h.01',
  settings:
    'M15 12a3 3 0 1 1-6 0 3 3 0 1 1 6 0zM19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z',
};

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './app.scss',
})
export class App {
  private readonly router = inject(Router);

  readonly icons = ICONS;

  /** The phone bottom nav; the desktop pill group adds Categories. */
  readonly mainNav: NavItem[] = [
    { path: '/dashboard', label: 'Home', icon: ICONS.home },
    { path: '/transactions', label: 'Activity', icon: ICONS.activity },
    { path: '/analytics', label: 'Insights', icon: ICONS.insights },
    { path: '/investments', label: 'Invest', icon: ICONS.invest },
    { path: '/salary', label: 'Salary', icon: ICONS.salary },
    { path: '/calendar', label: 'Calendar', icon: ICONS.calendar },
  ];

  /** Below 1024 px these sit behind the "More" button. */
  readonly moreNav: NavItem[] = [
    { path: '/rules', label: 'Categories', icon: ICONS.categories },
    { path: '/settings', label: 'Settings', icon: ICONS.settings },
  ];

  /** The desktop pill group; Settings is an icon button beside it. */
  readonly desktopNav: NavItem[] = [...this.mainNav, this.moreNav[0]];

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  /** The current route's `data.label`, the page title in the phone top bar. */
  readonly pageLabel = computed(() => {
    this.url();
    let route = this.router.routerState.snapshot.root;
    while (route.firstChild) route = route.firstChild;
    return (route.data['label'] as string | undefined) ?? 'Beacon';
  });

  readonly onUploadPage = computed(() => this.url().startsWith('/upload'));

  readonly moreOpen = signal(false);

  toggleMore(): void {
    this.moreOpen.update((v) => !v);
  }

  closeMore(): void {
    this.moreOpen.set(false);
  }
}
