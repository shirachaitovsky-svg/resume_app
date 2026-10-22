import { ChildProcess, execFileSync, spawn } from 'node:child_process';
import { mkdirSync, openSync } from 'node:fs';
import { connect } from 'node:net';
import { join, resolve } from 'node:path';
import {
  ADMIN_INITIAL_PASSWORD,
  ADMIN_USERNAME,
  BASE_URL,
  CLIENT_PORT,
  DATABASE_URL,
  DB_NAME,
  MAILPIT_URL,
  ORIGIN_SECRET,
  PG_CONTAINER,
  PG_USER,
  SERVER_PORT,
  SERVER_URL,
} from './helpers/env';
import { clearMailbox } from './helpers/mailpit';

const root = resolve(__dirname, '..');
const logDir = join(__dirname, 'test-results');
const children: ChildProcess[] = [];

function fail(step: string, detail: string): never {
  throw new Error(`e2e setup failed at "${step}": ${detail}`);
}

function psql(sql: string): void {
  try {
    execFileSync('docker', ['exec', PG_CONTAINER, 'psql', '-U', PG_USER, '-v', 'ON_ERROR_STOP=1', '-c', sql], { stdio: 'pipe' });
  } catch (e) {
    fail('database', `docker exec ${PG_CONTAINER} psql failed. Is docker running and the container up? ${(e as Error).message}`);
  }
}

/** A port is busy when anything accepts a TCP connection on it (HTTP or not). */
async function ensureFree(port: number): Promise<void> {
  const busy = await new Promise<boolean>((resolveBusy) => {
    const socket = connect({ port, host: 'localhost' });
    socket.once('connect', () => {
      socket.destroy();
      resolveBusy(true);
    });
    socket.once('error', () => resolveBusy(false));
  });
  if (busy) fail('ports', `port ${port} is already in use; the e2e run needs it free (it never kills other processes).`);
}

async function waitFor(url: string, step: string, headers: Record<string, string> = {}, timeoutMs = 240_000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  let last = '';
  while (Date.now() < deadline) {
    for (const child of children) {
      if (child.exitCode !== null) fail(step, `a child process exited early with code ${child.exitCode}; see ${logDir}`);
    }
    try {
      const res = await fetch(url, { headers, signal: AbortSignal.timeout(3000) });
      if (res.ok) return;
      last = `HTTP ${res.status}`;
    } catch (e) {
      last = (e as Error).message;
    }
    await new Promise((r) => setTimeout(r, 1000));
  }
  fail(step, `${url} did not become ready in ${timeoutMs} ms (${last}); see ${logDir}`);
}

function start(name: string, command: string, args: string[], cwd: string, env: NodeJS.ProcessEnv): ChildProcess {
  const log = openSync(join(logDir, `${name}.log`), 'w');
  const child = spawn(command, args, { cwd, env: { ...process.env, ...env }, stdio: ['ignore', log, log], windowsHide: true, detached: process.platform !== 'win32' });
  children.push(child);
  return child;
}

function stop(child: ChildProcess): void {
  if (child.pid === undefined || child.exitCode !== null) return;
  try {
    if (process.platform === 'win32') execFileSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' });
    else process.kill(-child.pid, 'SIGTERM');
  } catch {
    // already gone
  }
}

export default async function globalSetup(): Promise<() => Promise<void>> {
  mkdirSync(logDir, { recursive: true });
  const teardown = async () => {
    for (const child of children) stop(child);
  };
  // Ctrl+C or a kill of the runner must not leave the servers behind.
  for (const signal of ['SIGINT', 'SIGTERM'] as const) {
    process.once(signal, () => {
      for (const child of children) stop(child);
      process.exit(1);
    });
  }
  try {
    await ensureFree(SERVER_PORT);
    await ensureFree(CLIENT_PORT);

    // Mailpit must be reachable: the scenario reads every mail through its API.
    try {
      await clearMailbox();
    } catch (e) {
      fail('mailpit', `${MAILPIT_URL} is not reachable (${(e as Error).message}). Start it with: docker compose -f docker-compose.dev.yml up -d mailpit`);
    }

    // A fresh database per run, inside the existing dev Postgres container; the dev database `resume` is never touched.
    psql(`DROP DATABASE IF EXISTS ${DB_NAME} WITH (FORCE)`);
    psql(`CREATE DATABASE ${DB_NAME}`);

    const serverEnv = {
      ASPNETCORE_ENVIRONMENT: 'Development',
      GOOGLE_PROVIDER: 'fake',
      ORIGIN_SECRET,
      ADMIN_USERNAME,
      ADMIN_INITIAL_PASSWORD,
      DATABASE_URL,
      PUBLIC_BASE_URL: BASE_URL,
      ALLOWED_ORIGINS: BASE_URL,
    };
    start('server', 'dotnet', ['run', '--project', join(root, 'server', 'Api'), '--no-launch-profile', '--urls', SERVER_URL], root, serverEnv);
    await waitFor(`${SERVER_URL}/healthz`, 'server', { 'x-origin-secret': ORIGIN_SECRET });

    const ng = join(root, 'client', 'node_modules', '@angular', 'cli', 'bin', 'ng.js');
    start(
      'client',
      process.execPath,
      [ng, 'serve', '--port', String(CLIENT_PORT), '--proxy-config', 'proxy.conf.js'],
      join(root, 'client'),
      { ORIGIN_SECRET, API_TARGET: SERVER_URL, NG_CLI_ANALYTICS: 'false' },
    );
    await waitFor(`${BASE_URL}/`, 'client');
  } catch (e) {
    await teardown();
    throw e;
  }
  return teardown;
}
