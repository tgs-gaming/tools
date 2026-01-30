
# TGS Package Manager (Unity)

Ferramenta de Editor para listar, instalar e atualizar pacotes Unity hospedados em um repositorio GitHub.
Ela le um manifest e, para cada pacote, consulta a branch `tool/<id-do-pacote>` para obter o `package.json`
e as tags do repositorio para descobrir as versoes disponiveis.

## O que ela faz
- Lista pacotes disponiveis a partir do `manifest.json`.
- Le o `package.json` direto da branch do pacote para obter nome, descricao e requisito de Unity.
- Busca as tags do repositorio para montar a lista de versoes (ex.: `com.company.packageC-v1.0.0`).
- Instala a ponta da branch do pacote ao clicar em **Install Latest**.
- Permite instalar uma versao especifica (selecionada a partir das tags).
- Mostra atualizacoes disponiveis comparando a versao instalada com a mais recente.
- Indica incompatibilidade de Unity e bloqueia a instalacao nesses casos.

## Estrutura esperada do manifest
Arquivo `manifest.json`:
```json
{
  "schemaVersion": "1.0",
  "generatedAt": "2026-01-29",
  "repository": {
    "owner": "example",
    "name": "company-tools",
    "defaultBranch": "main"
  },
  "packages": [
    "com.company.packageA",
    "com.company.packageC"
  ]
}
```

## Como adicionar um novo pacote
1) Crie o pacote no repositorio
   - Use o pacote `tool/com.tgs.template` como base.
   - Crie um branch novo a partir dele: `tool/<id-do-pacote>`.
   - Dentro desse branch, ajuste o `package.json` (nome, displayName, descricao, unity, etc).

2) Adicione o ID no manifest
   - Inclua o `id` do pacote em `packages` no `manifest.json`.

3) Crie tags para as versoes
   - O manager identifica versoes pelo padrao:
     `com.company.packageC-v1.0.0`
   - Ou seja: `<id-do-pacote>-v<versao>`

## Observacoes importantes
- A lista de versoes vem das tags do repositorio, nao do `package.json`.
- O **Install Latest** instala sempre a ponta da branch `tool/<id-do-pacote>`.
- Se nao houver tags para um pacote, o manager usa o campo `version` do `package.json`
  apenas para exibir uma versao unica.

