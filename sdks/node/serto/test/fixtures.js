'use strict';

// Handlers referenced by harness.test.js via file-path entrypoints. (No tests here; harmless if the
// runner loads it.)

module.exports.success = async (ctx) => {
  ctx.logger.info('ran ok');
  ctx.publish('test.subject', { k: 1 });
};

module.exports.failing = async () => {
  throw new Error('boom');
};

module.exports.secret = async (ctx) => {
  ctx.logger.info('secret=' + (ctx.secrets.API_KEY || ''));
};
