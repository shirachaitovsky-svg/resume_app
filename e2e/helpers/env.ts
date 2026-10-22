// Dedicated ports and secrets for the isolated e2e environment. Nothing here touches the dev database (`resume`) or the dev ports 5000 and 4200.
export const SERVER_PORT = 5100;
export const CLIENT_PORT = 4300;
export const SERVER_URL = `http://localhost:${SERVER_PORT}`;
export const BASE_URL = `http://localhost:${CLIENT_PORT}`;
export const MAILPIT_URL = process.env.MAILPIT_URL ?? 'http://localhost:8025';
export const PG_CONTAINER = process.env.E2E_PG_CONTAINER ?? 'resume-dev-pg';
export const PG_USER = process.env.E2E_PG_USER ?? 'postgres';
export const PG_PASSWORD = process.env.E2E_PG_PASSWORD ?? 'dev';
export const PG_HOST_PORT = Number(process.env.E2E_PG_PORT ?? 5433);
export const DB_NAME = 'resume_e2e';

// The setup drops this database on every run, so it refuses to run against any other name.
if (DB_NAME !== 'resume_e2e') throw new Error(`Refusing to run: the e2e database must be named resume_e2e, got ${DB_NAME}.`);

// The only mailboxes the scenario touches (participant, visitor and the server's default admin notification address).
export const PARTICIPANT_EMAIL = 'dana.participant@example.test';
export const VISITOR_EMAIL = 'visitor@example.test';
export const SCENARIO_ADDRESSES = [PARTICIPANT_EMAIL, VISITOR_EMAIL, 'admin@example.test'];
export const ORIGIN_SECRET = 'e2e-origin-secret';
export const ADMIN_USERNAME = 'e2e-admin';
export const ADMIN_INITIAL_PASSWORD = 'E2e-initial-Password-1';
export const ADMIN_NEW_PASSWORD = 'E2e-changed-Password-2';
export const DATABASE_URL = `postgres://${PG_USER}:${PG_PASSWORD}@localhost:${PG_HOST_PORT}/${DB_NAME}`;
