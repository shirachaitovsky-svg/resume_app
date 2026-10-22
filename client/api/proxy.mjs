// Vercel serverless proxy: forwards /api/* to the backend and adds the shared secret from env vars,
// so neither the secret nor the backend address lives in the repo. Env vars: BACKEND_URL, ORIGIN_SECRET.
const STRIPPED_RESPONSE_HEADERS = ['content-encoding', 'content-length'];

async function handler(request) {
  const backend = process.env.BACKEND_URL;
  const secret = process.env.ORIGIN_SECRET;
  if (!backend || !secret) {
    return Response.json({ error: 'Backend not configured' }, { status: 503 });
  }

  const url = new URL(request.url);
  const path = (url.searchParams.get('path') ?? '').replace(/^\/+/, '');
  url.searchParams.delete('path');
  const query = url.searchParams.toString();

  const base = new URL(backend);
  const destination = new URL(`${path}${query ? `?${query}` : ''}`, base.href.replace(/\/*$/, '/'));
  if (destination.origin !== base.origin) {
    return Response.json({ error: 'Bad request' }, { status: 400 });
  }

  const headers = new Headers(request.headers);
  headers.delete('host');
  headers.set('x-origin-secret', secret);

  const init = { method: request.method, headers, redirect: 'manual' };
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    init.body = await request.arrayBuffer();
  }

  const upstream = await fetch(destination, init);
  const responseHeaders = new Headers(upstream.headers);
  for (const name of STRIPPED_RESPONSE_HEADERS) responseHeaders.delete(name);
  return new Response(upstream.body, { status: upstream.status, statusText: upstream.statusText, headers: responseHeaders });
}

export const GET = handler;
export const POST = handler;
export const PUT = handler;
export const PATCH = handler;
export const DELETE = handler;
export const OPTIONS = handler;
