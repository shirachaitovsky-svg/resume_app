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
- `pages/gallery/` -- home page (`/`): public gallery with cohort chips (`?cohort={id}`), profile cards, loading/empty/error states. `pages/profile/` -- public profile (`/profile/:id`) and the "profile unavailable" state. `pages/` also holds the static pages (about, join, edit, not-found).
- `shared/api/` -- `ApiClient` (get/post/put/delete/postMultipart; CSRF token and credentials on mutations, optional Turnstile via `{ turnstile: true }`), `CsrfService`, `TURNSTILE` injection token (the dev implementation returns `dev-turnstile-token`; replace the provider with the real widget), `AppError` (normalized errors with Hebrew messages).
- `shared/ui/` -- design-system components: button, chip, field, glass-card, nav-pill, loading-skeleton, empty-state, error-state, info-page, avatar (`AvatarImage`: photo or one of six illustrations), safe-markdown (`SafeMarkdown`: restricted Markdown rendered as Angular elements, no innerHTML).
- `shared/strings.he.ts` -- the only place for UI strings.
- `design-system/` -- demo page at `/design-system`.

## Admin area (`/admin`)

Lazy routes: `/admin/login`, `/admin/setup` (one-time password change, then TOTP enrollment with a QR code drawn client-side and the ten recovery codes), `/admin` (landing page with navigation) and `/admin/cohorts` (create a cohort, copy its invite link, revoke it behind the shared `app-confirm-dialog`; story 5.6 adds the review screens). `pages/admin/admin-session.ts` keeps the session in memory only (`GET /auth/me` on load; a 401 means signed out) and `admin.guard.ts` sends each state to its step. After sign-in the client switches `CsrfService` to `/api/auth/csrf`, because the anti-forgery token is bound to the user (the public `/api/profiles/csrf` token is for anonymous requests); sign-out switches back. The only added dependency is `qrcode-generator` (pinned), used for the QR matrix; the SVG itself is rendered by Angular.

Signing in during development:

1. Start the server with `ADMIN_USERNAME` and `ADMIN_INITIAL_PASSWORD` set (and `ORIGIN_SECRET`, see above) so the first admin is seeded; run `npm start`.
2. Open http://localhost:4200/admin/login and sign in with the one-time password (no code is needed the first time), choose a new password, scan the QR code with an authenticator app, enter the 6-digit code and save the recovery codes (shown once).
3. Later sign-ins need the 6-digit code, or a recovery code via "use a recovery code".
4. State-changing requests need a matching `Origin`. Through `ng serve` the proxy uses `changeOrigin`; if the server answers 403 for the Origin, add `http://localhost:4200` to the server's `ALLOWED_ORIGINS`.

## Sample data for the gallery (dev only)

The gallery is empty on a fresh database. With the `resume-dev-pg` container running (see the root README), seed sample cohorts and profiles:

```bash
powershell -File ../scripts/dev-seed.ps1   # run from client/; idempotent, touches only the seed rows
```

Then run `npm start` and open http://localhost:4200/. The seed includes one removed and one pending profile to show they are not listed.

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

## Profile submission form

`/join/:token` shows the participant submission form (name, product name, product URL, email, phone). It validates the invite via `GET /invites/{token}` and submits to `POST /invites/{token}/profiles` with a Turnstile token (dev token in development). To try it against the dev server, create a cohort and an invite link through the admin screen or the admin API, then open `/join/<token>` in the browser; a successful submission creates a `pending` profile. `/join` without a token remains the static explanation page.

The form also has an "optional" section (all fields may stay empty): short description (120 chars, live counter), role, workplace, intro text and three intro answers, a girl/boy avatar picker, a photo upload and a Markdown CV with a safe live preview and a privacy warning, plus a live preview of the gallery card. A chosen photo is resized in the browser (long side up to 800px, JPEG about 300KB, never over 400KB) and sent as `multipart/form-data` (`photo` part); without a photo the request stays JSON. Empty optional fields are not sent.
