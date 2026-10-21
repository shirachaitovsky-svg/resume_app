// Vercel Routing Middleware: forwards /api/* to Render and adds the shared secret from env vars,
// so the secret never lives in the repo. Env vars (Vercel project settings): ORIGIN_SECRET, RENDER_API_URL.
import { rewrite } from '@vercel/functions/middleware';

export const config = { matcher: '/api/:path*' };

export default function middleware(request) {
  const target = process.env.RENDER_API_URL;
  const secret = process.env.ORIGIN_SECRET;
  if (!target || !secret) {
    return new Response('Origin not configured', { status: 503 });
  }
  const url = new URL(request.url);
  const path = url.pathname.replace(/^\/api/, '').replace(/^\/+/, '/');
  const destination = new URL(path + url.search, target);
  if (destination.origin !== new URL(target).origin) {
    return new Response('Bad request', { status: 400 });
  }
  const headers = new Headers(request.headers);
  headers.set('x-origin-secret', secret);
  return rewrite(destination, { request: { headers } });
}
