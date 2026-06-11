'use strict';

const { test } = require('node:test');
const assert = require('node:assert');
const path = require('path');
const { Readable } = require('node:stream');
const { run, main } = require('../index.js');

const FIXTURES = path.join(__dirname, 'fixtures.js');

function invocation(name, overrides = {}) {
  return {
    protocolVersion: '1',
    entrypoint: `${FIXTURES}#${name}`,
    execution: { environment: 'production', integrationName: 'Test' },
    trigger: { type: 'manual' },
    payload: null,
    secrets: {},
    ...overrides,
  };
}

async function runEvents(name, overrides) {
  const events = [];
  await run(invocation(name, overrides), (e) => events.push(e));
  return events;
}

test('logs and publishes', async () => {
  const events = await runEvents('success');
  assert.ok(events.some((e) => e.type === 'log' && e.level === 'Information' && e.message === 'ran ok'));
  const message = events.find((e) => e.type === 'message');
  assert.equal(message.subject, 'test.subject');
  assert.deepEqual(JSON.parse(message.body), { k: 1 });
});

test('handler error rejects', async () => {
  await assert.rejects(() => runEvents('failing'), /boom/);
});

test('secrets are available', async () => {
  const events = await runEvents('secret', { secrets: { API_KEY: 'xyz' } });
  assert.ok(events.some((e) => (e.message || '').includes('secret=xyz')));
});

test('main emits a success result', async () => {
  let output = '';
  const stdout = { write: (chunk) => { output += chunk; return true; } };
  const stdin = Readable.from([JSON.stringify(invocation('success'))]);

  await main({ stdin, stdout });

  const events = output.trim().split('\n').map((line) => JSON.parse(line));
  const last = events[events.length - 1];
  assert.equal(last.type, 'result');
  assert.equal(last.succeeded, true);
});

test('main emits a failed result on handler error', async () => {
  let output = '';
  const stdout = { write: (chunk) => { output += chunk; return true; } };
  const stdin = Readable.from([JSON.stringify(invocation('failing'))]);

  await main({ stdin, stdout });

  const events = output.trim().split('\n').map((line) => JSON.parse(line));
  const last = events[events.length - 1];
  assert.equal(last.type, 'result');
  assert.equal(last.succeeded, false);
  assert.match(last.error, /boom/);
});
