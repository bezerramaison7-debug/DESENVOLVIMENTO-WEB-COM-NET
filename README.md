# DESENVOLVIMENTO-WEB-COM-NET

## Portal de Tarefas

Projeto didático para as oito práticas de Desenvolvimento Web com .NET.
O relatório individual descreve os resultados por roteiro. Este pacote reúne
código-fonte, migrations, contrato da API, scripts e evidências sanitizadas.

## Executar em desenvolvimento

Requisitos: SDK .NET 10 indicado em `global.json`, Node.js 24 LTS, npm, Python 3
e Chromium para a automação. Visual Studio 2026 pode abrir `PortalTarefas.sln`.
Na raiz do projeto, em Bash:

```bash
dotnet dev-certs https --trust
npm --prefix PortalTarefas.Frontend ci
npm --prefix PortalTarefas.Frontend run build
cp -r PortalTarefas.Frontend/dist/. PortalTarefas.Web/wwwroot/
dotnet user-secrets set 'ConnectionStrings:Portal' 'Data Source=portal-laboratorio.db' --project PortalTarefas.Web
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_HTTPS_PORT=7143
export ASPNETCORE_URLS='http://localhost:5080;https://localhost:7143'
read -rs -p 'Senha temporária do laboratório: ' Laboratorio__Senha
printf '\n'
export Laboratorio__Senha
dotnet run --project PortalTarefas.Web --no-launch-profile
```

Escolha senha de no mínimo 12 caracteres, com maiúscula, minúscula, número
e símbolo. A criação de `usuario` e `admin` exige esse valor externo em
Development. As contas já existentes conservam a senha do banco e o relatório
não registra credenciais. Para o frontend, abra https://localhost:7143/tarefas.
A interface MVC está em `/mvc/Tarefas`, a documentação local em `/docs`, o
contrato em `/openapi/v1.json` e a saúde em `/health/live` e `/health/ready`.

Em desenvolvimento, Vite pode usar o proxy definido em `vite.config.ts`.
Para testar sessões com cookies Secure, prefira a URL HTTPS integrada acima.
A política CORS no servidor permite leitura GET só para a origem declarada.
O contrato também pode ser baixado da aplicação em desenvolvimento e
regenerado com `npm --prefix PortalTarefas.Frontend run generate`.

## Reproduzir evidências

O script de integração usa uma base temporária e uma dependência controlável
em loopback. Na primeira execução, caso o Chromium do sistema esteja indisponível,
instale-o com `npx playwright install chromium` no diretório do frontend.
O navegador executa trajetos de visitante, usuário comum e administrador.

```bash
PORTAL_EVIDENCIAS="$PWD/novas-evidencias" ./tests/pipeline.sh
PORTAL_FALHAR_TESTE=1 ./tests/pipeline.sh
```

O segundo comando introduz uma falha depois da verificação principal e deve
terminar com código diferente de zero antes de `dotnet publish`. Execute em
checkout separado para evitar confundir artefatos de execuções anteriores.
O workflow em `.github/workflows/portal.yml` executa os mesmos passos em PRs
e na branch principal. O PDF identifica os passos validados localmente.
Para reproduzir em um checkout limpo, clone este repositório.
A revisão da validação local original consta em `evidencias/publicacao.txt`.

## Banco e atualização

O projeto adota SQLite por ser um banco relacional local sem servidor.
O aceite dessa escolha pelo professor pode ser necessário conforme o roteiro 3.
A inicialização aplica migrations e seed. O SQL está em `evidencias/migrations.sql`.
Em instalações com dados reais, faça backup e aplique migrations em etapa separada.
A atualização e uma linha de auditoria são gravadas juntas. Um conflito devolve
HTTP 409 e a tela de edição pede recarregamento para obter a versão atual.

## Contêiner e operação de teste

```bash
./tests/imagem.sh
```

O script constrói uma imagem identificada pelo commit, inicia com conexão externa,
verifica o Health Check e remove o contêiner temporário. Em uma implantação,
monte um volume com permissões para o usuário da imagem em `/data`, forneça
`ConnectionStrings__Portal` pelo ambiente e configure HTTPS no proxy de entrada.
Persista também as chaves do Data Protection em volume com acesso restrito caso
seja necessário preservar sessões durante a substituição do contêiner.
As contas de laboratório ficam restritas a Development. Um ambiente compartilhado
requer provisionamento de contas autorizado antes de validar login.

Promoção prevista: publicar a imagem vinculada ao commit em registro autorizado
com identidade federada, implantar o digest em ambiente de teste, confirmar os
endpoints de saúde e a navegação, registrar a revisão e promover o mesmo digest
após aprovação. Para retornar, aponte o ambiente para o digest anterior, execute
o smoke test e avalie a compatibilidade do esquema antes da troca. Use um backup
testado quando a reversão exigir também voltar o banco. O repositório não inclui
chaves de registro, credenciais de produção ou tokens de pipeline.

## Localização das evidências

| Roteiro | Arquivos principais |
| --- | --- |
| 1 | Program.cs, Infrastructure.cs, DiagnosticoController e http.json |
| 2 | Controllers/TarefasController.cs, Views e capturas r2 |
| 3 | Data.cs, Seed.cs, Migrations e migrations.sql |
| 4 | Controllers/ApiController.cs, openapi.json, scripts/generate.mjs e contrato.ts |
| 5 | PortalTarefas.Frontend/src, testes de navegador e capturas r5 |
| 6 | Controllers/SessaoController.cs, Infrastructure.cs e capturas r6 |
| 7 | Program.cs, Infrastructure.cs, tests/dependencia.py e telemetria.txt |
| 8 | Dockerfile, workflow, scripts pipeline.sh/imagem.sh e logs da validação |
