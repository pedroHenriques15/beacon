import { ApplicationConfig, ErrorHandler, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';

import { routes } from './app.routes';
import { apiKeyInterceptor } from './core/interceptors/api-key.interceptor';
import { googleConnectionInterceptor } from './core/interceptors/google-connection.interceptor';
import { ClientErrorHandler } from './core/services/client-error-handler';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: ErrorHandler, useClass: ClientErrorHandler },
    provideRouter(routes),
    provideHttpClient(
      withXhr(),
      withInterceptors([apiKeyInterceptor, googleConnectionInterceptor]),
    ),
  ],
};
