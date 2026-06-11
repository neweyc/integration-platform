'use strict';

module.exports.handler = async (ctx) => {
  ctx.logger.info(`Hello from Node — environment: ${ctx.execution.environment}`);

  if (ctx.secrets.API_KEY) {
    ctx.logger.info(`API_KEY is present (${ctx.secrets.API_KEY.length} chars)`);
  } else {
    ctx.logger.warn('API_KEY is not configured');
  }

  if (ctx.payload) {
    ctx.logger.info(`Received payload: ${ctx.payload}`);
  }

  await ctx.publish('node.greeted', { greeted: true, environment: ctx.execution.environment });
};
