/** `reconnectRequired`: Google rejected the stored token. `unreachable`: Google did not answer. */
export type GoogleConnectionState =
  'notConnected' | 'connected' | 'reconnectRequired' | 'unreachable';

export interface GoogleAuthStatus {
  state: GoogleConnectionState;
  /** True for `connected` and `unreachable`: the token works, or will once Google answers. */
  connected: boolean;
  expiresAt: string | null;
  connectedAt: string | null;
}
