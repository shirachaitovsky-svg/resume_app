// Local dev only: forwards /api/* to the .NET server (http://localhost:5000) and adds the shared secret.
const secret = process.env.ORIGIN_SECRET;
if (!secret) {
  console.warn('WARNING: ORIGIN_SECRET is not set; /api requests will be rejected (403) by the server.');
}
module.exports = {
  '/api': {
    target: 'http://localhost:5000',
    pathRewrite: { '^/api': '' },
    changeOrigin: true,
    headers: { 'x-origin-secret': secret ?? '' },
  },
};
