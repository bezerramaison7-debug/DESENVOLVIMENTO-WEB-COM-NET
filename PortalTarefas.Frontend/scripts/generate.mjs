import openapiTS, {astToString} from 'openapi-typescript';
import {readFile,writeFile} from 'node:fs/promises';

const schema = JSON.parse(await readFile('../evidencias/openapi.json', 'utf8'));
await writeFile('src/contrato.ts', astToString(await openapiTS(schema)));
const paths = Object.entries(schema.paths).filter(([path]) => path.startsWith('/api/tarefas'))
  .flatMap(([path, methods]) => Object.keys(methods).map(method => [method + path, path]));
const lines = [
  '/* Gerado a partir de evidencias/openapi.json. */',
  'export const rotas = ' + JSON.stringify(Object.fromEntries(paths), null, 2) + ' as const;',
];
await writeFile('src/rotas.ts', lines.join('\n') + '\n');
