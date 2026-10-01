/** Serilog's level names, lowest first. */
export type LogLevel = 'Verbose' | 'Debug' | 'Information' | 'Warning' | 'Error' | 'Fatal';

export interface LogEntry {
  timestamp: string;
  level: LogLevel;
  message: string;
  /** The logger's category: the class that logged it, or Beacon.Client for a client error. */
  source: string | null;
  /** The exception, or the stack of a client error. */
  details: string | null;
}

export interface LogsPage {
  /** False when the server writes no log files. */
  enabled: boolean;
  entries: LogEntry[];
  /** Older entries matched beyond the limit. */
  more: boolean;
}

export interface LogsQuery {
  minLevel?: LogLevel;
  from?: string | null;
  search?: string | null;
  limit?: number;
}

export interface ClientErrorReport {
  message: string;
  stack: string | null;
  route: string;
}
