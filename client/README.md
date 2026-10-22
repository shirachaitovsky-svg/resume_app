# Client

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 22.2.3.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Running the app

```bash
npm install
npm start   # ng serve with proxy.conf.js: /api/* is forwarded to http://localhost:5000
```

Set `ORIGIN_SECRET` before `npm start`; the proxy adds it as `x-origin-secret` and strips the `/api` prefix. The client only calls relative `/api/...` paths. Verify with `npm run build` and `npm test -- --watch=false`.

## Source layout (`src/app/`)

- `app.ts|html|css` -- shell: skip link, floating nav (`nav-pill`), single `main` with `router-outlet`.
- `app.routes.ts`, `app.config.ts` -- lazy routes (`''`, `about`, `join`, `edit`, `design-system`, `**` not found), HTTP, title strategy.
- `pages/` -- static pages (home placeholder until story 5.7, about, join, edit, not-found).
- `shared/api/` -- `ApiClient` (get/post/put/delete/postMultipart; CSRF token and credentials on mutations, optional Turnstile via `{ turnstile: true }`), `CsrfService`, `TURNSTILE` injection token (the dev implementation returns `dev-turnstile-token`; replace the provider with the real widget), `AppError` (normalized errors with Hebrew messages).
- `shared/ui/` -- design-system components: button, chip, field, glass-card, nav-pill, loading-skeleton, empty-state, error-state, info-page.
- `shared/strings.he.ts` -- the only place for UI strings.
- `design-system/` -- demo page at `/design-system`.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Vitest](https://vitest.dev/) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
