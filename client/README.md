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
