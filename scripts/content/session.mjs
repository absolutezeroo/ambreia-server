// What push.mjs and pull.mjs share: their options, failing, and the web session they sign in with.
// The password is read from an environment variable and never printed.

export const COOKIE = 'habbo-web-session';

export function option(name, fallback) {
  const i = process.argv.indexOf(`--${name}`);
  return i > 0 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
}

export function flag(name) {
  return process.argv.includes(`--${name}`);
}

export function fail(message) {
  console.error(message);
  process.exit(1);
}

/** The base URL and a session cookie of an account with content.publish (the owner role). */
export async function signIn() {
  const base = option('base', 'http://127.0.0.1:8080');
  const email = option('email', process.env.VORTEX_CONTENT_EMAIL);
  const passwordEnv = option('password-env', 'VORTEX_CONTENT_PASSWORD');
  const password = process.env[passwordEnv];
  if (!email) fail('No account: pass --email or set VORTEX_CONTENT_EMAIL.');
  if (!password) fail(`No password: set the environment variable ${passwordEnv}.`);

  let res;
  try {
    res = await fetch(`${base}/api/public/authentication/login`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ email, password }),
    });
  } catch (e) {
    fail(`Cannot reach ${base}: ${e.message}`);
  }
  const cookie = res.headers.getSetCookie().find((c) => c.startsWith(`${COOKIE}=`));
  if (!cookie) fail(`Login refused for ${email}: HTTP ${res.status}.`);
  return { base, cookie: cookie.split(';')[0] };
}
