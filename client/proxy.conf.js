// Local dev only: forwards /api/* to the .NET server (API_TARGET, default http://localhost:5000) and adds the shared secret.
const secret = process.env.ORIGIN_SECRET;
if (!secret) {
  console.warn('WARNING: ORIGIN_SECRET is not set; /api requests will be rejected (403) by the server.');
}
module.exports = {
  '/api': {
    target: process.env.API_TARGET || 'http://localhost:5000',
    pathRewrite: { '^/api': '' },
    changeOrigin: true,
    // The edge (Vercel) terminates TLS and tells the server so; without this the server (which only issues Secure cookies and anti-forgery tokens) throws on plain http.
    headers: { 'x-origin-secret': secret ?? '', 'x-forwarded-proto': 'https' },
  },
};
