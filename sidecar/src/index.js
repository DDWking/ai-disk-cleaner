import { startServer } from './server.js';

const port = Number(process.argv[2] || process.env.DSH_PORT || 51780);

startServer(port)
  .then(() => {
    // 约定：起来后打一行 READY，C# 等这行再发请求
    process.stdout.write(`READY ${port}\n`);
  })
  .catch((err) => {
    process.stderr.write(`FATAL ${err?.message ?? err}\n`);
    process.exit(1);
  });

process.on('uncaughtException', (err) => {
  process.stderr.write(`UNCAUGHT ${err?.message ?? err}\n`);
});
process.on('unhandledRejection', (err) => {
  process.stderr.write(`UNHANDLED ${err?.message ?? err}\n`);
});
