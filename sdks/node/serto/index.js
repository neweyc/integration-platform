'use strict';

// The Serto Node.js SDK. An integration exports a handler that takes a Context:
//
//   module.exports.handler = async (ctx) => {
//     ctx.logger.info('running');
//     await ctx.publish('orders.synced', { count: 42 });
//   };
//
// Declare it in serto.json with an entrypoint of `index.js#handler`. The agent launches the harness
// (bin/serto-runtime.js), sends the invocation on stdin, and reads wire-protocol events from stdout.
// See docs/multi-language-runtimes.md.

const path = require('path');

class Logger {
  constructor(emit) {
    this._emit = emit;
  }

  _log(level, message) {
    this._emit({ type: 'log', level, message: String(message) });
  }

  trace(message) { this._log('Trace', message); }
  debug(message) { this._log('Debug', message); }
  info(message) { this._log('Information', message); }
  warn(message) { this._log('Warning', message); }
  error(message) { this._log('Error', message); }
  critical(message) { this._log('Critical', message); }
}

class Context {
  constructor(invocation, emit) {
    this._emit = emit;
    this.secrets = invocation.secrets || {};
    this.payload = invocation.payload != null ? invocation.payload : null;
    this.trigger = invocation.trigger || {};
    this.execution = invocation.execution || {};
    this.logger = new Logger(emit);
  }

  payloadJson() {
    return this.payload ? JSON.parse(this.payload) : null;
  }

  publish(subject, body) {
    const encoded = typeof body === 'string' ? body : JSON.stringify(body);
    this._emit({ type: 'message', subject, body: encoded });
  }
}

// Resolves an entrypoint string into the handler function. Forms: `file.js#export` (preferred) or
// `file.js:export`. The file is resolved relative to the working directory (the package directory).
function resolveEntrypoint(spec) {
  if (!spec) {
    throw new Error("Entrypoint is required, e.g. 'index.js#handler'");
  }

  const separator = spec.includes('#') ? '#' : ':';
  const index = spec.lastIndexOf(separator);
  if (index < 0) {
    throw new Error(`Invalid entrypoint '${spec}', expected 'file.js#handler'`);
  }

  const file = spec.slice(0, index);
  const name = spec.slice(index + 1);
  const module_ = require(path.resolve(process.cwd(), file));

  const handler = module_[name];
  if (typeof handler !== 'function') {
    throw new Error(`Entrypoint '${spec}' does not export a function named '${name}'`);
  }
  return handler;
}

// Resolves and runs the integration. Throws on failure; the caller maps that to a result event. Supports
// both sync and async handlers.
async function run(invocation, emit) {
  const handler = resolveEntrypoint(invocation.entrypoint || '');
  const ctx = new Context(invocation, emit);
  await handler(ctx);
}

function readAll(stream) {
  return new Promise((resolve, reject) => {
    let data = '';
    stream.setEncoding('utf8');
    stream.on('data', (chunk) => { data += chunk; });
    stream.on('end', () => resolve(data));
    stream.on('error', reject);
  });
}

// Entry point for the harness (bin/serto-runtime.js). Reads the invocation, runs it, emits the result.
// streams are injectable for testing. The integration's own console output is redirected to stderr so a
// stray console.log can never corrupt the JSON-lines protocol channel.
async function main(streams = {}) {
  const out = streams.stdout || process.stdout;
  const input = streams.stdin || process.stdin;
  const emit = (event) => out.write(JSON.stringify(event) + '\n');

  const toStderr = (...args) => process.stderr.write(args.map(String).join(' ') + '\n');
  console.log = toStderr;
  console.info = toStderr;
  console.debug = toStderr;

  let raw = '';
  try {
    raw = await readAll(input);
  } catch (_) {
    // fall through; JSON.parse of '' reports the failure as a result below
  }

  try {
    const invocation = JSON.parse(raw);
    await run(invocation, emit);
    emit({ type: 'result', succeeded: true, error: null });
  } catch (err) {
    emit({ type: 'result', succeeded: false, error: err && err.stack ? err.stack : String(err) });
  }
}

module.exports = { Context, Logger, run, main, resolveEntrypoint };
