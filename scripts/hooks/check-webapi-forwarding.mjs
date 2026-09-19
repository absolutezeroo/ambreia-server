#!/usr/bin/env node
// The web API runs inside the Orleans host but builds its OWN container, and only the services
// WebApiWebHost.ForwardSingletons hands over exist in it. WebApiTestFactory then keeps a second copy
// of that list by hand, with fakes. Three lists, nothing comparing them.
//
// Forgetting one is invisible in two different ways depending on the annotation, and only one of them
// is loud:
//
//   plain parameter    a minimal API reads an unregistered type as the REQUEST BODY. A GET may not
//                      have one, so the route table fails to build and the whole API dies at
//                      startup. Impossible to miss.
//   [FromServices]     the route table builds fine and that one endpoint answers 500, per request,
//                      forever. Nothing fails until somebody loads the page.
//
// This checks the second kind, which is the one that ships. It has now happened three times:
// IChainService (both /api/public/chain routes, 500), IAuditSink, and ICameraPhotoStore -- a photo
// that was taken, stored and billed, and served as an error.
//
//   node scripts/hooks/check-webapi-forwarding.mjs
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '../..');

const hostingDir = join(root, 'Vortex.WebApi');
const hostPath = join(root, 'Vortex.WebApi/Hosting/WebApiWebHost.cs');
const factoryPath = join(root, 'Vortex.WebApi.Tests/WebApiTestFactory.cs');

// Supplied by a line no type-argument regex can see. Kept explicit rather than loosening the regex:
// a list of two with a reason each beats a pattern that quietly matches more than it should.
const SUPPLIED_UNTYPED = new Map([
  ['IOptions<WebApiConfig>', 'WebApiWebHost passes the IOptions instance itself: services.AddSingleton(options)'],
]);

function walk(dir, out = []) {
  for (const entry of readdirSync(dir)) {
    if (entry === 'bin' || entry === 'obj' || entry === 'node_modules' || entry === '.git') continue;

    const full = join(dir, entry);
    if (statSync(full).isDirectory()) walk(full, out);
    else if (entry.endsWith('.cs')) out.push(full);
  }
  return out;
}

// Comment lines go first: this file's own header quotes [FromServices] while explaining it, and so do
// two of the endpoints.
const strip = (source) => source.replace(/^\s*(\/\/|\*|\/\*).*$/gm, '');

const demanded = new Map();

for (const file of walk(hostingDir)) {
  const source = strip(readFileSync(file, 'utf8'));

  // [FromServices] IFoo<Bar> name -- the type is everything up to the parameter name.
  for (const m of source.matchAll(/\[FromServices\]\s+([\w.]+(?:<[^>]+>)?)\s+\w+/g)) {
    // Namespace-qualified at the call site or not, it is the same registration.
    const type = m[1].split('.').pop();

    if (!demanded.has(type)) demanded.set(type, relative(root, file).replace(/\\/g, '/'));
  }
}

function suppliedBy(path, label) {
  let source;

  try {
    source = strip(readFileSync(path, 'utf8'));
  } catch {
    console.error(`check-webapi-forwarding: ${label} introuvable (${relative(root, path)}).`);
    process.exit(2);
  }

  const supplied = new Set();

  // GetRequiredService<IFoo>() in the forwarding list, AddSingleton<IFoo>(...) in either, and
  // AddSingleton<IFoo, Impl>() for good measure -- only the first type argument is the contract an
  // endpoint asks for.
  for (const m of source.matchAll(/(?:GetRequiredService|AddSingleton|AddScoped|AddTransient)<\s*([\w.]+(?:<[^>]+>)?)/g)) {
    supplied.add(m[1].split('.').pop());
  }

  return supplied;
}

const inHost = suppliedBy(hostPath, 'WebApiWebHost');
const inFactory = suppliedBy(factoryPath, 'WebApiTestFactory');

const missing = [];

for (const [type, where] of demanded) {
  if (SUPPLIED_UNTYPED.has(type)) continue;

  const gaps = [];
  if (!inHost.has(type)) gaps.push('WebApiWebHost.ForwardSingletons (500 en production)');
  if (!inFactory.has(type)) gaps.push('WebApiTestFactory (aucun test ne peut couvrir la route)');

  if (gaps.length) missing.push({ type, where, gaps });
}

if (!missing.length) {
  console.log(
    `check-webapi-forwarding: OK (${demanded.size} service(s) [FromServices], tous transferes).`
  );
  process.exit(0);
}

for (const { type, where, gaps } of missing) {
  console.error(`check-webapi-forwarding: ${type} (${where}) manque dans :`);
  for (const gap of gaps) console.error(`  - ${gap}`);
}

console.error(
  '\nUn service [FromServices] absent du conteneur ne casse pas le demarrage : la route repond 500,'
);
console.error('a chaque requete, sans rien dans les logs de boot.');
process.exit(2);
