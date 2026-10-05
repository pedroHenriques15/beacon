import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ClientErrorReport, LogsPage, LogsQuery } from '../models/log-entry';
import { buildParams } from '../utils/http-params';

@Injectable({ providedIn: 'root' })
export class LogsService {
  private http = inject(HttpClient);

  /** The newest entries matching the query, newest first. */
  getLogs(query: LogsQuery): Observable<LogsPage> {
    return this.http.get<LogsPage>('/api/logs', {
      params: buildParams({
        minLevel: query.minLevel,
        from: query.from,
        search: query.search?.trim() || null,
        limit: query.limit,
      }),
    });
  }

  reportClientError(report: ClientErrorReport): Observable<void> {
    return this.http.post<void>('/api/logs/client-errors', report);
  }
}
