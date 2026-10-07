// F07: read-only smoke on the user's deployment; validate hostname and the supplied local CA.
import https from 'node:https';
import http from 'node:http';
import { readFileSync } from 'node:fs';
import assert from 'node:assert/strict';

const [caPath, httpsPort, httpPort] = process.argv.slice(2);
const ca = readFileSync(caPath);
async function request(secure, path) {
  return new Promise((resolve, reject) => {
    const req = (secure ? https : http).get({
      hostname: 'localhost', family: 4, port: secure ? Number(httpsPort) : Number(httpPort),
      path, ...(secure ? { ca, rejectUnauthorized: true } : {}), timeout: 15000,
    }, res => {
      let body = '';
      res.setEncoding('utf8');
      res.on('data', part => { body += part; });
      res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body }));
    });
    req.on('timeout', () => req.destroy(new Error('CD smoke timeout')));
    req.on('error', reject);
  });
}
const ready = await request(true, '/health/ready');
assert.equal(ready.status, 200);
assert.equal(ready.body, 'Ready');
const live = await request(true, '/health/live');
assert.equal(live.status, 200);
assert.equal(live.body, 'Healthy');
const catalog = await request(true, '/Catalog');
assert.equal(catalog.status, 200);
assert.ok(catalog.headers['content-security-policy']);
assert.equal(catalog.headers['x-content-type-options'], 'nosniff');
assert.equal(catalog.headers['x-frame-options'], 'DENY');
const redirect = await request(false, '/Catalog');
assert.equal(redirect.status, 308);
assert.equal(redirect.headers.location, `https://localhost:${httpsPort}/Catalog`);
console.log('PASS: read-only CD smoke (TLS CA/hostname, SQL/Redis readiness, catalog, security headers, HTTPS redirect).');
